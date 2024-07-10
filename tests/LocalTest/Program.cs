﻿using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Graphics.Vulkan;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace LocalTest
{
    class Window : GameWindow
    {
        VkInstance VulkanInstance;
        VkPhysicalDevice PhysicalDevice;
        VkDevice Device;

        VkQueue GraphicsQueue;
        VkRenderPass RenderPass;
        VkCommandBuffer CommandBuffer;

        VkExtent2D SwapchainExtents;
        VkSwapchainKHR Swapchain;
        VkImageView[] SwapchainImageViews;
        VkFramebuffer[] SwapchainFramebuffers;

        VkSemaphore ImageAvailableSemaphore;
        VkSemaphore RenderFinishedSemaphore;
        VkFence InFlightFence;

        static void Main(string[] args)
        {
            GameWindowSettings gwSettings = new GameWindowSettings()
            {
                UpdateFrequency = 250,
            };

            NativeWindowSettings nwSettings = new NativeWindowSettings()
            {
                API = ContextAPI.NoAPI,
                //APIVersion = new Version(3, 3),
                AutoLoadBindings = true,
                Flags = 0,
                IsEventDriven = false,
                Profile = 0,
                ClientSize = (800, 600),
                StartFocused = true,
                StartVisible = true,
                Title = "OpenTK Vulkan Bindings Test",
                WindowBorder = WindowBorder.Resizable,
                WindowState = WindowState.Normal,
            };

            using (Window window = new Window(gwSettings, nwSettings))
            {
                window.Run();
            }
        }

        public Window(GameWindowSettings gwSettings, NativeWindowSettings nwSettings) : base(gwSettings, nwSettings)
        {
        }

        public uint VK_MAKE_API_VERSION(uint variant, uint major, uint minor, uint patch) =>
            ((((uint)(variant)) << 29) | (((uint)(major)) << 22) | (((uint)(minor)) << 12) | ((uint)(patch)));

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetModuleHandle(IntPtr m);

        protected unsafe override void OnLoad()
        {
            base.OnLoad();

            VKLoader.Init();

            VkApplicationInfo applicationInfo;
            applicationInfo.sType = VkStructureType.StructureTypeApplicationInfo;
            applicationInfo.pNext = null;
            applicationInfo.pApplicationName = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference("OpenTK Vulkan Test"u8));
            applicationInfo.applicationVersion = 1;
            applicationInfo.pEngineName = null;
            applicationInfo.engineVersion = 0;
            applicationInfo.apiVersion = VK_MAKE_API_VERSION(0, 1, 3, 0);

            byte** extensionsStr = GLFW.GetRequiredInstanceExtensionsRaw(out uint extensionCount);

            VkInstanceCreateInfo instanceCreateInfo;
            instanceCreateInfo.sType = VkStructureType.StructureTypeInstanceCreateInfo;
            instanceCreateInfo.pNext = null;
            instanceCreateInfo.flags = 0;
            instanceCreateInfo.pApplicationInfo = &applicationInfo;
            instanceCreateInfo.enabledLayerCount = 0;
            instanceCreateInfo.ppEnabledLayerNames = null;
            instanceCreateInfo.enabledExtensionCount = extensionCount;
            instanceCreateInfo.ppEnabledExtensionNames = extensionsStr;

            VkInstance instance;
            VkResult result = Vk.CreateInstance(&instanceCreateInfo, null, &instance);
            if (result != VkResult.Success)
            {
                throw new Exception($"Was not able to create vk instance: {result}");
            }
            VulkanInstance = instance;

            VKLoader.SetInstance(VulkanInstance);

            uint deviceCount = default;
            result = Vk.EnumeratePhysicalDevices(VulkanInstance, &deviceCount, null);
            if (result != VkResult.Success)
            {
                throw new Exception($"Was not able to enumerate physical devices count: {result}");
            }
            Span<VkPhysicalDevice> physicalDevices = new VkPhysicalDevice[deviceCount];
            result = Vk.EnumeratePhysicalDevices(VulkanInstance, &deviceCount, (VkPhysicalDevice*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(physicalDevices)));
            if (result != VkResult.Success)
            {
                throw new Exception($"Was not able to enumerate physical devices: {result}");
            }

            PhysicalDevice = physicalDevices[0];

            uint deviceExtensionCount = 0;
            result = Vk.EnumerateDeviceExtensionProperties(PhysicalDevice, null, &deviceExtensionCount, null);

            Span<VkExtensionProperties> extensions = new VkExtensionProperties[deviceExtensionCount];
            result = Vk.EnumerateDeviceExtensionProperties(PhysicalDevice, null, &deviceExtensionCount, (VkExtensionProperties*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(extensions)));

            bool foundSwapchain = false;
            for (int i = 0; i < extensions.Length; i++)
            {
                ReadOnlySpan<byte> name = extensions[i].extensionName;
                name = name.Slice(0, name.IndexOf((byte)0));

                if (name.SequenceEqual("VK_KHR_swapchain"u8))
                {
                    foundSwapchain = true;
                }
            }

            if (foundSwapchain == false)
            {
                throw new Exception("Couldn't find VK_KHR_swapchain extension!");
            }
            
            int queueFamily = FindQueueFamily(PhysicalDevice);

            VkDeviceQueueCreateInfo queueCreateInfo;
            queueCreateInfo.sType = VkStructureType.StructureTypeDeviceQueueCreateInfo;
            queueCreateInfo.pNext = null;
            queueCreateInfo.flags = 0;
            queueCreateInfo.queueFamilyIndex = (uint)queueFamily;
            queueCreateInfo.queueCount = 1;
            float priority = 1.0f;
            queueCreateInfo.pQueuePriorities = &priority;

            VkPhysicalDeviceFeatures deviceFeatures = default;

            VkDeviceCreateInfo deviceCreateInfo;
            deviceCreateInfo.sType = VkStructureType.StructureTypeDeviceCreateInfo;
            deviceCreateInfo.pNext = null;
            deviceCreateInfo.flags = 0;
            deviceCreateInfo.queueCreateInfoCount = 1;
            deviceCreateInfo.pQueueCreateInfos = &queueCreateInfo;
            deviceCreateInfo.enabledLayerCount = 0;
            deviceCreateInfo.ppEnabledLayerNames = null;
            deviceCreateInfo.enabledExtensionCount = 1;
            ReadOnlySpan<byte> extensionNames = "VK_KHR_swapchain"u8;
            
            fixed (byte* extptr = extensionNames)
            {
                deviceCreateInfo.ppEnabledExtensionNames = &extptr;
                deviceCreateInfo.pEnabledFeatures = &deviceFeatures;

                VkDevice device;
                result = Vk.CreateDevice(PhysicalDevice, &deviceCreateInfo, null, &device);
                Device = device;
            }
            if (result != VkResult.Success)
            {
                throw new Exception($"Was not able to create logical device: {result}");
            }
            

            VkQueue graphicsQueue;
            Vk.GetDeviceQueue(Device, (uint)queueFamily, 0, &graphicsQueue);
            GraphicsQueue = graphicsQueue;

            result = (VkResult)GLFW.CreateWindowSurface(new VkHandle(instance.Handle), WindowPtr, null, out VkHandle surfaceH);
            VkSurfaceKHR surface = (VkSurfaceKHR)surfaceH.Handle;

            VkSurfaceCapabilitiesKHR surfaceCaps;
            result = Vk.GetPhysicalDeviceSurfaceCapabilitiesKHR(PhysicalDevice, surface, &surfaceCaps);

            uint surfaceFormatCount;
            result = Vk.GetPhysicalDeviceSurfaceFormatsKHR(PhysicalDevice, surface, &surfaceFormatCount, null);

            Span<VkSurfaceFormatKHR> surfaceFormats = stackalloc VkSurfaceFormatKHR[(int)surfaceFormatCount];
            result = Vk.GetPhysicalDeviceSurfaceFormatsKHR(PhysicalDevice, surface, &surfaceFormatCount, (VkSurfaceFormatKHR*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(surfaceFormats)));

            VkSurfaceFormatKHR choosenFormat = default;
            bool foundFormat = false;
            for (int i = 0; i < surfaceFormats.Length; i++)
            {
                if (surfaceFormats[i].format == VkFormat.FormatR8g8b8a8Srgb)
                {
                    choosenFormat = surfaceFormats[i];
                    foundFormat = true;
                    break;
                }
            }
            if (foundFormat == false)
            {
                choosenFormat = surfaceFormats[0];
            }


            uint presentModeCount = 0;
            result = Vk.GetPhysicalDeviceSurfacePresentModesKHR(PhysicalDevice, surface, &presentModeCount, null);

            Span<VkPresentModeKHR> presentModes = stackalloc VkPresentModeKHR[(int)presentModeCount];
            result = Vk.GetPhysicalDeviceSurfacePresentModesKHR(PhysicalDevice, surface, &presentModeCount, (VkPresentModeKHR*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(presentModes)));

            VkSwapchainCreateInfoKHR swapchainCreate;
            swapchainCreate.sType = VkStructureType.StructureTypeSwapchainCreateInfoKhr;
            swapchainCreate.pNext = null;
            swapchainCreate.flags = 0;
            swapchainCreate.surface = surface;
            swapchainCreate.minImageCount = surfaceCaps.minImageCount;
            swapchainCreate.imageFormat = choosenFormat.format;
            swapchainCreate.imageColorSpace = choosenFormat.colorSpace;
            swapchainCreate.imageExtent = surfaceCaps.currentExtent;
            swapchainCreate.imageArrayLayers = 1;
            swapchainCreate.imageUsage = VkImageUsageFlagBits.ImageUsageColorAttachmentBit;
            swapchainCreate.imageSharingMode = VkSharingMode.SharingModeExclusive;
            swapchainCreate.queueFamilyIndexCount = 0;
            swapchainCreate.pQueueFamilyIndices = null;
            swapchainCreate.preTransform = surfaceCaps.currentTransform;
            swapchainCreate.compositeAlpha = VkCompositeAlphaFlagBitsKHR.CompositeAlphaOpaqueBitKhr;
            // FIXME: Get from the possible present modes..
            swapchainCreate.presentMode = VkPresentModeKHR.PresentModeFifoKhr;
            swapchainCreate.clipped = 1;
            swapchainCreate.oldSwapchain = VkSwapchainKHR.Zero;

            VkSwapchainKHR swapchain;
            result = Vk.CreateSwapchainKHR(Device, &swapchainCreate, null, &swapchain);
            Swapchain = swapchain;

            SwapchainExtents = swapchainCreate.imageExtent;

            uint swapchainImageCount;
            result = Vk.GetSwapchainImagesKHR(Device, swapchain, &swapchainImageCount, null);

            Span<VkImage> swapchainImages = new VkImage[swapchainImageCount];
            result = Vk.GetSwapchainImagesKHR(Device, swapchain, &swapchainImageCount, (VkImage*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(swapchainImages)));

            VkAttachmentDescription colorAttachment;
            colorAttachment.flags = 0;
            colorAttachment.format = choosenFormat.format;
            colorAttachment.samples = VkSampleCountFlagBits.SampleCount1Bit;
            colorAttachment.loadOp = VkAttachmentLoadOp.AttachmentLoadOpClear;
            colorAttachment.storeOp = VkAttachmentStoreOp.AttachmentStoreOpStore;
            colorAttachment.stencilLoadOp = VkAttachmentLoadOp.AttachmentLoadOpClear;
            colorAttachment.stencilStoreOp = VkAttachmentStoreOp.AttachmentStoreOpStore;
            colorAttachment.initialLayout = VkImageLayout.ImageLayoutUndefined;
            colorAttachment.finalLayout = VkImageLayout.ImageLayoutPresentSrcKhr;

            VkAttachmentReference colorAttachmentRef;
            colorAttachmentRef.attachment = 0;
            colorAttachmentRef.layout = VkImageLayout.ImageLayoutColorAttachmentOptimal;

            VkSubpassDescription subpass;
            subpass.flags = 0;
            subpass.pipelineBindPoint = VkPipelineBindPoint.PipelineBindPointGraphics;
            subpass.inputAttachmentCount = 0;
            subpass.pInputAttachments = null;
            subpass.colorAttachmentCount = 1;
            subpass.pColorAttachments = &colorAttachmentRef;
            subpass.pResolveAttachments = null;
            subpass.pDepthStencilAttachment = null;
            subpass.preserveAttachmentCount = 0;
            subpass.pPreserveAttachments = null;

            VkRenderPassCreateInfo renderPassCreateInfo;
            renderPassCreateInfo.sType = VkStructureType.StructureTypeRenderPassCreateInfo;
            renderPassCreateInfo.pNext = null;
            renderPassCreateInfo.flags = 0;
            renderPassCreateInfo.attachmentCount = 1;
            renderPassCreateInfo.pAttachments = &colorAttachment;
            renderPassCreateInfo.subpassCount = 1;
            renderPassCreateInfo.pSubpasses = &subpass;
            renderPassCreateInfo.dependencyCount = 0;
            renderPassCreateInfo.pDependencies = null;

            VkRenderPass renderPass;
            result = Vk.CreateRenderPass(Device, &renderPassCreateInfo, null, &renderPass);
            RenderPass = renderPass;

            SwapchainImageViews = new VkImageView[swapchainImages.Length];
            SwapchainFramebuffers = new VkFramebuffer[swapchainImages.Length];
            for (int i = 0; i < swapchainImages.Length; i++)
            {
                VkImageViewCreateInfo imgViewCreate;
                imgViewCreate.sType = VkStructureType.StructureTypeImageViewCreateInfo;
                imgViewCreate.pNext = null;
                imgViewCreate.flags = 0;
                imgViewCreate.image = swapchainImages[i];
                imgViewCreate.viewType = VkImageViewType.ImageViewType2d;
                imgViewCreate.format = VkFormat.FormatR8g8b8a8Srgb;
                imgViewCreate.components.r = VkComponentSwizzle.ComponentSwizzleIdentity;
                imgViewCreate.components.g = VkComponentSwizzle.ComponentSwizzleIdentity;
                imgViewCreate.components.b = VkComponentSwizzle.ComponentSwizzleIdentity;
                imgViewCreate.components.a = VkComponentSwizzle.ComponentSwizzleIdentity;
                imgViewCreate.subresourceRange.aspectMask = VkImageAspectFlagBits.ImageAspectColorBit;
                imgViewCreate.subresourceRange.baseMipLevel = 0;
                imgViewCreate.subresourceRange.levelCount = 1;
                imgViewCreate.subresourceRange.baseArrayLayer = 0;
                imgViewCreate.subresourceRange.layerCount = 1;

                VkImageView imgView;
                result = Vk.CreateImageView(Device, &imgViewCreate, null, &imgView);
                SwapchainImageViews[i] = imgView;

                VkFramebufferCreateInfo fbCreate;
                fbCreate.sType = VkStructureType.StructureTypeFramebufferCreateInfo;
                fbCreate.pNext = null;
                fbCreate.flags = 0;
                fbCreate.renderPass = renderPass;
                fbCreate.attachmentCount = 1;
                fbCreate.pAttachments = &imgView;
                fbCreate.width = swapchainCreate.imageExtent.width;
                fbCreate.height = swapchainCreate.imageExtent.height;
                fbCreate.layers = 1;

                VkFramebuffer framebuffer;
                result = Vk.CreateFramebuffer(Device, &fbCreate, null, &framebuffer);
                SwapchainFramebuffers[i] = framebuffer;
            }

            VkCommandPoolCreateInfo commandPoolCreate;
            commandPoolCreate.sType = VkStructureType.StructureTypeCommandPoolCreateInfo;
            commandPoolCreate.pNext = null;
            commandPoolCreate.flags = VkCommandPoolCreateFlagBits.CommandPoolCreateResetCommandBufferBit;
            commandPoolCreate.queueFamilyIndex = (uint)queueFamily;

            VkCommandPool commandPool;
            result = Vk.CreateCommandPool(Device, &commandPoolCreate, null, &commandPool);

            VkCommandBufferAllocateInfo cmdBufferAlloc;
            cmdBufferAlloc.sType = VkStructureType.StructureTypeCommandBufferAllocateInfo;
            cmdBufferAlloc.pNext = null;
            cmdBufferAlloc.commandPool = commandPool;
            cmdBufferAlloc.level = VkCommandBufferLevel.CommandBufferLevelPrimary;
            cmdBufferAlloc.commandBufferCount = 1;

            VkCommandBuffer commandBuffer;
            result = Vk.AllocateCommandBuffers(Device, &cmdBufferAlloc, &commandBuffer);
            CommandBuffer = commandBuffer;

            {
                VkSemaphoreCreateInfo semaphoreCreate;
                semaphoreCreate.sType = VkStructureType.StructureTypeExportSemaphoreCreateInfo;
                semaphoreCreate.pNext = null;
                semaphoreCreate.flags = 0;

                VkSemaphore imageAvail;
                result = Vk.CreateSemaphore(Device, &semaphoreCreate, null, &imageAvail);
                ImageAvailableSemaphore = imageAvail;

                VkSemaphore renderFinished;
                result = Vk.CreateSemaphore(Device, &semaphoreCreate, null, &renderFinished);
                RenderFinishedSemaphore = renderFinished;

                VkFenceCreateInfo fenceCreate;
                fenceCreate.sType = VkStructureType.StructureTypeFenceCreateInfo;
                fenceCreate.pNext = null;
                fenceCreate.flags = VkFenceCreateFlagBits.FenceCreateSignaledBit;

                VkFence inFlight;
                result = Vk.CreateFence(Device, &fenceCreate, null, &inFlight);
                InFlightFence = inFlight;
            }

            static int FindQueueFamily(VkPhysicalDevice physicalDevice)
            {
                uint propertyCount = 0;
                Vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &propertyCount, null);

                Span<VkQueueFamilyProperties> familyProperties = stackalloc VkQueueFamilyProperties[(int)propertyCount];
                fixed (VkQueueFamilyProperties* ptr = familyProperties)
                    Vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &propertyCount, ptr);

                for (int i = 0; i < propertyCount; i++)
                {
                    if ((familyProperties[i].queueFlags & VkQueueFlagBits.QueueGraphicsBit) != 0)
                        return i;
                }

                throw new Exception("Found no suitable queue family.");
            }
        }

        static unsafe void RecordCommandBuffer(VkCommandBuffer commandBuffer, VkRenderPass renderPass, VkFramebuffer framebuffer, VkExtent2D imageExtent, float time)
        {
            VkCommandBufferBeginInfo beginInfo;
            beginInfo.sType = VkStructureType.StructureTypeCommandBufferBeginInfo;
            beginInfo.pNext = null;
            beginInfo.flags = 0;
            beginInfo.pInheritanceInfo = null;

            VkResult result = Vk.BeginCommandBuffer(commandBuffer, &beginInfo);

            VkRenderPassBeginInfo renderPassInfo;
            renderPassInfo.sType = VkStructureType.StructureTypeRenderPassBeginInfo;
            renderPassInfo.pNext = null;
            renderPassInfo.renderPass = renderPass;
            renderPassInfo.framebuffer = framebuffer;
            renderPassInfo.renderArea.offset = new VkOffset2D() { x = 0, y = 0 };
            renderPassInfo.renderArea.extent = imageExtent;
            renderPassInfo.clearValueCount = 1;

            Color4<Rgba> color = new Color4<Hsva>(time / CycleTime, 1, 1, 1).ToRgba();

            VkClearValue clearValue = default;
            clearValue.color.float32[0] = color.X;
            clearValue.color.float32[1] = color.Y;
            clearValue.color.float32[2] = color.Z;
            clearValue.color.float32[3] = color.W;
            renderPassInfo.pClearValues = &clearValue;

            Vk.CmdBeginRenderPass(commandBuffer, &renderPassInfo, VkSubpassContents.SubpassContentsInline);

            Vk.CmdEndRenderPass(commandBuffer);

            result = Vk.EndCommandBuffer(commandBuffer);
        }

        protected unsafe override void OnUnload()
        {
<<<<<<< HEAD
            base.OnUnload();
<<<<<<< HEAD
=======
            base.OnRenderFrame(args);
>>>>>>> 849925e8f (First iteration of the vulkan bindings.)
=======

            Vk.DeviceWaitIdle(Device);
            Vk.DestroyDevice(Device, null);
            Vk.DestroyInstance(VulkanInstance, null);
>>>>>>> 4892cb779 (Added vulkan features and extension definitions to binder.)
        }

        const float CycleTime = 8.0f;
        float Time = 0;

        protected unsafe override void OnUpdateFrame(FrameEventArgs args)
        {
            base.OnUpdateFrame(args);

            Time += (float)args.Time;
            if (Time > CycleTime) Time = 0;

            VkResult result;

            fixed (VkFence* inFlightFencePtr = &InFlightFence)
            {
                result = Vk.WaitForFences(Device, 1, inFlightFencePtr, 1, ulong.MaxValue);
                Vk.ResetFences(Device, 1, inFlightFencePtr);
            }

            uint imageIndex;
            result = Vk.AcquireNextImageKHR(Device, Swapchain, ulong.MaxValue, ImageAvailableSemaphore, VkFence.Zero, &imageIndex);

            result = Vk.ResetCommandBuffer(CommandBuffer, 0);
            RecordCommandBuffer(CommandBuffer, RenderPass, SwapchainFramebuffers[imageIndex], SwapchainExtents, Time);

            fixed (VkSemaphore* imageAvailableSemaphorePtr = &ImageAvailableSemaphore)
            fixed (VkSemaphore* renderFinishedSemaphorePtr = &RenderFinishedSemaphore)
            fixed (VkCommandBuffer* commandBufferPtr = &CommandBuffer)
            fixed (VkSwapchainKHR* swapchainPtr = &Swapchain)
            {
                VkSubmitInfo submitInfo;
                submitInfo.sType = VkStructureType.StructureTypeSubmitInfo;
                submitInfo.pNext = null;
                submitInfo.waitSemaphoreCount = 1;
                submitInfo.pWaitSemaphores = imageAvailableSemaphorePtr;
                VkPipelineStageFlagBits stage = VkPipelineStageFlagBits.PipelineStageColorAttachmentOutputBit;
                submitInfo.pWaitDstStageMask = &stage;
                submitInfo.commandBufferCount = 1;
                submitInfo.pCommandBuffers = commandBufferPtr;
                submitInfo.signalSemaphoreCount = 1;
                submitInfo.pSignalSemaphores = renderFinishedSemaphorePtr;

                Vk.QueueSubmit(GraphicsQueue, 1, &submitInfo, InFlightFence);

                VkPresentInfoKHR presentInfo;
                presentInfo.sType = VkStructureType.StructureTypePresentInfoKhr;
                presentInfo.pNext = null;
                presentInfo.waitSemaphoreCount = 1;
                presentInfo.pWaitSemaphores = renderFinishedSemaphorePtr;
                presentInfo.swapchainCount = 1;
                presentInfo.pSwapchains = swapchainPtr;
                presentInfo.pImageIndices = &imageIndex;
                presentInfo.pResults = null;

                result = Vk.QueuePresentKHR(GraphicsQueue, &presentInfo);
            }
        }

        float time = 0;

        protected override void OnRenderFrame(FrameEventArgs args)
        {
            base.OnRenderFrame(args);


        }

        protected override void OnFramebufferResize(FramebufferResizeEventArgs e)
        {
            base.OnFramebufferResize(e);

            //GL.Viewport(0, 0, e.Width, e.Height);
        }

        protected override void OnResize(ResizeEventArgs e)
        {
            base.OnResize(e);
        }

        protected override void OnMove(WindowPositionEventArgs e)
        {
            base.OnMove(e);
        }
    }
}
