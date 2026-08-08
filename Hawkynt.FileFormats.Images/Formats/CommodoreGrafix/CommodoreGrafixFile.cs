using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.CommodoreGrafix;

/// <summary>In-memory representation of a Commodore Grafix file (.cgx).</summary>
/// <remarks>
/// A sheet of C64 multicolour frames in a RIFF container — the same chunked wrapper Windows uses
/// for its own formats, borrowed by a C64 tool because it makes a file that can carry metadata
/// without a decoder having to know what the metadata is.
/// <para/>
/// Each frame is a small multicolour screen with its own background colour appended, and the frames
/// are laid out as a grid whose shape the header states. The whole point is that a game's animation
/// lives in one file rather than one file per frame.
/// </remarks>
public readonly record struct CommodoreGrafixFile
  : IImageFormatReader<CommodoreGrafixFile>, IImageToRawImage<CommodoreGrafixFile>,
    IImageFromRawImage<CommodoreGrafixFile>, IImageFormatWriter<CommodoreGrafixFile> {

  /// <summary>Bytes a frame spends on each of its characters: eight of bitmap, one matrix, one colour.</summary>
  public const int BytesPerCharacter = 10;

  /// <summary>Bytes a frame carries past its characters: its size and its background colour.</summary>
  public const int FrameTrailer = 2;

  static string IImageFormatMetadata<CommodoreGrafixFile>.PrimaryExtension => ".cgx";
  static string[] IImageFormatMetadata<CommodoreGrafixFile>.FileExtensions => [".cgx"];
  static CommodoreGrafixFile IImageFormatReader<CommodoreGrafixFile>.FromSpan(ReadOnlySpan<byte> data)
    => CommodoreGrafixReader.FromSpan(data);
  static byte[] IImageFormatWriter<CommodoreGrafixFile>.ToBytes(CommodoreGrafixFile file)
    => CommodoreGrafixWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<CommodoreGrafixFile>.VideoModes => [
    new("Grafix", [(IntegerRange.Any, IntegerRange.Any)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Offset of the frame data.</summary>
  public int DataOffset { get; init; }

  /// <summary>Frames across the sheet.</summary>
  public int MatrixColumns { get; init; }

  /// <summary>Frames down the sheet.</summary>
  public int MatrixRows { get; init; }

  /// <summary>Characters across one frame.</summary>
  public int FrameColumns { get; init; }

  /// <summary>Characters down one frame.</summary>
  public int FrameRows { get; init; }

  /// <summary>Pixels across the sheet.</summary>
  public int Width => MatrixColumns * FrameColumns << 3;

  /// <summary>Rows of the sheet.</summary>
  public int Height => MatrixRows * FrameRows << 3;

  public static RawImage ToRawImage(CommodoreGrafixFile file) {
    var data = file.Data ?? [];
    var width = file.Width;
    var characters = file.FrameColumns * file.FrameRows;
    var frameLength = characters * BytesPerCharacter + FrameTrailer;
    var pixels = new byte[width * file.Height];

    for (var row = 0; row < file.MatrixRows; ++row)
    for (var column = 0; column < file.MatrixColumns; ++column) {
      var frame = file.DataOffset + (row * file.MatrixColumns + column) * frameLength;

      // A frame's three planes follow one another: bitmap, then screen, then colour.
      var bitmap = frame;
      var matrix = frame + (characters << 3);
      var colors = frame + characters * 9;
      var background = data[frame + frameLength - 1] & 15;

      var left = column * file.FrameColumns << 3;
      var top = row * file.FrameRows << 3;

      for (var y = 0; y < file.FrameRows << 3; ++y)
      for (var x = 0; x < file.FrameColumns << 3; ++x) {
        var character = (y >> 3) * file.FrameColumns + (x >> 3);
        var pattern = (_At(data, bitmap + (character << 3) + (y & 7)) >> (~x & 6)) & 3;

        var color = pattern switch {
          1 => _At(data, matrix + character) >> 4,
          2 => _At(data, matrix + character),
          3 => _At(data, colors + character),
          _ => background,
        };

        pixels[(top + y) * width + left + x] = (byte)(color & 15);
      }
    }

    return new() {
      Width = width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = Commodore64Graphics.CreatePalette(),
      PaletteCount = Commodore64Graphics.ColorCount,
    };
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;

  /// <summary>The most characters either way a frame's count byte can state.</summary>
  public const int MaxFrameSide = 255;

  /// <summary>
  /// Encodes a picture as a sheet of one frame, which is what a still picture is.
  /// </summary>
  /// <remarks>
  /// The sheet exists so that a game's animation lives in one file rather than one per frame, and a
  /// picture is an animation of length one. Laying the same frame out several times would multiply
  /// the length and say nothing more.
  /// <para/>
  /// A frame is an ordinary multicolour screen: three colours a cell and one the whole frame shares.
  /// Unlike the machine's own colour memory the third is a whole byte here, so all sixteen colours
  /// can go in it rather than the low eight.
  /// </remarks>
  public static CommodoreGrafixFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var columns = Math.Clamp((image.Width + 4) / 8, 1, MaxFrameSide);
    var rows = Math.Clamp((image.Height + 4) / 8, 1, MaxFrameSide);
    var width = columns * 8;
    var height = rows * 8;
    var rgb = image.SampleTo(width, height).PixelData;

    // A multicolour pixel is two screen pixels wide, so only every other column is looked at.
    var logicalWidth = width >> 1;
    var indices = new int[logicalWidth * height];
    var counts = new int[Commodore64Graphics.ColorCount];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < logicalWidth; ++x) {
      var at = (y * width + x * 2) * 3;
      var index = Commodore64Graphics.FindNearestColorIndex(rgb[at], rgb[at + 1], rgb[at + 2]);
      indices[y * logicalWidth + x] = index;
      ++counts[index];
    }

    var background = 0;
    for (var colour = 1; colour < Commodore64Graphics.ColorCount; ++colour)
      if (counts[colour] > counts[background])
        background = colour;

    var distance = _DistanceTable();
    var characters = columns * rows;
    var frame = new byte[characters * BytesPerCharacter + FrameTrailer];
    frame[^1] = (byte)background;

    Span<int> cell = stackalloc int[32];
    Span<int> chosen = stackalloc int[3];

    for (var row = 0; row < rows; ++row)
    for (var column = 0; column < columns; ++column) {
      for (var y = 0; y < 8; ++y)
      for (var pixel = 0; pixel < 4; ++pixel)
        cell[y * 4 + pixel] = indices[(row * 8 + y) * logicalWidth + column * 4 + pixel];

      _ChooseTriple(cell, distance, background, chosen);

      var character = row * columns + column;
      for (var y = 0; y < 8; ++y) {
        var bits = 0;
        for (var pixel = 0; pixel < 4; ++pixel)
          bits |= _Pattern(distance, cell[y * 4 + pixel], background, chosen) << ((3 - pixel) << 1);

        frame[(character << 3) + y] = (byte)bits;
      }

      frame[(characters << 3) + character] = (byte)((chosen[0] << 4) | chosen[1]);
      frame[characters * 9 + character] = (byte)chosen[2];
    }

    return new() {
      Data = CommodoreGrafixWriter.Assemble(columns, rows, frame),
      DataOffset = CommodoreGrafixWriter.DataChunkOffset,
      MatrixColumns = 1,
      MatrixRows = 1,
      FrameColumns = columns,
      FrameRows = rows,
    };
  }

  /// <summary>Squared distance between every pair of the machine's colours.</summary>
  private static int[] _DistanceTable() {
    var table = new int[Commodore64Graphics.ColorCount * Commodore64Graphics.ColorCount];
    for (var left = 0; left < Commodore64Graphics.ColorCount; ++left)
    for (var right = 0; right < Commodore64Graphics.ColorCount; ++right) {
      int a = Commodore64Graphics.HexColors[left], b = Commodore64Graphics.HexColors[right];
      int dr = ((a >> 16) & 255) - ((b >> 16) & 255);
      int dg = ((a >> 8) & 255) - ((b >> 8) & 255);
      int db = (a & 255) - (b & 255);
      table[left * Commodore64Graphics.ColorCount + right] = dr * dr + dg * dg + db * db;
    }

    return table;
  }

  /// <summary>
  /// The three colours a cell's own bytes name, chosen as the triple with the least total error.
  /// </summary>
  /// <remarks>
  /// Every triple is tried rather than the three commonest kept: in a cell holding three near
  /// shades and one contrasting mark the mark is rare, and a frequency count discards the pixel
  /// most visible to anyone looking at the picture.
  /// </remarks>
  private static void _ChooseTriple(
    ReadOnlySpan<int> cell, ReadOnlySpan<int> distance, int background, Span<int> chosen) {
    var bestCost = long.MaxValue;

    for (var first = 0; first < Commodore64Graphics.ColorCount; ++first)
    for (var second = first; second < Commodore64Graphics.ColorCount; ++second)
    for (var third = second; third < Commodore64Graphics.ColorCount; ++third) {
      long cost = 0;
      foreach (var index in cell) {
        var at = index * Commodore64Graphics.ColorCount;
        var best = distance[at + background];
        best = Math.Min(best, distance[at + first]);
        best = Math.Min(best, distance[at + second]);
        cost += Math.Min(best, distance[at + third]);
      }

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      chosen[0] = first;
      chosen[1] = second;
      chosen[2] = third;
    }
  }

  private static int _Pattern(ReadOnlySpan<int> distance, int index, int background, ReadOnlySpan<int> chosen) {
    var at = index * Commodore64Graphics.ColorCount;
    var pattern = 0;
    var best = distance[at + background];

    for (var i = 0; i < 3; ++i) {
      var cost = distance[at + chosen[i]];
      if (cost >= best)
        continue;

      best = cost;
      pattern = i + 1;
    }

    return pattern;
  }
}
