using FileFormat.Core;

namespace FileFormat.Core.PixelFormats;

public readonly struct Bgra32 : IRawPixelFormat<Bgra32> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.Bgra32); }
public readonly struct Rgba32 : IRawPixelFormat<Rgba32> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.Rgba32); }
public readonly struct Argb32 : IRawPixelFormat<Argb32> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.Argb32); }
public readonly struct Rgb24 : IRawPixelFormat<Rgb24> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.Rgb24); }
public readonly struct Bgr24 : IRawPixelFormat<Bgr24> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.Bgr24); }
public readonly struct Gray8 : IRawPixelFormat<Gray8> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.Gray8); }
public readonly struct Gray16 : IRawPixelFormat<Gray16> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.Gray16); }
public readonly struct GrayAlpha16 : IRawPixelFormat<GrayAlpha16> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.GrayAlpha16); }
public readonly struct GrayAlpha32 : IRawPixelFormat<GrayAlpha32> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.GrayAlpha32); }

public readonly struct Indexed1 : IRawPixelFormat<Indexed1> { public static RawPixelFormatTraits Traits => RawPixelFormats.Indexed(1); }
public readonly struct Indexed2 : IRawPixelFormat<Indexed2> { public static RawPixelFormatTraits Traits => RawPixelFormats.Indexed(2); }
public readonly struct Indexed3 : IRawPixelFormat<Indexed3> { public static RawPixelFormatTraits Traits => RawPixelFormats.Indexed(3); }
public readonly struct Indexed4 : IRawPixelFormat<Indexed4> { public static RawPixelFormatTraits Traits => RawPixelFormats.Indexed(4); }
public readonly struct Indexed5 : IRawPixelFormat<Indexed5> { public static RawPixelFormatTraits Traits => RawPixelFormats.Indexed(5); }
public readonly struct Indexed6 : IRawPixelFormat<Indexed6> { public static RawPixelFormatTraits Traits => RawPixelFormats.Indexed(6); }
public readonly struct Indexed7 : IRawPixelFormat<Indexed7> { public static RawPixelFormatTraits Traits => RawPixelFormats.Indexed(7); }
public readonly struct Indexed8 : IRawPixelFormat<Indexed8> { public static RawPixelFormatTraits Traits => RawPixelFormats.Indexed(8); }
public readonly struct Indexed9 : IRawPixelFormat<Indexed9> { public static RawPixelFormatTraits Traits => RawPixelFormats.Indexed(9); }
public readonly struct Indexed10 : IRawPixelFormat<Indexed10> { public static RawPixelFormatTraits Traits => RawPixelFormats.Indexed(10); }
public readonly struct Indexed11 : IRawPixelFormat<Indexed11> { public static RawPixelFormatTraits Traits => RawPixelFormats.Indexed(11); }
public readonly struct Indexed12 : IRawPixelFormat<Indexed12> { public static RawPixelFormatTraits Traits => RawPixelFormats.Indexed(12); }
public readonly struct Indexed13 : IRawPixelFormat<Indexed13> { public static RawPixelFormatTraits Traits => RawPixelFormats.Indexed(13); }
public readonly struct Indexed14 : IRawPixelFormat<Indexed14> { public static RawPixelFormatTraits Traits => RawPixelFormats.Indexed(14); }
public readonly struct Indexed15 : IRawPixelFormat<Indexed15> { public static RawPixelFormatTraits Traits => RawPixelFormats.Indexed(15); }
public readonly struct Indexed16 : IRawPixelFormat<Indexed16> { public static RawPixelFormatTraits Traits => RawPixelFormats.Indexed(16); }

public readonly struct Rgba64 : IRawPixelFormat<Rgba64> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.Rgba64); }
public readonly struct Rgb48 : IRawPixelFormat<Rgb48> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.Rgb48); }
public readonly struct Rgb565 : IRawPixelFormat<Rgb565> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.Rgb565); }
public readonly struct Gray10 : IRawPixelFormat<Gray10> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.Gray10); }
public readonly struct Rgb30 : IRawPixelFormat<Rgb30> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.Rgb30); }

public readonly struct GrayF16 : IRawPixelFormat<GrayF16> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.GrayF16); }
public readonly struct GrayAlphaF16 : IRawPixelFormat<GrayAlphaF16> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.GrayAlphaF16); }
public readonly struct RgbF16 : IRawPixelFormat<RgbF16> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.RgbF16); }
public readonly struct RgbaF16 : IRawPixelFormat<RgbaF16> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.RgbaF16); }
public readonly struct GrayF32 : IRawPixelFormat<GrayF32> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.GrayF32); }
public readonly struct GrayAlphaF32 : IRawPixelFormat<GrayAlphaF32> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.GrayAlphaF32); }
public readonly struct RgbF32 : IRawPixelFormat<RgbF32> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.RgbF32); }
public readonly struct RgbaF32 : IRawPixelFormat<RgbaF32> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.RgbaF32); }

public readonly struct Yuv420P8 : IRawPixelFormat<Yuv420P8> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.Yuv420P8); }
public readonly struct Yuv422P8 : IRawPixelFormat<Yuv422P8> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.Yuv422P8); }
public readonly struct Yuv440P8 : IRawPixelFormat<Yuv440P8> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.Yuv440P8); }
public readonly struct Yuv444P8 : IRawPixelFormat<Yuv444P8> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.Yuv444P8); }
public readonly struct Yuv420P10 : IRawPixelFormat<Yuv420P10> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.Yuv420P10); }
public readonly struct Yuv422P10 : IRawPixelFormat<Yuv422P10> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.Yuv422P10); }
public readonly struct Yuv440P10 : IRawPixelFormat<Yuv440P10> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.Yuv440P10); }
public readonly struct Yuv444P10 : IRawPixelFormat<Yuv444P10> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.Yuv444P10); }
public readonly struct Yuv420P12 : IRawPixelFormat<Yuv420P12> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.Yuv420P12); }
public readonly struct Yuv422P12 : IRawPixelFormat<Yuv422P12> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.Yuv422P12); }
public readonly struct Yuv440P12 : IRawPixelFormat<Yuv440P12> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.Yuv440P12); }
public readonly struct Yuv444P12 : IRawPixelFormat<Yuv444P12> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.Yuv444P12); }
public readonly struct Yuv420P16 : IRawPixelFormat<Yuv420P16> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.Yuv420P16); }
public readonly struct Yuv422P16 : IRawPixelFormat<Yuv422P16> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.Yuv422P16); }
public readonly struct Yuv440P16 : IRawPixelFormat<Yuv440P16> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.Yuv440P16); }
public readonly struct Yuv444P16 : IRawPixelFormat<Yuv444P16> { public static RawPixelFormatTraits Traits => RawPixelFormats.Get(PixelFormat.Yuv444P16); }
