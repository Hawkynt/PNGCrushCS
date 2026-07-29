using System;
using FileFormat.Core;

namespace FileFormat.Commodore64Font;

/// <summary>In-memory representation of a Commodore 64 character set (.64c, .g).</summary>
/// <remarks>
/// A two-byte load address, then eight bytes per glyph, one bit per pixel. Laid out for viewing,
/// thirty-two glyphs sit side by side across 256 pixels and the set runs down in rows of eight
/// scanlines — so a glyph's eight bytes are consecutive in the file but vertical on screen.
/// <para/>
/// There are no colours in the file. A character set is not a picture: the machine draws it in
/// whatever the screen's registers happen to hold, so it is shown as white on black.
/// </remarks>
public readonly record struct Commodore64FontFile
  : IImageFormatReader<Commodore64FontFile>, IImageToRawImage<Commodore64FontFile>,
    IImageFromRawImage<Commodore64FontFile>, IImageFormatWriter<Commodore64FontFile> {

  /// <summary>Size of the load address.</summary>
  public const int HeaderSize = 2;

  /// <summary>Scanlines one glyph spans.</summary>
  public const int GlyphHeight = 8;

  /// <summary>Glyphs shown side by side.</summary>
  public const int GlyphsPerRow = 32;

  /// <summary>Displayed width.</summary>
  public const int Width = GlyphsPerRow * 8;

  /// <summary>Bytes one row of glyphs occupies.</summary>
  public const int BytesPerGlyphRow = GlyphsPerRow * GlyphHeight;

  /// <summary>Smallest file we accept.</summary>
  public const int MinFileSize = 10;

  /// <summary>Largest file we accept: 256 glyphs plus the load address.</summary>
  public const int MaxFileSize = HeaderSize + 256 * GlyphHeight;

  /// <summary>Size of a SEUCK character set, which is fixed at 64 glyphs.</summary>
  public const int SeuckFileSize = HeaderSize + 64 * GlyphHeight;

  /// <summary>Low byte of the load address a SEUCK set carries.</summary>
  public const byte SeuckLoadAddressLow = 66;

  static string IImageFormatMetadata<Commodore64FontFile>.PrimaryExtension => ".64c";
  static string[] IImageFormatMetadata<Commodore64FontFile>.FileExtensions => [".64c", ".g"];
  static Commodore64FontFile IImageFormatReader<Commodore64FontFile>.FromSpan(ReadOnlySpan<byte> data)
    => Commodore64FontReader.FromSpan(data);
  static byte[] IImageFormatWriter<Commodore64FontFile>.ToBytes(Commodore64FontFile file)
    => Commodore64FontWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<Commodore64FontFile>.VideoModes => [
    new("Character set", [(Width, 64)], [2]),
    new("SEUCK font", [(Width, 16)], [2]),
  ];

  /// <summary>
  /// Displayed height for a file of a given length: whole rows of glyphs, rounded up.
  /// </summary>
  public static int HeightFor(int fileSize) => ((fileSize + 253) >> 8) << 3;

  /// <summary>Which of the two sets this is.</summary>
  public Commodore64FontKind Kind { get; init; }

  /// <summary>The glyph bytes, eight per glyph, without the load address.</summary>
  public byte[] GlyphData { get; init; }

  /// <summary>Displayed height.</summary>
  public int Height => HeightFor(HeaderSize + (this.GlyphData?.Length ?? 0));

  public static RawImage ToRawImage(Commodore64FontFile file) {
    var data = file.GlyphData ?? [];
    var height = file.Height;
    var pixels = new byte[Width * height];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < Width; ++x) {
      // A glyph's rows are consecutive bytes, so the row within the glyph is the low part of the
      // offset and the glyph's column picks a block of eight.
      var row = y % GlyphHeight;
      var offset = (y - row) * GlyphsPerRow + (x >> 3) * GlyphHeight + row;
      var b = offset < data.Length ? data[offset] : 0;
      pixels[y * Width + x] = (byte)((b >> (~x & 7)) & 1);
    }

    return new() {
      Width = Width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = [0, 0, 0, 255, 255, 255],
      PaletteCount = 2,
    };
  }

  public static Commodore64FontFile FromRawImage(RawImage image) => FromRawImage(image, Commodore64FontKind.CharacterSet);

  /// <summary>Encodes a character set of a chosen kind.</summary>
  public static Commodore64FontFile FromRawImage(RawImage image, Commodore64FontKind kind) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != Width)
      throw new ArgumentException($"A character set is {Width} pixels wide, got {image.Width}.", nameof(image));
    if (image.Height < GlyphHeight || image.Height % GlyphHeight != 0)
      throw new ArgumentException($"A character set is a whole number of {GlyphHeight}-scanline rows, got {image.Height}.", nameof(image));

    var rows = image.Height / GlyphHeight;
    if (kind == Commodore64FontKind.SeuckFont && rows != 2)
      throw new ArgumentException($"A SEUCK set is 64 glyphs, which is two rows; got {rows}.", nameof(image));

    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var data = new byte[rows * BytesPerGlyphRow];

    for (var y = 0; y < image.Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var pixel = (y * Width + x) * 4;
      // Only two colours exist and neither is stored; a pixel is set when it is more light than dark.
      if (bgra.PixelData[pixel] + bgra.PixelData[pixel + 1] + bgra.PixelData[pixel + 2] < 384)
        continue;

      var row = y % GlyphHeight;
      data[(y - row) * GlyphsPerRow + (x >> 3) * GlyphHeight + row] |= (byte)(0x80 >> (x & 7));
    }

    return new() { Kind = kind, GlyphData = data };
  }
}
