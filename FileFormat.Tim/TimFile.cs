using System;
using FileFormat.Core;

namespace FileFormat.Tim;

/// <summary>In-memory representation of a PlayStation 1 TIM texture.</summary>
[FormatMagicBytes([0x10, 0x00, 0x00, 0x00])]
public readonly record struct TimFile : IImageFormatReader<TimFile>, IImageToRawImage<TimFile>, IImageFormatWriter<TimFile> {

  static string IImageFormatMetadata<TimFile>.PrimaryExtension => ".tim";
  static string[] IImageFormatMetadata<TimFile>.FileExtensions => [".tim"];
  static TimFile IImageFormatReader<TimFile>.FromSpan(ReadOnlySpan<byte> data) => TimReader.FromSpan(data);
  static byte[] IImageFormatWriter<TimFile>.ToBytes(TimFile file) => TimWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public TimBpp Bpp { get; init; }
  public bool HasClut { get; init; }

  /// <summary>CLUT (palette) data as A1B5G5R5 16-bit entries, null if no CLUT.</summary>
  public byte[]? ClutData { get; init; }
  public int ClutX { get; init; }
  public int ClutY { get; init; }
  public int ClutWidth { get; init; }
  public int ClutHeight { get; init; }

  public int ImageX { get; init; }
  public int ImageY { get; init; }

  /// <summary>Raw pixel data as stored in the TIM file.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts a TIM file to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(TimFile file) {

    return file.Bpp switch {
      TimBpp.Bpp4 => _Decode4Bpp(file),
      TimBpp.Bpp8 => _Decode8Bpp(file),
      TimBpp.Bpp16 => _Decode16Bpp(file),
      TimBpp.Bpp24 => _Decode24Bpp(file),
      _ => throw new NotSupportedException($"Unsupported TIM BPP: {file.Bpp}")
    };
  }

  private static byte[] _ConvertClutToRgbPalette(byte[] clutData, int entryCount) {
    var palette = new byte[entryCount * 3];
    for (var i = 0; i < entryCount && i * 2 + 1 < clutData.Length; ++i) {
      var lo = clutData[i * 2];
      var hi = clutData[i * 2 + 1];
      var val16 = lo | (hi << 8);
      var r5 = val16 & 0x1F;
      var g5 = (val16 >> 5) & 0x1F;
      var b5 = (val16 >> 10) & 0x1F;
      palette[i * 3] = (byte)((r5 << 3) | (r5 >> 2));
      palette[i * 3 + 1] = (byte)((g5 << 3) | (g5 >> 2));
      palette[i * 3 + 2] = (byte)((b5 << 3) | (b5 >> 2));
    }

    return palette;
  }

  private static RawImage _Decode4Bpp(TimFile file) {
    var width = file.Width;
    var height = file.Height;
    var pixels = new byte[width * height];
    var srcIndex = 0;
    for (var i = 0; i < width * height; i += 2) {
      if (srcIndex >= file.PixelData.Length)
        break;
      var b = file.PixelData[srcIndex++];
      pixels[i] = (byte)(b & 0x0F);
      if (i + 1 < pixels.Length)
        pixels[i + 1] = (byte)((b >> 4) & 0x0F);
    }

    byte[]? palette = null;
    var paletteCount = 0;
    if (file.ClutData != null) {
      paletteCount = 16;
      palette = _ConvertClutToRgbPalette(file.ClutData, paletteCount);
    }

    return new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = paletteCount,
    };
  }

  private static RawImage _Decode8Bpp(TimFile file) {
    var width = file.Width;
    var height = file.Height;
    var pixels = new byte[width * height];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, pixels.Length)).CopyTo(pixels);

    byte[]? palette = null;
    var paletteCount = 0;
    if (file.ClutData != null) {
      paletteCount = 256;
      palette = _ConvertClutToRgbPalette(file.ClutData, paletteCount);
    }

    return new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = paletteCount,
    };
  }

  private static RawImage _Decode16Bpp(TimFile file) {
    var width = file.Width;
    var height = file.Height;
    var rgb = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      var srcOff = i * 2;
      if (srcOff + 1 >= file.PixelData.Length)
        break;
      var lo = file.PixelData[srcOff];
      var hi = file.PixelData[srcOff + 1];
      var val16 = lo | (hi << 8);
      var r5 = val16 & 0x1F;
      var g5 = (val16 >> 5) & 0x1F;
      var b5 = (val16 >> 10) & 0x1F;
      rgb[i * 3] = (byte)((r5 << 3) | (r5 >> 2));
      rgb[i * 3 + 1] = (byte)((g5 << 3) | (g5 >> 2));
      rgb[i * 3 + 2] = (byte)((b5 << 3) | (b5 >> 2));
    }

    return new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  private static RawImage _Decode24Bpp(TimFile file) {
    var width = file.Width;
    var height = file.Height;
    var rgb = new byte[width * height * 3];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, rgb.Length)).CopyTo(rgb);

    return new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }
}
