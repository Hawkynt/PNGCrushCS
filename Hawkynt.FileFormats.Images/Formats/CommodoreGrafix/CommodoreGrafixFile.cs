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
  : IImageFormatReader<CommodoreGrafixFile>, IImageToRawImage<CommodoreGrafixFile> {

  /// <summary>Bytes a frame spends on each of its characters: eight of bitmap, one matrix, one colour.</summary>
  public const int BytesPerCharacter = 10;

  /// <summary>Bytes a frame carries past its characters: its size and its background colour.</summary>
  public const int FrameTrailer = 2;

  static string IImageFormatMetadata<CommodoreGrafixFile>.PrimaryExtension => ".cgx";
  static string[] IImageFormatMetadata<CommodoreGrafixFile>.FileExtensions => [".cgx"];
  static CommodoreGrafixFile IImageFormatReader<CommodoreGrafixFile>.FromSpan(ReadOnlySpan<byte> data)
    => CommodoreGrafixReader.FromSpan(data);
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
}
