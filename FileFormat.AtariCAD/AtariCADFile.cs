using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AtariCAD;

/// <summary>In-memory representation of an Atari CAD Screen (.acd) file.</summary>
public sealed class AtariCADFile : IImageFileFormat<AtariCADFile> {

  /// <summary>Exact file size: 40 bytes/row x 192 rows.</summary>
  public const int ExpectedFileSize = 7680;

  /// <summary>Width in pixels.</summary>
  internal const int PixelWidth = 320;

  /// <summary>Height in pixels.</summary>
  internal const int PixelHeight = 192;

  /// <summary>Bytes per row in the raw screen dump.</summary>
  internal const int BytesPerRow = 40;

  static string IImageFileFormat<AtariCADFile>.PrimaryExtension => ".acd";
  static string[] IImageFileFormat<AtariCADFile>.FileExtensions => [".acd"];
  static FormatCapability IImageFileFormat<AtariCADFile>.Capabilities => FormatCapability.MonochromeOnly;
  static AtariCADFile IImageFileFormat<AtariCADFile>.FromFile(FileInfo file) => AtariCADReader.FromFile(file);
  static AtariCADFile IImageFileFormat<AtariCADFile>.FromBytes(byte[] data) => AtariCADReader.FromBytes(data);
  static AtariCADFile IImageFileFormat<AtariCADFile>.FromStream(Stream stream) => AtariCADReader.FromStream(stream);
  static byte[] IImageFileFormat<AtariCADFile>.ToBytes(AtariCADFile file) => AtariCADWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 192.</summary>
  public int Height => PixelHeight;

  /// <summary>Raw 1bpp MSB-first screen data (7680 bytes).</summary>
  public byte[] PixelData { get; init; } = [];

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Converts this Atari CAD Screen to an Indexed1 raw image (320x192, B&amp;W palette).</summary>
  public static RawImage ToRawImage(AtariCADFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelData = new byte[ExpectedFileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, ExpectedFileSize)).CopyTo(pixelData);

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Indexed1,
      PixelData = pixelData,
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  /// <summary>Creates an Atari CAD Screen from an Indexed1 raw image (320x192).</summary>
  public static AtariCADFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected {PixelFormat.Indexed1} but got {image.Format}.", nameof(image));
    if (image.Width != PixelWidth || image.Height != PixelHeight)
      throw new ArgumentException($"Expected {PixelWidth}x{PixelHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var pixelData = new byte[ExpectedFileSize];
    image.PixelData.AsSpan(0, Math.Min(image.PixelData.Length, ExpectedFileSize)).CopyTo(pixelData);

    return new() { PixelData = pixelData };
  }
}
