using System;
using FileFormat.Core;

namespace FileFormat.JetGraphicsPlanner;

/// <summary>In-memory representation of a Jet Graphics Planner font (.jgp) for the Atari 8-bit.</summary>
/// <remarks>
/// A 256-glyph character set for ANTIC mode 4, laid out as a sheet of thirty-two glyphs across and
/// eight bands down. Mode 4 reads two bits a pixel, so a glyph is four pixels wide and the sheet is
/// 256 by 64.
/// <para/>
/// The glyph number's top bit is a colour switch rather than part of the number: the fourth pixel
/// value comes from one playfield register below 128 and a different one at or above it. That is
/// what lets a mode 4 screen show five colours from four registers, and why the second half of the
/// sheet looks different from the first even where the shapes repeat.
/// </remarks>
public readonly record struct JetGraphicsPlannerFile
  : IImageFormatReader<JetGraphicsPlannerFile>, IImageToRawImage<JetGraphicsPlannerFile> {

  /// <summary>Size of the Atari executable header the file opens with.</summary>
  public const int HeaderSize = 6;

  /// <summary>Bytes of glyph data.</summary>
  public const int GlyphDataSize = 2048;

  /// <summary>Total file size.</summary>
  public const int FileSize = HeaderSize + GlyphDataSize;

  /// <summary>Glyphs shown side by side.</summary>
  public const int Columns = 32;

  /// <summary>Bands of glyphs down the sheet.</summary>
  public const int Bands = 8;

  /// <summary>Scanlines one glyph spans.</summary>
  public const int GlyphHeight = 8;

  /// <summary>Displayed width; a glyph is four pixels drawn two wide.</summary>
  public const int Width = Columns * 8;

  /// <summary>Displayed height.</summary>
  public const int Height = Bands * GlyphHeight;

  /// <summary>The GTIA colours mode 4 draws with: background, then three playfield registers.</summary>
  public static ReadOnlySpan<byte> Registers => [0, 4, 8, 12];

  /// <summary>The colour an inverse glyph draws its fourth pixel value in.</summary>
  public const byte InverseRegister = 0;

  static string IImageFormatMetadata<JetGraphicsPlannerFile>.PrimaryExtension => ".jgp";
  static string[] IImageFormatMetadata<JetGraphicsPlannerFile>.FileExtensions => [".jgp"];
  static JetGraphicsPlannerFile IImageFormatReader<JetGraphicsPlannerFile>.FromSpan(ReadOnlySpan<byte> data)
    => JetGraphicsPlannerReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<JetGraphicsPlannerFile>.VideoModes => [
    new("Character set", [(Width, Height)], [4])
  ];

  /// <summary>The glyph data, eight bytes a glyph.</summary>
  public byte[] GlyphData { get; init; }

  /// <summary>
  /// Where a band's glyphs begin within the set.
  /// </summary>
  /// <remarks>
  /// The bands are not in order. Even bands come from the first kilobyte and odd ones from the
  /// second, and within each the band number picks a 256-byte block — so reading the sheet top to
  /// bottom walks the set in a stride that only makes sense once you know the halves are separate.
  /// </remarks>
  public static int BandOffset(int band) => ((band & 6) << 7) + ((band & 1) << 10);

  public static RawImage ToRawImage(JetGraphicsPlannerFile file) {
    var font = file.GlyphData ?? [];
    var gtia = Atari8BitGraphics.Palette;
    var pixels = new byte[Width * Height * 3];

    for (var y = 0; y < Height; ++y) {
      var band = y / GlyphHeight;
      var origin = BandOffset(band);

      for (var x = 0; x < Width; ++x) {
        var character = x >> 3;
        var index = origin + ((character & 127) << 3) + (y % GlyphHeight);
        var row = index < font.Length ? font[index] : 0;
        var value = (row >> (~x & 6)) & 3;

        // The fourth value swaps registers for the upper half of the set.
        var color = value == 3 && character >= 128 ? InverseRegister : Registers[value];
        var entry = color * 3;
        var target = (y * Width + x) * 3;
        pixels[target] = gtia[entry];
        pixels[target + 1] = gtia[entry + 1];
        pixels[target + 2] = gtia[entry + 2];
      }
    }

    return new() { Width = Width, Height = Height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }
}
