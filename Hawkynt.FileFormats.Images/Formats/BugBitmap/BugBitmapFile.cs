using System;
using FileFormat.Core;

namespace FileFormat.BugBitmap;

/// <summary>In-memory representation of a Commodore 64 Bug Bitmap image.</summary>
public readonly record struct BugBitmapFile : IImageFormatReader<BugBitmapFile>, IImageToRawImage<BugBitmapFile>, IImageFromRawImage<BugBitmapFile>, IImageFormatWriter<BugBitmapFile> {

  static string IImageFormatMetadata<BugBitmapFile>.PrimaryExtension => ".bbm";
  static string[] IImageFormatMetadata<BugBitmapFile>.FileExtensions => [".bbm", ".bug"];
  static BugBitmapFile IImageFormatReader<BugBitmapFile>.FromSpan(ReadOnlySpan<byte> data) => BugBitmapReader.FromSpan(data);
  static byte[] IImageFormatWriter<BugBitmapFile>.ToBytes(BugBitmapFile file) => BugBitmapWriter.ToBytes(file);

  /// <summary>The fixed width of a Bug Bitmap image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of a Bug Bitmap image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size in bytes (2 + 8000 + 1000 + 1000 + 1 + 1 + 14).</summary>
  public const int ExpectedFileSize = 10018;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the video matrix section in bytes.</summary>
  internal const int VideoMatrixSize = 1000;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>Size of the padding section in bytes.</summary>
  internal const int PaddingSize = 14;

  /// <summary>Image width, always 160.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address, typically 0x4000.</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Multicolor bitmap data (8000 bytes, 2 bits per pixel).</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Video matrix / screen RAM (1000 bytes, upper/lower nybble = 2 colors per cell).</summary>
  public byte[] VideoMatrix { get; init; }

  /// <summary>Color RAM (1000 bytes, lower nybble = 3rd color per cell).</summary>
  public byte[] ColorRam { get; init; }

  /// <summary>Border color index (0-15).</summary>
  public byte BorderColor { get; init; }

  /// <summary>Background color index (0-15).</summary>
  public byte BackgroundColor { get; init; }

  /// <summary>Trailing padding bytes (14 bytes).</summary>
  public byte[] Padding { get; init; }

  /// <summary>Converts this Bug Bitmap image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(BugBitmapFile file)
    => Commodore64Graphics.DecodeMulticolor(
      file.BitmapData, file.VideoMatrix, file.ColorRam, file.BackgroundColor, FixedWidth, FixedHeight);

  /// <summary>Creates a Bug Bitmap screen from a <see cref="RawImage"/>, sampling it to the VIC-II's 160x200 multicolour screen.</summary>
  /// <remarks>
  /// The file is a fixed 10018 bytes with nowhere to state a size, so a picture of any other size is
  /// sampled to the screen rather than refused. The hardware allows four colours in each 4x8 cell:
  /// one shared background register plus three cell-local registers, so the background is the
  /// commonest colour across the whole screen and each cell keeps its three commonest others. A
  /// picture already obeying that limit survives the trip untouched; a busier one loses its rarest
  /// colours per cell, which is the constraint of the machine and not of this encoder.
  /// </remarks>
  public static BugBitmapFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var indices = _ToC64Indices(image);
    var background = _MostCommon(indices);
    var bitmapData = new byte[BitmapDataSize];
    var videoMatrix = new byte[VideoMatrixSize];
    var colorRam = new byte[ColorRamSize];

    Span<int> cellColors = stackalloc int[3];
    for (var cellY = 0; cellY < FixedHeight / Commodore64Graphics.CellHeight; ++cellY)
      for (var cellX = 0; cellX < Commodore64Graphics.Columns; ++cellX) {
        var cellIndex = cellY * Commodore64Graphics.Columns + cellX;
        _PickCellColors(indices, cellX, cellY, background, cellColors);

        videoMatrix[cellIndex] = (byte)((cellColors[0] << 4) | cellColors[1]);
        colorRam[cellIndex] = (byte)cellColors[2];

        for (var row = 0; row < Commodore64Graphics.CellHeight; ++row) {
          byte packed = 0;
          for (var column = 0; column < 4; ++column) {
            var color = indices[(cellY * Commodore64Graphics.CellHeight + row) * FixedWidth + cellX * 4 + column];
            var pattern = _PickPattern(color, background, cellColors);
            packed |= (byte)(pattern << ((3 - column) * 2));
          }

          bitmapData[cellIndex * Commodore64Graphics.CellHeight + row] = packed;
        }
      }

    return new() {
      LoadAddress = 0x4000,
      BitmapData = bitmapData,
      VideoMatrix = videoMatrix,
      ColorRam = colorRam,
      BorderColor = background,
      BackgroundColor = background,
      Padding = new byte[PaddingSize],
    };
  }

  /// <summary>Samples to the screen size and reduces every pixel to its nearest VIC-II colour.</summary>
  private static byte[] _ToC64Indices(RawImage image) {
    var bgra = image.SampleTo(FixedWidth, FixedHeight).ToBgra32();
    var indices = new byte[FixedWidth * FixedHeight];
    for (var i = 0; i < indices.Length; ++i) {
      var offset = i * 4;
      indices[i] = (byte)Commodore64Graphics.FindNearestColorIndex(bgra[offset + 2], bgra[offset + 1], bgra[offset]);
    }

    return indices;
  }

  /// <summary>Returns the colour used by the most pixels, which becomes the shared background register.</summary>
  private static byte _MostCommon(byte[] indices) {
    Span<int> frequency = stackalloc int[16];
    foreach (var index in indices)
      ++frequency[index];

    var best = 0;
    for (var i = 1; i < 16; ++i)
      if (frequency[i] > frequency[best])
        best = i;

    return (byte)best;
  }

  /// <summary>Fills <paramref name="cellColors"/> with the three commonest colours in the cell that are not the background.</summary>
  private static void _PickCellColors(byte[] indices, int cellX, int cellY, byte background, Span<int> cellColors) {
    Span<int> frequency = stackalloc int[16];
    for (var row = 0; row < Commodore64Graphics.CellHeight; ++row)
      for (var column = 0; column < 4; ++column)
        ++frequency[indices[(cellY * Commodore64Graphics.CellHeight + row) * FixedWidth + cellX * 4 + column]];

    frequency[background] = -1;
    for (var slot = 0; slot < 3; ++slot) {
      var best = 0;
      for (var i = 1; i < 16; ++i)
        if (frequency[i] > frequency[best])
          best = i;

      cellColors[slot] = frequency[best] > 0 ? best : background;
      if (frequency[best] > 0)
        frequency[best] = -1;
    }
  }

  /// <summary>Picks the two-bit pattern whose register holds the colour, or the nearest one it does hold.</summary>
  private static int _PickPattern(byte color, byte background, ReadOnlySpan<int> cellColors) {
    if (color == background)
      return 0;

    for (var slot = 0; slot < 3; ++slot)
      if (cellColors[slot] == color)
        return slot + 1;

    var bestPattern = 0;
    var bestDistance = _Distance(color, background);
    for (var slot = 0; slot < 3; ++slot) {
      var distance = _Distance(color, (byte)cellColors[slot]);
      if (distance >= bestDistance)
        continue;

      bestDistance = distance;
      bestPattern = slot + 1;
    }

    return bestPattern;
  }

  /// <summary>Squared RGB distance between two VIC-II palette entries.</summary>
  private static int _Distance(byte left, byte right) {
    var a = Commodore64Graphics.HexColors[left];
    var b = Commodore64Graphics.HexColors[right];
    var dr = ((a >> 16) & 0xFF) - ((b >> 16) & 0xFF);
    var dg = ((a >> 8) & 0xFF) - ((b >> 8) & 0xFF);
    var db = (a & 0xFF) - (b & 0xFF);
    return dr * dr + dg * dg + db * db;
  }

}
