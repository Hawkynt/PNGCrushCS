using System;
using FileFormat.Core;

namespace FileFormat.IffRgbn;

/// <summary>In-memory representation of an IFF RGBN (13-bit RGB + genlock) image.</summary>
[FormatMagicBytes([0x46, 0x4F, 0x52, 0x4D])]
public readonly record struct IffRgbnFile : IImageFormatReader<IffRgbnFile>, IImageToRawImage<IffRgbnFile>, IImageFromRawImage<IffRgbnFile>, IImageFormatWriter<IffRgbnFile> {

  static string IImageFormatMetadata<IffRgbnFile>.PrimaryExtension => ".rgbn";
  static string[] IImageFormatMetadata<IffRgbnFile>.FileExtensions => [".rgbn", ".iff"];
  static IffRgbnFile IImageFormatReader<IffRgbnFile>.FromSpan(ReadOnlySpan<byte> data) => IffRgbnReader.FromSpan(data);

  static bool? IImageFormatMetadata<IffRgbnFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 12 && header[0] == 0x46 && header[1] == 0x4F && header[2] == 0x52 && header[3] == 0x4D
      && header[8] == 0x52 && header[9] == 0x47 && header[10] == 0x42 && header[11] == 0x4E;

  static byte[] IImageFormatWriter<IffRgbnFile>.ToBytes(IffRgbnFile file) => IffRgbnWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Raw RGB24 pixel data (3 bytes per pixel: R, G, B).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this IFF RGBN file to a format-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(IffRgbnFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  /// <summary>Creates an <see cref="IffRgbnFile"/> from a format-independent <see cref="RawImage"/>. Accepts Rgb24, Indexed1, or Indexed8.</summary>
  public static IffRgbnFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureAnyFormat(PixelFormat.Rgb24, PixelFormat.Indexed8, PixelFormat.Indexed1);

    byte[] pixels;
    if (image.Format == PixelFormat.Rgb24) {
      // Refuse inputs that cannot be losslessly stored as 4-bit per channel.
      var src = image.PixelData;
      for (var i = 0; i < src.Length; ++i) {
        var v = src[i];
        // 8-bit values that survive 4-bit quantization with high-bit replication are exactly multiples of 17.
        if (v % 17 != 0)
          throw new ArgumentException($"Rgb24 input cannot be losslessly stored as 4-bit per channel; use Indexed1/Indexed8 input with a compatible palette.", nameof(image));
      }
      pixels = src[..];
    } else if (image.Format == PixelFormat.Indexed1 || image.Format == PixelFormat.Indexed8) {
      var palette = image.Palette ?? throw new ArgumentException("Indexed input requires a palette.", nameof(image));
      var paletteCount = image.PaletteCount;
      pixels = new byte[image.Width * image.Height * 3];
      if (image.Format == PixelFormat.Indexed1) {
        var stride = (image.Width + 7) / 8;
        for (var y = 0; y < image.Height; ++y)
          for (var x = 0; x < image.Width; ++x) {
            var b = image.PixelData[y * stride + (x >> 3)];
            var idx = (b >> (7 - (x & 7))) & 1;
            if (idx >= paletteCount) idx = 0;
            var dst = (y * image.Width + x) * 3;
            pixels[dst] = palette[idx * 3];
            pixels[dst + 1] = palette[idx * 3 + 1];
            pixels[dst + 2] = palette[idx * 3 + 2];
          }
      } else {
        for (var i = 0; i < image.Width * image.Height; ++i) {
          var idx = image.PixelData[i];
          if (idx >= paletteCount) idx = 0;
          pixels[i * 3] = palette[idx * 3];
          pixels[i * 3 + 1] = palette[idx * 3 + 1];
          pixels[i * 3 + 2] = palette[idx * 3 + 2];
        }
      }
    } else {
      throw new ArgumentException($"Expected {PixelFormat.Rgb24}, {PixelFormat.Indexed1}, or {PixelFormat.Indexed8} but got {image.Format}.", nameof(image));
    }

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = pixels,
    };
  }
}
