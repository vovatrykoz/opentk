﻿using System;
using System.Diagnostics;
using OpenTK.Core.Platform;
using OpenTK.Core.Utility;
using static OpenTK.Platform.Native.macOS.ObjC;

namespace OpenTK.Platform.Native.macOS
{
    public class MacOSIconComponent : IIconComponent
    {
        internal static readonly IntPtr macwindow = ToNSString("macwindow"u8);
        internal static readonly IntPtr exclamationmark_octagon_fill = ToNSString("exclamationmark.octagon.fill"u8);
        internal static readonly IntPtr info_circle_fill = ToNSString("info.circle.fill"u8);
        internal static readonly IntPtr questionmark_circle_fill = ToNSString("questionmark.circle.fill"u8);
        internal static readonly IntPtr lock_fill = ToNSString("lock.fill"u8);
        internal static readonly IntPtr exclamationmark_triangle_fill = ToNSString("exclamationmark.triangle.fill"u8);
        // FIXME: Are we allowed to use apple.logo? Probably not...
        // FIXME: Try apple.terminal first, if it doesn't exist try terminal
        internal static readonly IntPtr apple_terminal = ToNSString("apple.terminal"u8);
        internal static readonly IntPtr terminal = ToNSString("terminal"u8);

        // FIXME: Should we describe the colors here?
        internal static readonly IntPtr desc_default = ToNSString("An icon representing a macOS window."u8);
        internal static readonly IntPtr desc_error = ToNSString("An error exclamationmark inside of a red octagon."u8);
        internal static readonly IntPtr desc_information = ToNSString("An information i inside a filled circle."u8);
        internal static readonly IntPtr desc_question = ToNSString("A questionmark inside a filled circle."u8);
        internal static readonly IntPtr desc_shield = ToNSString("A closed lock icon."u8);
        internal static readonly IntPtr desc_warning = ToNSString("A warning exclamation mark inside a yellow triangle."u8);
        // FIXME: Different descriptions for the fallbacks?
        internal static readonly IntPtr desc_operatingsystem = ToNSString("A logo of a terminal."u8);

        internal static readonly IntPtr NSCalibratedRGBColorSpace = GetStringConstant(AppKitLibrary, "NSCalibratedRGBColorSpace"u8);

        internal static readonly ObjCClass NSImageClass = objc_getClass("NSImage"u8);
        internal static readonly ObjCClass NSImageSymbolConfigurationClass = objc_getClass("NSImageSymbolConfiguration"u8);
        internal static readonly ObjCClass NSBitmapImageRep = objc_getClass("NSBitmapImageRep"u8);

        internal static readonly SEL selImageWithSystemSymbolName_AccessibilityDescription = sel_registerName("imageWithSystemSymbolName:accessibilityDescription:"u8);

        internal static readonly SEL selConfigurationPreferringMulticolor = sel_registerName("configurationPreferringMulticolor"u8);
        internal static readonly SEL selConfigurationWithScale = sel_registerName("configurationWithScale:"u8);
        internal static readonly SEL selConfigurationByApplyingConfiguration = sel_registerName("configurationByApplyingConfiguration:"u8);

        internal static readonly SEL selInitWithSize = sel_registerName("initWithSize:"u8);
        internal static readonly SEL selAddRepresentation = sel_registerName("addRepresentation:"u8);

        internal static readonly SEL selInitWithBitmapDataPlanes_PixelsWide_PixelsHigh_BitsPerSample_SamplesPerPixel_HasAlpha_IsPlanar_ColorSpaceName_BitmapFormat_BytesPerRow_BitsPerPixel = sel_registerName("initWithBitmapDataPlanes:pixelsWide:pixelsHigh:bitsPerSample:samplesPerPixel:hasAlpha:isPlanar:colorSpaceName:bitmapFormat:bytesPerRow:bitsPerPixel:"u8);
        internal static readonly SEL selBitmapData = sel_registerName("bitmapData"u8);
        internal static readonly SEL selSetSize = sel_registerName("setSize:"u8);
        internal static readonly SEL selSize = sel_registerName("size"u8);


        public string Name => nameof(MacOSIconComponent);

        public PalComponents Provides => PalComponents.WindowIcon;

        public ILogger? Logger { get; set; }

        public void Initialize(PalComponents which)
        {
            if (which != PalComponents.WindowIcon)
            {
                throw new Exception("MacOSIconComponent can only initialize the WindowIcon component.");
            }


        }

        public bool CanLoadSystemIcons => true;

        public IconHandle Create(SystemIconType systemIcon)
        {
            IntPtr symbolName = 0;
            IntPtr desc = 0;
            switch (systemIcon)
            {
                case SystemIconType.Default:
                    symbolName = macwindow;
                    desc = desc_default;
                    break;
                case SystemIconType.Error:
                    symbolName = exclamationmark_octagon_fill;
                    desc = desc_error;
                    break;
                case SystemIconType.Information:
                    symbolName = info_circle_fill;
                    desc = desc_information;
                    break;
                case SystemIconType.Question:
                    symbolName = questionmark_circle_fill;
                    desc = desc_question;
                    break;
                case SystemIconType.Shield:
                    symbolName = lock_fill;
                    desc = desc_shield;
                    break;
                case SystemIconType.Warning:
                    symbolName = exclamationmark_triangle_fill;
                    desc = desc_warning;
                    break;
                case SystemIconType.OperatingSystem:
                    // FIXME: Can we use some other logo here?
                    symbolName = terminal;
                    desc = desc_operatingsystem;
                    break;
            }

            // FIXME: Load colored icons!
            IntPtr image = objc_msgSend_IntPtr((IntPtr)NSImageClass, selImageWithSystemSymbolName_AccessibilityDescription, symbolName, desc);

            IntPtr configuration = objc_msgSend_IntPtr((IntPtr)NSImageSymbolConfigurationClass, selConfigurationPreferringMulticolor);
            configuration = objc_msgSend_IntPtr(
                configuration,
                selConfigurationByApplyingConfiguration,
                objc_msgSend_IntPtr((IntPtr)NSImageSymbolConfigurationClass, selConfigurationWithScale, (nint)NSImageSymbolScale.Large));

            NSIconHandle nsicon = new NSIconHandle(image, configuration);

            return nsicon;
        }

        public IconHandle Create(int width, int height, ReadOnlySpan<byte> data)
        {
            IntPtr bitmap = objc_msgSend_IntPtr((IntPtr)NSBitmapImageRep, Alloc);
            // FIXME: BOOL
            bitmap = objc_msgSend_IntPtr(bitmap,
                selInitWithBitmapDataPlanes_PixelsWide_PixelsHigh_BitsPerSample_SamplesPerPixel_HasAlpha_IsPlanar_ColorSpaceName_BitmapFormat_BytesPerRow_BitsPerPixel,
                IntPtr.Zero,
                width,
                height,
                8,
                4,
                true,
                false,
                NSCalibratedRGBColorSpace,
                (nuint)NSBitmapFormat.AlphaNonpremultiplied,
                width * 4,
                32);

            Debug.Assert(bitmap != 0);

            unsafe
            {
                Span<byte> dataSpan = new Span<byte>((void*)objc_msgSend_IntPtr(bitmap, selBitmapData), width * height * 4);
                data.CopyTo(dataSpan);
            }

            // FIXME: Dissociate the resolution from the screen size.
            // objc_msgSend(bitmap, selSetSize, new NSSize(width, height));

            IntPtr nsimage = objc_msgSend_IntPtr(objc_msgSend_IntPtr((IntPtr)NSImageClass, Alloc), selInitWithSize, new NSSize(width, height));
            objc_msgSend(nsimage, selAddRepresentation, bitmap);

            NSIconHandle nsicon = new NSIconHandle(nsimage);

            return nsicon;
        }

        public void Destroy(IconHandle handle)
        {
            NSIconHandle nsicon = handle.As<NSIconHandle>(this);

            objc_msgSend(nsicon.Image, Release);
            nsicon.Image = IntPtr.Zero;
            if (nsicon.SymbolConfiguration != IntPtr.Zero)
            {
                objc_msgSend(nsicon.SymbolConfiguration, Release);
                nsicon.SymbolConfiguration = IntPtr.Zero;
            }
        }

        public void GetSize(IconHandle handle, out int width, out int height)
        {
            NSIconHandle nsicon = handle.As<NSIconHandle>(this);

            NSSize size = objc_msgSend_NSSize(nsicon.Image, selSize);

            // FIXME: Do we want to get the size or the resolution?
            width = (int)size.width;
            height = (int)size.height;
        }

        // FIXME: Method for getting icon image data?
    }
}

