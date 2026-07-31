using System;
using FileFormat.Core;

namespace FileFormat.PaintMagic;

/// <summary>In-memory representation of a Commodore 64 Paint Magic picture (.pmg).</summary>
/// <remarks>
/// A multicolour screen with no colour RAM. Pattern 11 shows one colour across the whole picture
/// rather than a colour per cell, so a cell here has two entries of its own and two the screen
/// shares — which is a colour less than most multicolour formats and shows in what it can draw.
/// <para/>
/// The bitmap does not start at the head of the file: a 116-byte preamble comes first, and the two
/// shared registers sit in the gap between the bitmap and the video matrix.
/// </remarks>
public readonly record struct PaintMagicFile
  : IImageFormatReader<PaintMagicFile>, IImageToRawImage<PaintMagicFile>,
    IImageFromRawImage<PaintMagicFile>, IImageFormatWriter<PaintMagicFile> {

  static string IImageFormatMetadata<PaintMagicFile>.PrimaryExtension => ".pmg";
  static string[] IImageFormatMetadata<PaintMagicFile>.FileExtensions => [".pmg"];
  static PaintMagicFile IImageFormatReader<PaintMagicFile>.FromSpan(ReadOnlySpan<byte> data)
    => PaintMagicReader.FromSpan(data);
  static byte[] IImageFormatWriter<PaintMagicFile>.ToBytes(PaintMagicFile file)
    => PaintMagicWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<PaintMagicFile>.VideoModes => [
    new("Multicolour", [(FixedWidth, FixedHeight)], [16])
  ];

  /// <summary>The fixed width of the picture in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of the picture in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The exact file size.</summary>
  public const int ExpectedFileSize = 9332;

  /// <summary>Size of the bitmap in bytes.</summary>
  public const int BitmapDataSize = 8000;

  /// <summary>Size of the video matrix in bytes.</summary>
  public const int VideoMatrixSize = 1000;

  /// <summary>Where the bitmap starts, after the preamble.</summary>
  public const int BitmapOffset = 116;

  /// <summary>Where the shared background register sits.</summary>
  public const int BackgroundOffset = 8116;

  /// <summary>Where the one colour pattern 11 shows everywhere sits.</summary>
  public const int SharedColorOffset = 8119;

  /// <summary>Where the video matrix starts.</summary>
  public const int VideoMatrixOffset = 8308;

  /// <summary>Picture width, always 160.</summary>
  public int Width => FixedWidth;

  /// <summary>Picture height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>Bitmap data, two bits a pixel within 4x8 cells.</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Video matrix: two of each cell's colours, one per nibble.</summary>
  public byte[] VideoMatrix { get; init; }

  /// <summary>The colour shown behind pattern 00, shared by the whole screen.</summary>
  public byte BackgroundColor { get; init; }

  /// <summary>The colour shown for pattern 11, shared by the whole screen.</summary>
  public byte SharedColor { get; init; }

  public static RawImage ToRawImage(PaintMagicFile file) {
    // The decoder wants a colour per cell; this format has one for all of them, so it is handed the
    // same entry throughout rather than given a special path.
    var colorRam = new byte[FixedWidth / 4 * (FixedHeight / Commodore64Graphics.CellHeight)];
    Array.Fill(colorRam, file.SharedColor);

    return Commodore64Graphics.DecodeMulticolor(
      file.BitmapData, file.VideoMatrix, colorRam, file.BackgroundColor, FixedWidth, FixedHeight);
  }

  /// <summary>Builds a screen, with two colours per cell and two the whole picture shares.</summary>
  public static PaintMagicFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(FixedWidth, FixedHeight);
    var (background, shared) = _ChooseSharedPair(rgb.PixelData);

    var bitmap = new byte[BitmapDataSize];
    var matrix = new byte[VideoMatrixSize];
    var colorRam = new byte[VideoMatrixSize];
    Commodore64Graphics.EncodeMulticolor(
      rgb.PixelData, FixedWidth, FixedHeight, bitmap, matrix, colorRam, background, shared);

    return new() {
      BitmapData = bitmap,
      VideoMatrix = matrix,
      BackgroundColor = (byte)background,
      SharedColor = (byte)shared,
    };
  }

  /// <summary>The two colours the whole screen shares: the commonest, and the next after it.</summary>
  /// <remarks>
  /// Both are spent once for the entire picture, so they should go to colours that appear often
  /// everywhere rather than to anything a single cell could have chosen for itself.
  /// </remarks>
  private static (int Background, int Shared) _ChooseSharedPair(ReadOnlySpan<byte> rgb) {
    Span<int> totals = stackalloc int[Commodore64Graphics.ColorCount];
    for (var i = 0; i + 2 < rgb.Length; i += 3)
      ++totals[Commodore64Graphics.FindNearestColorIndex(rgb[i], rgb[i + 1], rgb[i + 2])];

    int first = 0, second = -1;
    for (var i = 1; i < Commodore64Graphics.ColorCount; ++i)
      if (totals[i] > totals[first])
        first = i;

    for (var i = 0; i < Commodore64Graphics.ColorCount; ++i)
      if (i != first && (second < 0 || totals[i] > totals[second]))
        second = i;

    return (first, second);
  }
}
