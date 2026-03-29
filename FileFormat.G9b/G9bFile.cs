using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.G9b;

/// <summary>In-memory representation of a V9990 GFX9000 (.g9b) image.
/// Header: 3-byte magic "G9B" + 2-byte LE header size + 1-byte screen mode + 1-byte color mode + 2-byte LE width + 2-byte LE height + pixel data.
/// </summary>
[FormatMagicBytes([0x47, 0x39, 0x42])]
public sealed class G9bFile : IImageFileFormat<G9bFile> {

  static string IImageFileFormat<G9bFile>.PrimaryExtension => ".g9b";
  static string[] IImageFileFormat<G9bFile>.FileExtensions => [".g9b"];
  static FormatCapability IImageFileFormat<G9bFile>.Capabilities => FormatCapability.VariableResolution;
  static G9bFile IImageFileFormat<G9bFile>.FromFile(FileInfo file) => G9bReader.FromFile(file);
  static G9bFile IImageFileFormat<G9bFile>.FromBytes(byte[] data) => G9bReader.FromBytes(data);
  static G9bFile IImageFileFormat<G9bFile>.FromStream(Stream stream) => G9bReader.FromStream(stream);
  static byte[] IImageFileFormat<G9bFile>.ToBytes(G9bFile file) => G9bWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Screen mode (determines bits per pixel).</summary>
  public G9bScreenMode ScreenMode { get; init; }

  /// <summary>Color mode byte (stored in header, typically 0).</summary>
  public byte ColorMode { get; init; }

  /// <summary>Header size as stored in the file (default 11).</summary>
  public int HeaderSize { get; init; } = G9bReader.DefaultHeaderSize;

  /// <summary>Raw pixel data.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts this G9B image to a platform-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(G9bFile file) {
    ArgumentNullException.ThrowIfNull(file);

    return file.ScreenMode switch {
      G9bScreenMode.Indexed8 => _Mode3ToRawImage(file),
      G9bScreenMode.Rgb555 => _Mode5ToRawImage(file),
      _ => throw new NotSupportedException($"Unsupported G9B screen mode: {file.ScreenMode}.")
    };
  }

  /// <summary>Creates a G9B image from a platform-independent <see cref="RawImage"/>.</summary>
  public static G9bFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return image.Format switch {
      PixelFormat.Gray8 => _FromGray8(image),
      PixelFormat.Rgb24 => _FromRgb24(image),
      _ => throw new ArgumentException($"RawImage must use Gray8 or Rgb24 format, got {image.Format}.", nameof(image))
    };
  }

  private static RawImage _Mode3ToRawImage(G9bFile file) {
    var palette = new byte[256 * 3];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)i;
      palette[i * 3 + 1] = (byte)i;
      palette[i * 3 + 2] = (byte)i;
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Gray8,
      PixelData = file.PixelData[..],
    };
  }

  private static RawImage _Mode5ToRawImage(G9bFile file) {
    var pixelCount = file.Width * file.Height;
    var rgb = new byte[pixelCount * 3];

    for (var i = 0; i < pixelCount; ++i) {
      var lo = file.PixelData[i * 2];
      var hi = file.PixelData[i * 2 + 1];
      var word = lo | (hi << 8);

      // GGGRRRRR 0BBBBBGG - RGB555
      var r = (word & 0x1F) << 3;
      var g = (((word >> 5) & 0x07) | (((word >> 8) & 0x07) << 3)) << 2;
      var b = ((word >> 10) & 0x1F) << 3;

      rgb[i * 3] = (byte)Math.Min(r | (r >> 5), 255);
      rgb[i * 3 + 1] = (byte)Math.Min(g | (g >> 6), 255);
      rgb[i * 3 + 2] = (byte)Math.Min(b | (b >> 5), 255);
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  private static G9bFile _FromGray8(RawImage image) => new() {
    Width = image.Width,
    Height = image.Height,
    ScreenMode = G9bScreenMode.Indexed8,
    ColorMode = 0,
    PixelData = image.PixelData[..],
  };

  private static G9bFile _FromRgb24(RawImage image) {
    var pixelCount = image.Width * image.Height;
    var pixelData = new byte[pixelCount * 2];

    for (var i = 0; i < pixelCount; ++i) {
      var r = image.PixelData[i * 3];
      var g = image.PixelData[i * 3 + 1];
      var b = image.PixelData[i * 3 + 2];

      var r5 = r >> 3;
      var g6 = g >> 2;
      var b5 = b >> 3;

      // GGGRRRRR 0BBBBBGG
      var gLow = g6 & 0x07;
      var gHigh = (g6 >> 3) & 0x07;
      var word = (ushort)(r5 | (gLow << 5) | (gHigh << 8) | (b5 << 10));

      pixelData[i * 2] = (byte)(word & 0xFF);
      pixelData[i * 2 + 1] = (byte)(word >> 8);
    }

    return new() {
      Width = image.Width,
      Height = image.Height,
      ScreenMode = G9bScreenMode.Rgb555,
      ColorMode = 0,
      PixelData = pixelData,
    };
  }
}
