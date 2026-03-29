using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Tim2;

/// <summary>In-memory representation of a PlayStation 2/PSP TIM2 texture file.</summary>
public sealed class Tim2File : IImageFileFormat<Tim2File> {

  static string IImageFileFormat<Tim2File>.PrimaryExtension => ".tm2";
  static string[] IImageFileFormat<Tim2File>.FileExtensions => [".tm2"];
  static Tim2File IImageFileFormat<Tim2File>.FromFile(FileInfo file) => Tim2Reader.FromFile(file);
  static Tim2File IImageFileFormat<Tim2File>.FromBytes(byte[] data) => Tim2Reader.FromBytes(data);
  static Tim2File IImageFileFormat<Tim2File>.FromStream(Stream stream) => Tim2Reader.FromStream(stream);
  static byte[] IImageFileFormat<Tim2File>.ToBytes(Tim2File file) => Tim2Writer.ToBytes(file);
  public byte Version { get; init; }
  public byte Alignment { get; init; }

  /// <summary>All pictures contained in this TIM2 file.</summary>
  public IReadOnlyList<Tim2Picture> Pictures { get; init; } = [];

  /// <summary>Converts the first picture of a TIM2 file to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(Tim2File file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Pictures.Count == 0)
      throw new ArgumentException("TIM2 file contains no pictures.", nameof(file));

    var pic = file.Pictures[0];
    return pic.Format switch {
      Tim2Format.Indexed4 => _DecodeIndexed4(pic),
      Tim2Format.Indexed8 => _DecodeIndexed8(pic),
      Tim2Format.Rgb16 => _DecodeRgb16(pic),
      Tim2Format.Rgb24 => _DecodeRgb24(pic),
      Tim2Format.Rgb32 => _DecodeRgb32(pic),
      _ => throw new NotSupportedException($"Unsupported TIM2 format: {pic.Format}")
    };
  }

  /// <summary>TIM2 encoding is not supported from a single raw image.</summary>
  public static Tim2File FromRawImage(RawImage image) => throw new NotSupportedException("TIM2 encoding from a single raw image is not supported.");

  private static byte[] _ConvertPaletteToRgb(byte[] paletteData, int colorCount) {
    var palette = new byte[colorCount * 3];
    for (var i = 0; i < colorCount && i * 4 + 2 < paletteData.Length; ++i) {
      palette[i * 3] = paletteData[i * 4];         // R
      palette[i * 3 + 1] = paletteData[i * 4 + 1]; // G
      palette[i * 3 + 2] = paletteData[i * 4 + 2]; // B
    }

    return palette;
  }

  private static RawImage _DecodeIndexed4(Tim2Picture pic) {
    var width = pic.Width;
    var height = pic.Height;
    var pixels = new byte[width * height];
    var srcIndex = 0;
    for (var i = 0; i < width * height; i += 2) {
      if (srcIndex >= pic.PixelData.Length)
        break;
      var b = pic.PixelData[srcIndex++];
      pixels[i] = (byte)(b & 0x0F);
      if (i + 1 < pixels.Length)
        pixels[i + 1] = (byte)((b >> 4) & 0x0F);
    }

    byte[]? palette = null;
    var paletteCount = 0;
    if (pic.PaletteData != null) {
      paletteCount = pic.PaletteColors;
      palette = _ConvertPaletteToRgb(pic.PaletteData, paletteCount);
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

  private static RawImage _DecodeIndexed8(Tim2Picture pic) {
    var width = pic.Width;
    var height = pic.Height;
    var pixels = new byte[width * height];
    pic.PixelData.AsSpan(0, Math.Min(pic.PixelData.Length, pixels.Length)).CopyTo(pixels);

    byte[]? palette = null;
    var paletteCount = 0;
    if (pic.PaletteData != null) {
      paletteCount = pic.PaletteColors;
      palette = _ConvertPaletteToRgb(pic.PaletteData, paletteCount);
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

  private static RawImage _DecodeRgb16(Tim2Picture pic) {
    var width = pic.Width;
    var height = pic.Height;
    var rgb = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      var srcOff = i * 2;
      if (srcOff + 1 >= pic.PixelData.Length)
        break;
      var lo = pic.PixelData[srcOff];
      var hi = pic.PixelData[srcOff + 1];
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

  private static RawImage _DecodeRgb24(Tim2Picture pic) {
    var width = pic.Width;
    var height = pic.Height;
    var rgb = new byte[width * height * 3];
    pic.PixelData.AsSpan(0, Math.Min(pic.PixelData.Length, rgb.Length)).CopyTo(rgb);

    return new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  private static RawImage _DecodeRgb32(Tim2Picture pic) {
    var width = pic.Width;
    var height = pic.Height;
    var rgba = new byte[width * height * 4];
    pic.PixelData.AsSpan(0, Math.Min(pic.PixelData.Length, rgba.Length)).CopyTo(rgba);

    return new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgba32,
      PixelData = rgba,
    };
  }
}
