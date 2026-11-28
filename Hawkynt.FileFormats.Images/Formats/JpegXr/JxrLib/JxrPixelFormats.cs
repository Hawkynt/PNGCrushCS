using System;
using System.Collections.Generic;

namespace FileFormat.JpegXr.JxrLib;

/// <summary>Container pixel format: bits per pixel, whether it carries alpha and whether samples are stored BGR.</summary>
internal sealed record JxrPixelFormatInfo(Guid Guid, string Name, int BitsPerUnit, bool HasAlpha, bool Bgr);

/// <summary>Pixel format GUIDs of the JPEG XR container (T.832 annex A / jxrlib JXRGlue.c pixelInfo).</summary>
internal static class JxrPixelFormats {

  private static Guid _Wmp(byte last) => new(0x6fddc324, 0x4e03, 0x4bfe, 0xb1, 0x85, 0x3d, 0x77, 0x76, 0x8d, 0xc9, last);

  private static readonly JxrPixelFormatInfo[] _All = [
    new(_Wmp(0x05), "BlackWhite", 1, false, false),
    new(_Wmp(0x08), "8bppGray", 8, false, false),
    new(_Wmp(0x0b), "16bppGray", 16, false, false),
    new(_Wmp(0x13), "16bppGrayFixedPoint", 16, false, false),
    new(_Wmp(0x3e), "16bppGrayHalf", 16, false, false),
    new(_Wmp(0x3f), "32bppGrayFixedPoint", 32, false, false),
    new(_Wmp(0x11), "32bppGrayFloat", 32, false, false),

    new(_Wmp(0x0d), "24bppRGB", 24, false, false),
    new(_Wmp(0x0c), "24bppBGR", 24, false, true),
    new(new Guid(0xd98c6b95, 0x3efe, 0x47d6, 0xbb, 0x25, 0xeb, 0x17, 0x48, 0xab, 0x0c, 0xf1), "32bppRGB", 32, false, false),
    new(_Wmp(0x0e), "32bppBGR", 32, false, true),
    new(_Wmp(0x15), "48bppRGB", 48, false, false),
    new(_Wmp(0x12), "48bppRGBFixedPoint", 48, false, false),
    new(_Wmp(0x3b), "48bppRGBHalf", 48, false, false),
    new(_Wmp(0x40), "64bppRGBFixedPoint", 64, false, false),
    new(_Wmp(0x42), "64bppRGBHalf", 64, false, false),
    new(_Wmp(0x18), "96bppRGBFixedPoint", 96, false, false),
    new(new Guid(0xe3fed78f, 0xe8db, 0x4acf, 0x84, 0xc1, 0xe9, 0x7f, 0x61, 0x36, 0xb3, 0x27), "96bppRGBFloat", 96, false, false),
    new(_Wmp(0x41), "128bppRGBFixedPoint", 128, false, false),
    new(_Wmp(0x1b), "128bppRGBFloat", 128, false, false),

    new(_Wmp(0x0f), "32bppBGRA", 32, true, true),
    new(new Guid(0xf5c7ad2d, 0x6a8d, 0x43dd, 0xa7, 0xa8, 0xa2, 0x99, 0x35, 0x26, 0x1a, 0xe9), "32bppRGBA", 32, true, false),
    new(_Wmp(0x16), "64bppRGBA", 64, true, false),
    new(_Wmp(0x1d), "64bppRGBAFixedPoint", 64, true, false),
    new(_Wmp(0x3a), "64bppRGBAHalf", 64, true, false),
    new(_Wmp(0x1e), "128bppRGBAFixedPoint", 128, true, false),
    new(_Wmp(0x19), "128bppRGBAFloat", 128, true, false),

    new(_Wmp(0x10), "32bppPBGRA", 32, true, true),
    new(new Guid(0x3cc4a650, 0xa527, 0x4d37, 0xa9, 0x16, 0x31, 0x42, 0xc7, 0xeb, 0xed, 0xba), "32bppPRGBA", 32, true, false),
    new(_Wmp(0x17), "64bppPRGBA", 64, true, false),
    new(_Wmp(0x1a), "128bppPRGBAFloat", 128, true, false),

    new(_Wmp(0x09), "16bppRGB555", 16, false, false),
    new(_Wmp(0x0a), "16bppRGB565", 16, false, false),
    new(_Wmp(0x14), "32bppRGB101010", 32, false, false),

    new(_Wmp(0x1c), "32bppCMYK", 32, false, false),
    new(_Wmp(0x2c), "40bppCMYKAlpha", 40, true, false),
    new(_Wmp(0x1f), "64bppCMYK", 64, false, false),
    new(_Wmp(0x2d), "80bppCMYKAlpha", 80, true, false),

    new(_Wmp(0x20), "24bpp3Channels", 24, false, false),
    new(_Wmp(0x21), "32bpp4Channels", 32, false, false),
    new(_Wmp(0x22), "40bpp5Channels", 40, false, false),
    new(_Wmp(0x23), "48bpp6Channels", 48, false, false),
    new(_Wmp(0x24), "56bpp7Channels", 56, false, false),
    new(_Wmp(0x25), "64bpp8Channels", 64, false, false),
    new(_Wmp(0x2e), "32bpp3ChannelsAlpha", 32, true, false),
    new(_Wmp(0x2f), "40bpp4ChannelsAlpha", 40, true, false),
    new(_Wmp(0x30), "48bpp5ChannelsAlpha", 48, true, false),
    new(_Wmp(0x31), "56bpp6ChannelsAlpha", 56, true, false),
    new(_Wmp(0x32), "64bpp7ChannelsAlpha", 64, true, false),
    new(_Wmp(0x33), "72bpp8ChannelsAlpha", 72, true, false),
    new(_Wmp(0x26), "48bpp3Channels", 48, false, false),
    new(_Wmp(0x27), "64bpp4Channels", 64, false, false),
    new(_Wmp(0x28), "80bpp5Channels", 80, false, false),
    new(_Wmp(0x29), "96bpp6Channels", 96, false, false),
    new(_Wmp(0x2a), "112bpp7Channels", 112, false, false),
    new(_Wmp(0x2b), "128bpp8Channels", 128, false, false),
    new(_Wmp(0x34), "64bpp3ChannelsAlpha", 64, true, false),
    new(_Wmp(0x35), "80bpp4ChannelsAlpha", 80, true, false),
    new(_Wmp(0x36), "96bpp5ChannelsAlpha", 96, true, false),
    new(_Wmp(0x37), "112bpp6ChannelsAlpha", 112, true, false),
    new(_Wmp(0x38), "128bpp7ChannelsAlpha", 128, true, false),
    new(_Wmp(0x39), "144bpp8ChannelsAlpha", 144, true, false),

    new(_Wmp(0x3d), "32bppRGBE", 32, false, false),
  ];

  private static readonly Dictionary<Guid, JxrPixelFormatInfo> _ByGuid = _BuildIndex();

  private static Dictionary<Guid, JxrPixelFormatInfo> _BuildIndex() {
    var d = new Dictionary<Guid, JxrPixelFormatInfo>();
    foreach (var f in _All)
      d[f.Guid] = f;
    return d;
  }

  internal static JxrPixelFormatInfo? Find(Guid guid) => _ByGuid.GetValueOrDefault(guid);
}
