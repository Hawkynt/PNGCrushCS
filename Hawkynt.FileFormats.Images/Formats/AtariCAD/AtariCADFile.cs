using System;
using FileFormat.Core;

namespace FileFormat.AtariCAD;

/// <summary>In-memory representation of an Atari CAD Screen (.acd) file.</summary>
public readonly record struct AtariCADFile : IImageFormatReader<AtariCADFile>, IImageToRawImage<AtariCADFile>, IImageFromRawImage<AtariCADFile>, IImageFormatWriter<AtariCADFile> {

  /// <summary>Exact file size: 40 bytes/row x 192 rows.</summary>
  /// <summary>Bytes a drawing occupies: forty to a row, a hundred and sixty rows.</summary>
  /// <remarks>
  /// This was written as 192 rows, which is the height of a full Graphics 8 screen rather than the
  /// height this program draws at. A real drawing is 6400 bytes and was refused for being 1280
  /// short of what we expected.
  /// </remarks>
  public const int ExpectedFileSize = BytesPerRow * PixelHeight;

  /// <summary>Width in pixels.</summary>
  internal const int PixelWidth = 320;

  /// <summary>Height in pixels.</summary>
  internal const int PixelHeight = 160;

  /// <summary>Bytes per row in the raw screen dump.</summary>
  internal const int BytesPerRow = 40;

  static string IImageFormatMetadata<AtariCADFile>.PrimaryExtension => ".drg";
  /// <summary>The extension a drawing actually carries, with the one this used to claim after it.</summary>
  /// <remarks>
  /// Nothing writes .acd. The program's drawings are .drg, which another format here also claims —
  /// they are different files under one name, and the extension resolves by trying each in turn.
  /// Until this said .drg, a real drawing was never even offered to this reader.
  /// </remarks>
  static string[] IImageFormatMetadata<AtariCADFile>.FileExtensions => [".drg", ".acd"];
  static AtariCADFile IImageFormatReader<AtariCADFile>.FromSpan(ReadOnlySpan<byte> data) => AtariCADReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<AtariCADFile>.VideoModes => [
    new("Default", [(PixelWidth, PixelHeight)], [2])
  ];
  static byte[] IImageFormatWriter<AtariCADFile>.ToBytes(AtariCADFile file) => AtariCADWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 192.</summary>
  public int Height => PixelHeight;

  /// <summary>Raw 1bpp MSB-first screen data (7680 bytes).</summary>
  public byte[] PixelData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Converts this Atari CAD Screen to an Indexed1 raw image (320x192, B&amp;W palette).</summary>
  public static RawImage ToRawImage(AtariCADFile file) {

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
    image = image.EnsureFormat(PixelFormat.Indexed1);
    if (image.Width != PixelWidth || image.Height != PixelHeight)
      throw new ArgumentException($"Expected {PixelWidth}x{PixelHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var pixelData = new byte[ExpectedFileSize];
    image.PixelData.AsSpan(0, Math.Min(image.PixelData.Length, ExpectedFileSize)).CopyTo(pixelData);

    return new() { PixelData = pixelData };
  }
}
