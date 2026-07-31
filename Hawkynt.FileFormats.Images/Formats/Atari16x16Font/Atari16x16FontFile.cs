using System;
using FileFormat.Core;

namespace FileFormat.Atari16x16Font;

/// <summary>In-memory representation of an Atari 8-bit 16x16 font (.sxs).</summary>
/// <remarks>
/// A character set meant to be read as sixteen-pixel glyphs: each one is four of the machine's 8x8
/// characters arranged in a square. The file stores them as an ordinary 128-glyph set behind an
/// Atari executable header, and the pairing is implicit in the numbering rather than written down.
/// <para/>
/// Laying it out for viewing therefore means undoing that numbering. The map below puts the four
/// quarters of each large glyph next to each other, which is why the sheet is 32 characters across
/// and only four rows deep.
/// </remarks>
public readonly record struct Atari16x16FontFile
  : IImageFormatReader<Atari16x16FontFile>, IImageToRawImage<Atari16x16FontFile>,
    IImageFromRawImage<Atari16x16FontFile>, IImageFormatWriter<Atari16x16FontFile> {

  /// <summary>Where the machine is told to load the character set.</summary>
  /// <remarks>
  /// A character set has to start on a page boundary the hardware can point ANTIC at, and this is
  /// the one the file names. Nothing about the picture depends on it, but keeping it means a file
  /// read and written back is the file that came in.
  /// </remarks>
  public const int DefaultLoadAddress = 0x8000;

  /// <summary>Size of the Atari executable header the file opens with.</summary>
  public const int HeaderSize = 6;

  /// <summary>Bytes of glyph data.</summary>
  public const int GlyphDataSize = 1024;

  /// <summary>Total file size.</summary>
  public const int FileSize = HeaderSize + GlyphDataSize;

  /// <summary>Glyphs the set holds.</summary>
  public const int GlyphCount = 128;

  /// <summary>Scanlines one glyph spans.</summary>
  public const int GlyphHeight = 8;

  /// <summary>Glyphs shown side by side.</summary>
  public const int Columns = 32;

  /// <summary>Displayed width.</summary>
  public const int Width = Columns * 8;

  /// <summary>Displayed height.</summary>
  public const int Height = GlyphCount / Columns * GlyphHeight;

  /// <summary>GTIA colour of the background.</summary>
  public const byte BackgroundColor = 0;

  /// <summary>GTIA colour of the foreground; Graphics 0 takes only its luminance.</summary>
  public const byte ForegroundColor = 14;

  static string IImageFormatMetadata<Atari16x16FontFile>.PrimaryExtension => ".sxs";
  static string[] IImageFormatMetadata<Atari16x16FontFile>.FileExtensions => [".sxs"];
  static Atari16x16FontFile IImageFormatReader<Atari16x16FontFile>.FromSpan(ReadOnlySpan<byte> data)
    => Atari16x16FontReader.FromSpan(data);
  static byte[] IImageFormatWriter<Atari16x16FontFile>.ToBytes(Atari16x16FontFile file)
    => Atari16x16FontWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<Atari16x16FontFile>.VideoModes => [
    new("16x16 font", [(Width, Height)], [2])
  ];

  /// <summary>The glyph data, eight bytes a glyph.</summary>
  public byte[] GlyphData { get; init; }

  /// <summary>The address the executable header names, kept so a file survives a round trip.</summary>
  public int LoadAddress { get; init; }

  /// <summary>
  /// Which stored glyph belongs at a position on the sheet.
  /// </summary>
  /// <remarks>
  /// The four quarters of one large glyph are not consecutive in the set, so the sheet is not the
  /// file in order. This unshuffles the numbering: bit 4 of the position becomes bit 1 of the glyph
  /// and the middle bits shift up to make room, which puts each large glyph's four pieces adjacent.
  /// </remarks>
  public static int GlyphAt(int position)
    => (position & 65) | ((position >> 4) & 2) | ((position & 30) << 1);

  public static RawImage ToRawImage(Atari16x16FontFile file) {
    var font = file.GlyphData ?? [];
    var gtia = Atari8BitGraphics.Palette;

    // Graphics 0 takes the hue from the background register and only the luminance from the
    // foreground, so the two can differ in brightness alone.
    var foreground = (byte)((BackgroundColor & 240) | (ForegroundColor & 14));
    var palette = new byte[6];
    gtia.Slice(BackgroundColor * 3, 3).CopyTo(palette);
    gtia.Slice(foreground * 3, 3).CopyTo(palette.AsSpan(3));

    var pixels = new byte[Width * Height];
    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var character = GlyphAt((y / GlyphHeight) * Columns + (x >> 3));
      var index = (character & (GlyphCount - 1)) * GlyphHeight + (y % GlyphHeight);
      var row = index < font.Length ? font[index] : 0;
      // The code's top bit inverts the glyph rather than selecting one.
      pixels[y * Width + x] = (byte)(((row >> (~x & 7)) ^ (character >> 7)) & 1);
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = 2,
    };
  }

  /// <summary>Reads the sheet back into the set, reshuffling the quarters into their stored order.</summary>
  /// <remarks>
  /// The sheet shows each large glyph's four quarters side by side, which is not the order the file
  /// keeps them in; writing therefore walks the sheet and sends each cell to the glyph
  /// <see cref="GlyphAt"/> names, which is the same map the reader follows and so undoes it exactly.
  /// </remarks>
  public static Atari16x16FontFile FromRawImage(RawImage image) {
    var set = GlyphSheet.Sample(image, Width, Height);
    var font = new byte[GlyphDataSize];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var character = GlyphAt(y / GlyphHeight * Columns + (x >> 3));

      // The code's top bit inverts the glyph, so an inverted cell stores the opposite of what it
      // shows. Nothing on this sheet is inverted, but following the reader's rule costs nothing.
      var inverted = (character >> 7) & 1;
      if (((set[y * Width + x] ? 1 : 0) ^ inverted) == 0)
        continue;

      font[(character & (GlyphCount - 1)) * GlyphHeight + y % GlyphHeight] |= (byte)(1 << (~x & 7));
    }

    return new() { GlyphData = font, LoadAddress = DefaultLoadAddress };
  }
}
