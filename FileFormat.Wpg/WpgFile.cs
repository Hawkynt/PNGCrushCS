using System;
using FileFormat.Core;

namespace FileFormat.Wpg;

/// <summary>In-memory representation of a WPG (WordPerfect Graphics) raster image.</summary>
[FormatMagicBytes([0xFF, 0x57, 0x50, 0x43])]
public readonly record struct WpgFile : IImageFormatReader<WpgFile>, IImageToRawImage<WpgFile>, IImageFromRawImage<WpgFile>, IImageFormatWriter<WpgFile> {

  static string IImageFormatMetadata<WpgFile>.PrimaryExtension => ".wpg";
  static string[] IImageFormatMetadata<WpgFile>.FileExtensions => [".wpg"];
  static WpgFile IImageFormatReader<WpgFile>.FromSpan(ReadOnlySpan<byte> data) => WpgReader.FromSpan(data);
  static byte[] IImageFormatWriter<WpgFile>.ToBytes(WpgFile file) => WpgWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int BitsPerPixel { get; init; }

  /// <summary>Raw pixel data (indexed or monochrome).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>RGB palette (3 bytes per entry), or null if no palette.</summary>
  public byte[]? Palette { get; init; }

  /// <summary>Converts a WPG file to a <see cref="RawImage"/>. Unpacks 1/4bpp to Indexed8.</summary>
  public static RawImage ToRawImage(WpgFile file) {

    byte[] pixels;
    int paletteCount;

    switch (file.BitsPerPixel) {
      case 1: {
        paletteCount = 2;
        var stride = (file.Width + 7) / 8;
        pixels = new byte[file.Width * file.Height];
        for (var y = 0; y < file.Height; ++y)
          for (var x = 0; x < file.Width; ++x) {
            var byteIndex = y * stride + (x >> 3);
            var bitIndex = 7 - (x & 7);
            pixels[y * file.Width + x] = (byte)((file.PixelData[byteIndex] >> bitIndex) & 1);
          }

        break;
      }
      case 4: {
        paletteCount = 16;
        var stride = (file.Width + 1) / 2;
        pixels = new byte[file.Width * file.Height];
        for (var y = 0; y < file.Height; ++y)
          for (var x = 0; x < file.Width; ++x) {
            var byteIndex = y * stride + (x >> 1);
            pixels[y * file.Width + x] = (x & 1) == 0
              ? (byte)((file.PixelData[byteIndex] >> 4) & 0x0F)
              : (byte)(file.PixelData[byteIndex] & 0x0F);
          }

        break;
      }
      case 8:
        paletteCount = 256;
        pixels = file.PixelData[..];
        break;
      default:
        throw new ArgumentException($"Unsupported WPG bits per pixel: {file.BitsPerPixel}.", nameof(file));
    }

    return new RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = file.Palette != null ? file.Palette[..] : null,
      PaletteCount = paletteCount
    };
  }

  /// <summary>Creates a WPG file from a <see cref="RawImage"/>. Must be Indexed8.</summary>
  public static WpgFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"WPG requires Indexed8 pixel format, got {image.Format}.", nameof(image));

    int bpp;
    byte[] pixels;

    if (image.PaletteCount <= 2) {
      bpp = 1;
      var stride = (image.Width + 7) / 8;
      pixels = new byte[stride * image.Height];
      for (var y = 0; y < image.Height; ++y)
        for (var x = 0; x < image.Width; ++x)
          if ((image.PixelData[y * image.Width + x] & 1) != 0)
            pixels[y * stride + (x >> 3)] |= (byte)(0x80 >> (x & 7));
    } else if (image.PaletteCount <= 16) {
      bpp = 4;
      var stride = (image.Width + 1) / 2;
      pixels = new byte[stride * image.Height];
      for (var y = 0; y < image.Height; ++y)
        for (var x = 0; x < image.Width; ++x) {
          var value = image.PixelData[y * image.Width + x];
          var byteIndex = y * stride + (x >> 1);
          if ((x & 1) == 0)
            pixels[byteIndex] |= (byte)((value & 0x0F) << 4);
          else
            pixels[byteIndex] |= (byte)(value & 0x0F);
        }
    } else {
      bpp = 8;
      pixels = image.PixelData[..];
    }

    return new WpgFile {
      Width = image.Width,
      Height = image.Height,
      BitsPerPixel = bpp,
      PixelData = pixels,
      Palette = image.Palette != null ? image.Palette[..] : null
    };
  }
}
