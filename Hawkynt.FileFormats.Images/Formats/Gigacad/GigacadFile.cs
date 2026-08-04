using System;
using FileFormat.Core;

namespace FileFormat.Gigacad;

/// <summary>In-memory representation of an Atari ST GigaCAD monochrome image (640x400, 1 bitplane).</summary>
public readonly record struct GigacadFile : IImageFormatReader<GigacadFile>, IImageToRawImage<GigacadFile>, IImageFromRawImage<GigacadFile>, IImageFormatWriter<GigacadFile> {

  /// <summary>
  /// The size of the only form any sample takes: a load address and eight kilobytes.
  /// </summary>
  /// <remarks>
  /// This reader expected 32000 bytes at 640 by 400, which is an Atari ST screen and is not what the
  /// corpus holds — the one GigaCAD picture is 8194 bytes and both RECOIL and XnView draw it 320 by
  /// 200, which is a Commodore screen. It was refused outright while both tools read it.
  /// <para/>
  /// The 640 by 400 form is kept because nothing here disproves it, but nothing confirms it either;
  /// no sample of that size exists to check against.
  /// </remarks>
  public const int CommodoreFileSize = 8194;

  /// <summary>Bytes of screen a Commodore picture holds, after two of load address.</summary>
  internal const int CommodoreScreenSize = 8000;

  /// <summary>The exact file size: 80 bytes/line x 400 lines = 32000 bytes.</summary>
  public const int ExpectedFileSize = 32000;

  static string IImageFormatMetadata<GigacadFile>.PrimaryExtension => ".gcd";
  static string[] IImageFormatMetadata<GigacadFile>.FileExtensions => [".gcd"];
  static GigacadFile IImageFormatReader<GigacadFile>.FromSpan(ReadOnlySpan<byte> data) => GigacadReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<GigacadFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])
  ];
  static byte[] IImageFormatWriter<GigacadFile>.ToBytes(GigacadFile file) => GigacadWriter.ToBytes(file);

  /// <summary>640 for an Atari screen, 320 for a Commodore one.</summary>
  public int Width { get; init; }

  /// <summary>400 for an Atari screen, 200 for a Commodore one.</summary>
  public int Height { get; init; }

  /// <summary>
  /// Whether a set bit is paper rather than ink, which is how the Commodore form has it.
  /// </summary>
  /// <remarks>
  /// The two forms run opposite ways. Read with the Atari convention the Commodore sample came out as
  /// its own negative against RECOIL and XnView, which agree with each other; read this way it matches
  /// both on every pixel.
  /// </remarks>
  public bool SetBitIsPaper { get; init; }

  /// <summary>Raw monochrome bitmap data (1 bit per pixel, 32000 bytes total).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>
  /// Reduces a picture to the Commodore screen, 320 by 200, a clear bit standing for ink.
  /// </summary>
  /// <remarks>
  /// The reader takes two shapes and this writes the smaller: both samples are the Commodore one at
  /// 8194 bytes, and RECOIL refuses the Atari length of 32000 at this extension. The rows are dealt
  /// back into character cells on the way out, eight lines of one cell together, which is how the
  /// machine holds a bitmap and not how a picture is drawn.
  /// <para/>
  /// A set bit is paper here rather than ink, which is the opposite of most screens of the period
  /// and is what the samples are.
  /// </remarks>
  public static GigacadFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    const int width = 320;
    const int height = 200;
    const int bytesPerRow = width / 8;

    var rgb = image.SampleTo(width, height).PixelData;
    var rows = new byte[CommodoreScreenSize];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 3;
        var brightness = (rgb[at] * 299 + rgb[at + 1] * 587 + rgb[at + 2] * 114) / 1000;
        if (brightness >= 128)
          rows[y * bytesPerRow + x / 8] |= (byte)(1 << (7 - (x % 8)));
      }

    return new() { Width = width, Height = height, SetBitIsPaper = true, PixelData = rows };
  }

  /// <summary>Deals rows back into character cells, which is how the machine holds them.</summary>
  internal static byte[] RowsToCells(ReadOnlySpan<byte> rows, int width, int height) {
    var cells = new byte[rows.Length];
    var columns = width / 8;

    for (var cellRow = 0; cellRow < height / 8; ++cellRow)
      for (var cellColumn = 0; cellColumn < columns; ++cellColumn)
        for (var line = 0; line < 8; ++line)
          cells[(cellRow * columns + cellColumn) * 8 + line] = rows[(cellRow * 8 + line) * columns + cellColumn];

    return cells;
  }

  public static RawImage ToRawImage(GigacadFile file) {

    var width = file.Width > 0 ? file.Width : 640;
    var height = file.Height > 0 ? file.Height : 400;
    var bytesPerRow = width / 8;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var byteIndex = y * bytesPerRow + x / 8;
        var bitIndex = 7 - (x % 8);
        var isSet = byteIndex < file.PixelData.Length && (file.PixelData[byteIndex] & (1 << bitIndex)) != 0;
        var color = isSet == file.SetBitIsPaper ? (byte)255 : (byte)0;
        var offset = (y * width + x) * 3;
        rgb[offset] = color;
        rgb[offset + 1] = color;
        rgb[offset + 2] = color;
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Puts a screen held a character cell at a time back into rows, as a Commodore holds it.</summary>
  internal static byte[] CellsToRows(ReadOnlySpan<byte> cells, int width, int height) {
    var rows = new byte[cells.Length];
    var columns = width / 8;

    for (var cellRow = 0; cellRow < height / 8; ++cellRow)
      for (var cellColumn = 0; cellColumn < columns; ++cellColumn)
        for (var line = 0; line < 8; ++line)
          rows[(cellRow * 8 + line) * columns + cellColumn] = cells[(cellRow * columns + cellColumn) * 8 + line];

    return rows;
  }

}
