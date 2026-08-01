using System;
using FileFormat.Core;

namespace FileFormat.SpecScii;

/// <summary>In-memory representation of a SpecSCII picture (.zxs).</summary>
/// <remarks>
/// A Spectrum screen drawn out of a character set rather than as a bitmap: 112 characters, and then
/// one index and one attribute per cell. That is what makes the file 2452 bytes where a screen is
/// 6912 — a picture built from repeated shapes costs a byte a cell instead of eight.
/// <para/>
/// The two cell maps are stored column by column rather than row by row, so consecutive bytes run
/// down the screen and not across it.
/// </remarks>
public readonly record struct SpecSciiFile
  : IImageFormatReader<SpecSciiFile>, IImageToRawImage<SpecSciiFile>,
    IImageFromRawImage<SpecSciiFile>, IImageFormatWriter<SpecSciiFile> {

  static byte[] IImageFormatWriter<SpecSciiFile>.ToBytes(SpecSciiFile file) => SpecSciiWriter.ToBytes(file);

  /// <summary>Pixels across.</summary>
  public const int Width = ZxSpectrumGraphics.ScreenWidth;

  /// <summary>Rows.</summary>
  public const int Height = ZxSpectrumGraphics.ScreenHeight;

  /// <summary>Cells across.</summary>
  public const int Columns = Width / 8;

  /// <summary>Cell rows.</summary>
  public const int Rows = Height / 8;

  /// <summary>Characters the set holds.</summary>
  public const int CharacterCount = 112;

  /// <summary>Offset of the character set.</summary>
  public const int CharactersOffset = 12;

  /// <summary>Offset of the cell indices.</summary>
  public const int ScreenOffset = CharactersOffset + CharacterCount * 8;

  /// <summary>Offset of the cell attributes.</summary>
  public const int AttributeOffset = ScreenOffset + Columns * Rows;

  /// <summary>Total file size.</summary>
  public const int FileSize = AttributeOffset + Columns * Rows + 8;

  /// <summary>The string every file starts with.</summary>
  public const string Signature = "ZX_SSCII";

  /// <summary>Where the stored length sits, right after the signature.</summary>
  public const int LengthOffset = 8;

  static string IImageFormatMetadata<SpecSciiFile>.PrimaryExtension => ".zxs";
  static string[] IImageFormatMetadata<SpecSciiFile>.FileExtensions => [".zxs"];
  static SpecSciiFile IImageFormatReader<SpecSciiFile>.FromSpan(ReadOnlySpan<byte> data)
    => SpecSciiReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<SpecSciiFile>.VideoModes => [
    new("SpecSCII", [(Width, Height)], [ZxSpectrumGraphics.PaletteEntryCount])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(SpecSciiFile file) {
    var data = file.Data ?? [];
    var pixels = new byte[Width * Height];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      // Column-major: a cell's index is its column times the row count plus its row.
      var cell = (x >> 3) * Rows + (y >> 3);
      var character = data[ScreenOffset + cell];
      var attribute = data[AttributeOffset + cell];

      var at = CharactersOffset + character * 8 + (y & 7);
      var ink = at < data.Length && ((data[at] >> (~x & 7)) & 1) != 0;
      pixels[y * Width + x] = (byte)ZxSpectrumGraphics.ColorIndex(attribute, ink);
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = ZxSpectrumGraphics.Palette.ToArray(),
      PaletteCount = ZxSpectrumGraphics.PaletteEntryCount,
    };
  }

  /// <summary>Builds a screen, defining the character set from the picture itself.</summary>
  /// <remarks>
  /// Unlike most character screens this one carries its own shapes, so the writer chooses them
  /// rather than matching against a set it was given. Every cell is reduced to two colours first —
  /// trying each ink, paper and brightness and keeping whichever splits that cell best — and the
  /// eight-byte patterns that fall out are collected. A picture using no more than 112 distinct
  /// patterns comes back exactly; beyond that the commonest 112 are kept and the rest take whichever
  /// of those differs in the fewest pixels.
  /// </remarks>
  public static SpecSciiFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height).PixelData;
    var patterns = new ulong[Columns * Rows];
    var attributes = new byte[Columns * Rows];

    for (var row = 0; row < Rows; ++row)
    for (var column = 0; column < Columns; ++column) {
      var cell = column * Rows + row;
      (patterns[cell], attributes[cell]) = _EncodeCell(rgb, column * 8, row * 8);
    }

    var chosen = _ChooseCharacterSet(patterns);

    var data = new byte[FileSize];
    System.Text.Encoding.ASCII.GetBytes(Signature).CopyTo(data.AsSpan(0));

    // A four-byte length follows the signature. Our own reader never looked at it, so leaving it
    // zero round-tripped here and was turned away by everything else.
    data[LengthOffset] = (byte)(FileSize & 0xFF);
    data[LengthOffset + 1] = (byte)(FileSize >> 8);

    for (var i = 0; i < chosen.Count; ++i)
    for (var y = 0; y < 8; ++y)
      data[CharactersOffset + i * 8 + y] = (byte)(chosen[i] >> (56 - y * 8));

    for (var cell = 0; cell < patterns.Length; ++cell) {
      data[ScreenOffset + cell] = (byte)_NearestPattern(chosen, patterns[cell]);
      data[AttributeOffset + cell] = attributes[cell];
    }

    return new() { Data = data };
  }

  /// <summary>Reduces one cell to two colours, returning its shape and the attribute that names them.</summary>
  private static (ulong Pattern, byte Attribute) _EncodeCell(ReadOnlySpan<byte> rgb, int x0, int y0) {
    var palette = ZxSpectrumGraphics.Palette;
    byte bestAttribute = 0;
    var bestCost = long.MaxValue;

    for (var bright = 0; bright < 2; ++bright)
    for (var ink = 0; ink < 8; ++ink)
    for (var paper = 0; paper < 8; ++paper) {
      long cost = 0;
      for (var y = y0; y < y0 + 8; ++y)
      for (var x = x0; x < x0 + 8; ++x) {
        var at = (y * Width + x) * 3;
        cost += Math.Min(
          _Distance(rgb, at, palette, bright * 8 + ink), _Distance(rgb, at, palette, bright * 8 + paper));
      }

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      bestAttribute = (byte)((bright << 6) | (paper << 3) | ink);
    }

    var inkEntry = ZxSpectrumGraphics.ColorIndex(bestAttribute, true);
    var paperEntry = ZxSpectrumGraphics.ColorIndex(bestAttribute, false);

    ulong pattern = 0;
    for (var y = y0; y < y0 + 8; ++y)
    for (var x = x0; x < x0 + 8; ++x) {
      var at = (y * Width + x) * 3;
      if (_Distance(rgb, at, palette, inkEntry) < _Distance(rgb, at, palette, paperEntry))
        pattern |= 1UL << (63 - ((y - y0) * 8 + (x - x0)));
    }

    return (pattern, bestAttribute);
  }

  /// <summary>Keeps the shapes the picture uses most, up to as many as the file has room for.</summary>
  private static System.Collections.Generic.List<ulong> _ChooseCharacterSet(ulong[] patterns) {
    var counts = new System.Collections.Generic.Dictionary<ulong, int>();
    foreach (var pattern in patterns)
      counts[pattern] = counts.TryGetValue(pattern, out var seen) ? seen + 1 : 1;

    var ordered = new System.Collections.Generic.List<ulong>(counts.Keys);
    ordered.Sort((a, b) => counts[b].CompareTo(counts[a]));

    if (ordered.Count > CharacterCount)
      ordered.RemoveRange(CharacterCount, ordered.Count - CharacterCount);

    return ordered;
  }

  /// <summary>The kept shape differing from a wanted one in the fewest pixels.</summary>
  private static int _NearestPattern(System.Collections.Generic.List<ulong> chosen, ulong wanted) {
    var best = 0;
    var bestCost = int.MaxValue;

    for (var i = 0; i < chosen.Count; ++i) {
      var cost = System.Numerics.BitOperations.PopCount(chosen[i] ^ wanted);
      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = i;
      if (cost == 0)
        break;
    }

    return best;
  }

  private static long _Distance(ReadOnlySpan<byte> rgb, int at, ReadOnlySpan<byte> palette, int color) {
    var entry = color * 3;
    long dr = rgb[at] - palette[entry], dg = rgb[at + 1] - palette[entry + 1], db = rgb[at + 2] - palette[entry + 2];

    return dr * dr + dg * dg + db * db;
  }
}
