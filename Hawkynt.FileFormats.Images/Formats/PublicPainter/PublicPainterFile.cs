using System;
using FileFormat.Core;

namespace FileFormat.PublicPainter;

/// <summary>In-memory representation of a Public Painter compressed monochrome image (Atari ST, 640x400).</summary>
public readonly record struct PublicPainterFile : IImageFormatReader<PublicPainterFile>, IImageToRawImage<PublicPainterFile>, IImageFromRawImage<PublicPainterFile>, IImageFormatWriter<PublicPainterFile> {

  /// <summary>Decompressed bitmap size: 640x400 / 8 bits per byte = 32000 bytes.</summary>
  public const int DecompressedSize = 32000;

  /// <summary>Offset of the escape byte that opens the file.</summary>
  public const int EscapeOffset = 0;

  /// <summary>Offset of the height selector: 0 means 400 lines, 200 means 800.</summary>
  public const int HeightSelectorOffset = 1;

  /// <summary>Height selector value for the standard 640x400 screen.</summary>
  public const byte SingleHeightSelector = 0;

  /// <summary>Offset of the compressed stream.</summary>
  public const int StreamOffset = 2;

  /// <summary>Fixed image width.</summary>
  public const int ImageWidth = 640;

  /// <summary>Fixed image height.</summary>
  public const int ImageHeight = 400;

  static string IImageFormatMetadata<PublicPainterFile>.PrimaryExtension => ".cmp";
  static string[] IImageFormatMetadata<PublicPainterFile>.FileExtensions => [".cmp"];
  static PublicPainterFile IImageFormatReader<PublicPainterFile>.FromSpan(ReadOnlySpan<byte> data) => PublicPainterReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<PublicPainterFile>.VideoModes => [new("Default", [(ImageWidth, ImageHeight)], [2])];
  static byte[] IImageFormatWriter<PublicPainterFile>.ToBytes(PublicPainterFile file) => PublicPainterWriter.ToBytes(file);

  /// <summary>Image width (always 640).</summary>
  public int Width { get; init; }

  /// <summary>Image height (always 400).</summary>
  public int Height { get; init; }

  /// <summary>32000 bytes of 1bpp monochrome bitmap data (MSB first, 80 bytes per row).</summary>
  public byte[] PixelData { get; init; }

  // A set bit is ink, and the paper it sits on is white — so index 0 is the paper, not the ink.
  private static readonly byte[] _BlackWhitePalette = [255, 255, 255, 0, 0, 0];

  public static RawImage ToRawImage(PublicPainterFile file) {

    return new() {
      Width = ImageWidth,
      Height = ImageHeight,
      Format = PixelFormat.Indexed1,
      PixelData = file.PixelData[..],
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  public static PublicPainterFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Indexed1);
    if (image.Width != ImageWidth)
      throw new ArgumentException($"Public Painter images must be exactly {ImageWidth} pixels wide.", nameof(image));
    if (image.Height != ImageHeight)
      throw new ArgumentException($"Public Painter images must be exactly {ImageHeight} pixels tall.", nameof(image));

    return new() {
      Width = ImageWidth,
      Height = ImageHeight,
      PixelData = image.PixelData[..],
    };
  }
}
