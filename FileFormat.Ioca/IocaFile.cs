using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Ioca;

/// <summary>In-memory representation of an IBM IOCA (Image Object Content Architecture) image.</summary>
public sealed class IocaFile : IImageFileFormat<IocaFile> {

  /// <summary>Minimum header size for an IOCA container (4-byte dimension header).</summary>
  internal const int MinHeaderSize = 4;

  static string IImageFileFormat<IocaFile>.PrimaryExtension => ".ica";
  static string[] IImageFileFormat<IocaFile>.FileExtensions => [".ica", ".ioca"];
  static FormatCapability IImageFileFormat<IocaFile>.Capabilities => FormatCapability.MonochromeOnly;
  static IocaFile IImageFileFormat<IocaFile>.FromFile(FileInfo file) => IocaReader.FromFile(file);
  static IocaFile IImageFileFormat<IocaFile>.FromBytes(byte[] data) => IocaReader.FromBytes(data);
  static IocaFile IImageFileFormat<IocaFile>.FromStream(Stream stream) => IocaReader.FromStream(stream);
  static RawImage IImageFileFormat<IocaFile>.ToRawImage(IocaFile file) => ToRawImage(file);
  static IocaFile IImageFileFormat<IocaFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<IocaFile>.ToBytes(IocaFile file) => IocaWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Packed 1bpp pixel data (width/8 * height bytes).</summary>
  public byte[] PixelData { get; init; } = [];

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Converts to Indexed1 raw image with B&amp;W palette.</summary>
  public static RawImage ToRawImage(IocaFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var bytesPerRow = (file.Width + 7) / 8;
    var expectedSize = bytesPerRow * file.Height;
    var pixelData = new byte[expectedSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, expectedSize)).CopyTo(pixelData);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed1,
      PixelData = pixelData,
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  /// <summary>Creates an IOCA file from an Indexed1 raw image (monochrome).</summary>
  public static IocaFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected {PixelFormat.Indexed1} but got {image.Format}.", nameof(image));

    var pixelData = image.PixelData[..];

    return new() { Width = image.Width, Height = image.Height, PixelData = pixelData };
  }
}
