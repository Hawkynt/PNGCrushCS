using System;
using System.IO;
using FileFormat.Core;
using FileFormat.TextMode;

namespace FileFormat.CommodorePet;

/// <summary>Commodore PET PETSCII screen dump data model.</summary>
public sealed class CommodorePetFile : IImageFormatReader<CommodorePetFile>, IImageToRawImage<CommodorePetFile>, IImageFromRawImage<CommodorePetFile>, IImageFormatWriter<CommodorePetFile> {

  public const int FileSize = 1000;

  /// <summary>Character cells across the screen.</summary>
  public const int Columns = 40;

  /// <summary>Character cells down the screen.</summary>
  public const int Rows = 25;

  /// <summary>Pixels along one side of a cell.</summary>
  public const int CellSize = 8;

  /// <summary>Bytes of load address a saved screen begins with.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Cells the screen holds, which is also the length of each of the two areas.</summary>
  internal const int CellCount = Columns * Rows;

  public const int ImageWidth = Columns * CellSize;
  public const int ImageHeight = Rows * CellSize;

  public int Width { get; init; } = ImageWidth;
  public int Height { get; init; } = ImageHeight;

  /// <summary>One character code a cell.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>One colour a cell, taken from the colour area that follows the screen.</summary>
  public byte[] CellColors { get; init; } = [];

  public byte[] Palette { get; init; } = new byte[768];

  /// <summary>The machine's character ROM, one byte per glyph row.</summary>
  private static ReadOnlySpan<byte> Font => BitmapFontEmbedded.C64PetsciiGraphics8x8.GlyphData;

  public static string PrimaryExtension => ".pet";
  public static string[] FileExtensions => [".pet"];
  static CommodorePetFile IImageFormatReader<CommodorePetFile>.FromSpan(ReadOnlySpan<byte> data) => CommodorePetReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<CommodorePetFile>.VideoModes => [
    new("Default", [(40, 25)], [new IntegerRange(2, 256)])
  ];
  public static CommodorePetFile FromFile(FileInfo file) => CommodorePetReader.FromFile(file);
  public static CommodorePetFile FromBytes(byte[] data) => CommodorePetReader.FromBytes(data);
  public static CommodorePetFile FromStream(Stream stream) => CommodorePetReader.FromStream(stream);
  public static byte[] ToBytes(CommodorePetFile file) => CommodorePetWriter.ToBytes(file);

  /// <summary>
  /// Draws the screen the file holds.
  /// </summary>
  /// <remarks>
  /// What came back before was the character codes themselves, one to a pixel, as a picture 40 by 25
  /// with a 256-entry palette — so a screenful of art arrived as a thumbnail of meaningless indices.
  /// The codes are not the picture: each names a glyph in the machine's character ROM, and the
  /// picture is those glyphs drawn eight pixels square in the colour the second area gives them.
  /// <para/>
  /// Checked against RECOIL, which draws the same 320 by 200.
  /// </remarks>
  public static RawImage ToRawImage(CommodorePetFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var font = Font;
    var pixels = new byte[ImageWidth * ImageHeight];
    for (var y = 0; y < ImageHeight; ++y)
    for (var x = 0; x < ImageWidth; ++x) {
      var cell = y / CellSize * Columns + x / CellSize;
      var character = cell < file.PixelData.Length ? file.PixelData[cell] : (byte)32;

      // The top bit of a code inverts the glyph rather than choosing another one.
      var row = font[(character & 0x7F) * CellSize + y % CellSize];
      var lit = (((row >> (~x & 7)) ^ (character >> 7)) & 1) != 0;

      pixels[y * ImageWidth + x] = lit && cell < file.CellColors.Length ? (byte)(file.CellColors[cell] & 15) : (byte)0;
    }

    return new RawImage {
      Width = ImageWidth,
      Height = ImageHeight,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = Commodore64Graphics.CreatePalette(),
      PaletteCount = Commodore64Graphics.ColorCount,
    };
  }

  public static CommodorePetFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Indexed8);
    if (image.Width != ImageWidth || image.Height != ImageHeight)
      throw new ArgumentException($"Expected {ImageWidth}x{ImageHeight}, got {image.Width}x{image.Height}");

    // One cell of the screen at a time, choosing the glyph whose lit pixels best match the block and
    // the colour that appears most often among them.
    var codes = new byte[CellCount];
    var colors = new byte[CellCount];
    var font = Font;
    Span<int> tally = stackalloc int[16];

    for (var row = 0; row < Rows; ++row)
    for (var column = 0; column < Columns; ++column) {
      tally.Clear();
      for (var y = 0; y < CellSize; ++y)
      for (var x = 0; x < CellSize; ++x)
        ++tally[image.PixelData[(row * CellSize + y) * ImageWidth + column * CellSize + x] & 15];

      var ink = 1;
      for (var i = 1; i < 16; ++i)
        if (tally[i] > tally[ink])
          ink = i;

      // All 256 codes, because the top bit inverts the glyph rather than choosing another one —
      // searching only the first 128 cannot produce a solid block, which is an inverted space.
      var best = 32;
      var bestScore = -1;
      for (var candidate = 0; candidate < 256; ++candidate) {
        var score = 0;
        for (var y = 0; y < CellSize; ++y) {
          var bits = font[(candidate & 0x7F) * CellSize + y];
          for (var x = 0; x < CellSize; ++x) {
            var wanted = (image.PixelData[(row * CellSize + y) * ImageWidth + column * CellSize + x] & 15) != 0;
            var lit = (((bits >> (~x & 7)) ^ (candidate >> 7)) & 1) != 0;
            if (lit == wanted)
              ++score;
          }
        }

        if (score > bestScore) {
          bestScore = score;
          best = candidate;
        }
      }

      var cell = row * Columns + column;
      codes[cell] = (byte)best;
      colors[cell] = (byte)ink;
    }

    return new CommodorePetFile {
      PixelData = codes,
      CellColors = colors,
      Palette = Commodore64Graphics.CreatePalette(),
    };
  }
}
