using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.ScitexCt;

/// <summary>In-memory representation of a Scitex CT (Continuous Tone) image.</summary>
public readonly record struct ScitexCtFile : IImageFormatReader<ScitexCtFile>, IImageToRawImage<ScitexCtFile>, IImageFromRawImage<ScitexCtFile>, IImageFormatWriter<ScitexCtFile> {

  static string IImageFormatMetadata<ScitexCtFile>.PrimaryExtension => ".sct";
  static string[] IImageFormatMetadata<ScitexCtFile>.FileExtensions => [".sct", ".ct"];
  static ScitexCtFile IImageFormatReader<ScitexCtFile>.FromSpan(ReadOnlySpan<byte> data) => ScitexCtReader.FromSpan(data);
  static byte[] IImageFormatWriter<ScitexCtFile>.ToBytes(ScitexCtFile file) => ScitexCtWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Bits per component (always 8).</summary>
  public int BitsPerComponent { get; init; }

  /// <summary>Color mode (Grayscale, RGB, or CMYK).</summary>
  public ScitexCtColorMode ColorMode { get; init; }

  /// <summary>Horizontal resolution.</summary>
  public int HResolution { get; init; }

  /// <summary>Vertical resolution.</summary>
  public int VResolution { get; init; }

  /// <summary>Image description (up to 36 ASCII characters).</summary>
  public string Description { get; init; }

  /// <summary>Raw pixel data: Width * Height * channels bytes.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this Scitex CT image to a platform-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(ScitexCtFile file) {

    var width = file.Width;
    var height = file.Height;

    switch (file.ColorMode) {
      case ScitexCtColorMode.Grayscale: {
        var gray = new byte[width * height];
        file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, gray.Length)).CopyTo(gray);
        return new() { Width = width, Height = height, Format = PixelFormat.Gray8, PixelData = gray };
      }
      case ScitexCtColorMode.Rgb: {
        var rgb = new byte[width * height * 3];
        file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, rgb.Length)).CopyTo(rgb);
        return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
      }
      case ScitexCtColorMode.Cmyk: {
        var rgb = new byte[width * height * 3];
        var pixelCount = width * height;
        for (var i = 0; i < pixelCount; ++i) {
          var srcIdx = i * 4;
          if (srcIdx + 3 >= file.PixelData.Length)
            break;
          var c = file.PixelData[srcIdx];
          var m = file.PixelData[srcIdx + 1];
          var y = file.PixelData[srcIdx + 2];
          var k = file.PixelData[srcIdx + 3];
          var dstIdx = i * 3;
          rgb[dstIdx] = (byte)Math.Max(0, 255 - c - k);
          rgb[dstIdx + 1] = (byte)Math.Max(0, 255 - m - k);
          rgb[dstIdx + 2] = (byte)Math.Max(0, 255 - y - k);
        }
        return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
      }
      default:
        throw new InvalidDataException($"Unknown Scitex CT color mode: {file.ColorMode}.");
    }
  }

  /// <summary>Creates a Scitex CT file from a platform-independent <see cref="RawImage"/>.</summary>
  public static ScitexCtFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return image.Format switch {
      PixelFormat.Gray8 => new() {
        Width = image.Width,
        Height = image.Height,
        BitsPerComponent = 8,
        ColorMode = ScitexCtColorMode.Grayscale,
        HResolution = 300,
        VResolution = 300,
        PixelData = image.PixelData[..],
      },
      PixelFormat.Rgb24 => new() {
        Width = image.Width,
        Height = image.Height,
        BitsPerComponent = 8,
        ColorMode = ScitexCtColorMode.Rgb,
        HResolution = 300,
        VResolution = 300,
        PixelData = image.PixelData[..],
      },
      _ => throw new ArgumentException($"Unsupported pixel format for Scitex CT: {image.Format}. Expected Gray8 or Rgb24.", nameof(image)),
    };
  }
}
