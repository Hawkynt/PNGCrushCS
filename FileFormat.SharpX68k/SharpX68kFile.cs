using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.SharpX68k;

/// <summary>Sharp X68000 16-bit color screen data model.</summary>
public sealed class SharpX68kFile : IImageFormatReader<SharpX68kFile>, IImageToRawImage<SharpX68kFile>, IImageFromRawImage<SharpX68kFile>, IImageFormatWriter<SharpX68kFile> {

  public const int HeaderSize = 8;

  public int Width { get; init; } = 512;
  public int Height { get; init; } = 512;
  public byte[] PixelData { get; init; } = [];

  public static string PrimaryExtension => ".x68";
  public static string[] FileExtensions => [".x68", ".x68k"];
  static SharpX68kFile IImageFormatReader<SharpX68kFile>.FromSpan(ReadOnlySpan<byte> data) => SharpX68kReader.FromSpan(data);
  public static SharpX68kFile FromFile(FileInfo file) => SharpX68kReader.FromFile(file);
  public static SharpX68kFile FromBytes(byte[] data) => SharpX68kReader.FromBytes(data);
  public static SharpX68kFile FromStream(Stream stream) => SharpX68kReader.FromStream(stream);
  public static byte[] ToBytes(SharpX68kFile file) => SharpX68kWriter.ToBytes(file);

  public static RawImage ToRawImage(SharpX68kFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var pixels = file.PixelData[..];
    return new RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,

    };
  }

  public static SharpX68kFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    byte[] pixels;
    if (image.Format == PixelFormat.Rgb24) {
      // Reject inputs that wouldn't round-trip cleanly through RGB555.
      var src = image.PixelData;
      for (var i = 0; i < src.Length; ++i) {
        var v = src[i];
        var v5 = (v >> 3) & 0x1F;
        var reconstructed = (byte)((v5 << 3) | (v5 >> 2));
        if (reconstructed != v)
          throw new ArgumentException($"Rgb24 input cannot be losslessly encoded as RGB555; use Indexed1/Indexed8 input with a compatible palette.", nameof(image));
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
      throw new ArgumentException($"Expected Rgb24, Indexed1, or Indexed8, got {image.Format}");
    }

    return new SharpX68kFile {
      Width = image.Width,
      Height = image.Height,
      PixelData = pixels,
    };
  }
}
