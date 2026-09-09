using System;
using FileFormat.Core;

namespace FileFormat.HighresMedium;

/// <summary>In-memory representation of a HighresMedium picture for the Atari ST.</summary>
/// <remarks>
/// Two bitplanes at 640 by 400 — the ST's medium resolution run at the high resolution's line count
/// — with the palette reloaded eight times across every one of those lines. That is what the file
/// is mostly made of: 64000 bytes of screen and then 400 palettes of 35 colours apiece.
/// <para/>
/// Which of the 35 a pixel takes is not simply its plane value. The raster splits fall at 80-pixel
/// intervals, but a colour register is loaded a little before the pixel that first uses it and each
/// of the four is loaded at a different point in the line, so the zone a pixel belongs to is worked
/// out from its own register's lead — 80, 72, 40 and 32 pixels for registers 0 to 3. Reading the
/// zone off the pixel alone puts a stripe of the wrong colour down the left of every split.
/// <para/>
/// The two frames the mode names are the two television fields, and what reaches the eye is their
/// average: rows are shown in pairs and the pair is one row of picture.
/// <para/>
/// What was written before was 64064 bytes at 640 by 200: two whole screens each with a sixteen-word
/// palette of its own, in the plain three-bit ST form. That is not this format's length, geometry,
/// palette layout or colour depth — the picture is an STE's four bits a channel.
/// </remarks>
[VerifiedBy(ConformanceOracle.Recoil2Png)]
public readonly record struct HighresMediumFile
  : IImageFormatReader<HighresMediumFile>, IImageToRawImage<HighresMediumFile>,
    IImageFromRawImage<HighresMediumFile>, IImageFormatWriter<HighresMediumFile> {

  static string IImageFormatMetadata<HighresMediumFile>.PrimaryExtension => ".hrm";
  static string[] IImageFormatMetadata<HighresMediumFile>.FileExtensions => [".hrm"];
  static HighresMediumFile IImageFormatReader<HighresMediumFile>.FromSpan(ReadOnlySpan<byte> data) => HighresMediumReader.FromSpan(data);
  static byte[] IImageFormatWriter<HighresMediumFile>.ToBytes(HighresMediumFile file) => HighresMediumWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<HighresMediumFile>.VideoModes => [
    new("HighresMedium", [(ImageWidth, ImageHeight)], [ColorCount])
  ];

  /// <summary>Image width, always 640.</summary>
  public const int ImageWidth = 640;

  /// <summary>Image height, always 400: the high resolution's line count.</summary>
  public const int ImageHeight = 400;

  /// <summary>Bitplanes, and so two bits a pixel.</summary>
  public const int NumPlanes = 2;

  /// <summary>Colours a pixel can name at any one point across the line.</summary>
  public const int ColorCount = 4;

  /// <summary>Bytes a row of screen takes.</summary>
  internal const int BytesPerRow = ImageWidth * NumPlanes / 8;

  /// <summary>Bytes the screen takes.</summary>
  internal const int BitmapSize = BytesPerRow * ImageHeight;

  /// <summary>Where the per-row palettes start.</summary>
  internal const int PalettesOffset = BitmapSize;

  /// <summary>Colours one row's palette holds, which the raster splits share out across it.</summary>
  internal const int PaletteEntryCount = 35;

  /// <summary>Bytes one row's palette takes.</summary>
  internal const int PaletteRowSize = PaletteEntryCount * 2;

  /// <summary>The length of a whole picture, which is also what identifies it.</summary>
  public const int FileSize = PalettesOffset + PaletteRowSize * ImageHeight;

  /// <summary>Pixels across between one raster split and the next.</summary>
  internal const int ZoneWidth = 80;

  /// <summary>
  /// How far ahead of the pixel that first uses it each of the four registers is loaded.
  /// </summary>
  /// <remarks>
  /// The four are written one after another and the raster has moved on between them, so they do not
  /// change over at the same column. These are the leads the reference decoder uses and they are
  /// what the display does, not a choice.
  /// </remarks>
  internal static ReadOnlySpan<int> ZoneLeads => [80, 72, 40, 32];

  /// <summary>Image width, always 640.</summary>
  public int Width => ImageWidth;

  /// <summary>Image height, always 400.</summary>
  public int Height => ImageHeight;

  /// <summary>The screen, two bitplanes interleaved every sixteen pixels.</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>The palettes, 35 big-endian STE colour words for each of the 400 rows.</summary>
  public byte[] Palettes { get; init; }

  /// <summary>Converts this picture to a platform-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(HighresMediumFile file) {
    var chunky = PlanarConverter.AtariStToChunky(file.BitmapData ?? [], ImageWidth, ImageHeight, NumPlanes);
    var palettes = file.Palettes ?? [];
    var rgb = new byte[ImageWidth * ImageHeight * 3];

    for (var y = 0; y < ImageHeight; ++y)
    for (var x = 0; x < ImageWidth; ++x) {
      var value = chunky[y * ImageWidth + x];
      var colour = AtariStGraphics.ColorAt(palettes, y * PaletteRowSize + EntryFor(x, value) * 2, ste: true);
      var at = (y * ImageWidth + x) * 3;
      rgb[at] = (byte)(colour >> 16);
      rgb[at + 1] = (byte)(colour >> 8);
      rgb[at + 2] = (byte)colour;
    }

    // A pair of rows is one television field each and one row of picture between them.
    for (var y = 1; y < ImageHeight; y += 2) {
      var above = (y - 1) * ImageWidth * 3;
      var here = y * ImageWidth * 3;
      for (var i = 0; i < ImageWidth * 3; ++i) {
        var mixed = (byte)((rgb[above + i] & rgb[here + i]) + (((rgb[above + i] ^ rgb[here + i]) >> 1) & 0x7F));
        rgb[above + i] = mixed;
        rgb[here + i] = mixed;
      }
    }

    return new() {
      Width = ImageWidth,
      Height = ImageHeight,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Which of a row's 35 colours a pixel of a given plane value takes.</summary>
  internal static int EntryFor(int x, int value)
    => value + (((x + ZoneLeads[value]) / ZoneWidth) << 2) - 1;

  /// <summary>Encodes a picture as a HighresMedium screen, scaling it to 640x400 first.</summary>
  /// <remarks>
  /// One set of four colours for the whole picture, laid into every row's 35 entries in the order
  /// the zones ask for them, so that a plane value means the same colour wherever it appears.
  /// Choosing a different four for every zone of every row is what the format is for and is a
  /// problem of its own; what this writes is a correct file that happens not to change colour across
  /// the line.
  /// </remarks>
  public static HighresMediumFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(ImageWidth, ImageHeight).PixelData;
    var reduced = ColorQuantizer.Quantize(
      PixelConverter.Convert(
        new RawImage { Width = ImageWidth, Height = ImageHeight, Format = PixelFormat.Rgb24, PixelData = rgb },
        PixelFormat.Bgra32).PixelData,
      ImageWidth * ImageHeight, ColorCount);

    // Through the hardware and back, so what is encoded against is what the picture will show.
    var palette = new byte[ColorCount * 3];
    for (var i = 0; i < ColorCount; ++i) {
      var colour = _Quantise(reduced.Palette, i);
      palette[i * 3] = (byte)(colour >> 16);
      palette[i * 3 + 1] = (byte)(colour >> 8);
      palette[i * 3 + 2] = (byte)colour;
    }

    var chunky = new byte[ImageWidth * ImageHeight];
    for (var i = 0; i < chunky.Length; ++i)
      chunky[i] = _Nearest(palette, rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]);

    var palettes = new byte[PaletteRowSize * ImageHeight];
    for (var entry = 0; entry < PaletteEntryCount; ++entry) {
      // Register n is asked for at entries congruent to n - 1, which is what makes one set of four
      // serve every zone.
      var word = _ToSteWord(palette, (entry + 1) % ColorCount);
      for (var y = 0; y < ImageHeight; ++y) {
        palettes[y * PaletteRowSize + entry * 2] = (byte)(word >> 8);
        palettes[y * PaletteRowSize + entry * 2 + 1] = (byte)word;
      }
    }

    return new() {
      BitmapData = PlanarConverter.ChunkyToAtariSt(chunky, ImageWidth, ImageHeight, NumPlanes),
      Palettes = palettes,
    };
  }

  /// <summary>One quantised colour as the STE can actually show it.</summary>
  private static int _Quantise(ReadOnlySpan<byte> palette, int index) {
    var entry = index * 3;
    var red = entry < palette.Length ? _Channel(palette[entry]) : 0;
    var green = entry + 1 < palette.Length ? _Channel(palette[entry + 1]) : 0;
    var blue = entry + 2 < palette.Length ? _Channel(palette[entry + 2]) : 0;

    return ChannelScaling.Expand4(red) << 16 | ChannelScaling.Expand4(green) << 8 | ChannelScaling.Expand4(blue);
  }

  /// <summary>One colour as the big-endian word an STE register holds, low bit stored highest.</summary>
  private static int _ToSteWord(ReadOnlySpan<byte> palette, int index) {
    var entry = index * 3;
    int red = _Channel(palette[entry]), green = _Channel(palette[entry + 1]), blue = _Channel(palette[entry + 2]);

    return _Nibble(red) << 8 | _Nibble(green) << 4 | _Nibble(blue);
  }

  /// <summary>An eight-bit channel as the four bits a register holds.</summary>
  private static int _Channel(byte value) => (value * 15 + 127) / 255;

  /// <summary>A four-bit channel in the order the register stores it: the low bit above the rest.</summary>
  private static int _Nibble(int value) => (value >> 1 & 7) | (value & 1) << 3;

  /// <summary>Which of the four colours is nearest a given one.</summary>
  private static byte _Nearest(ReadOnlySpan<byte> palette, byte red, byte green, byte blue) {
    byte best = 0;
    var bestCost = int.MaxValue;

    for (var i = 0; i < ColorCount; ++i) {
      int dr = palette[i * 3] - red, dg = palette[i * 3 + 1] - green, db = palette[i * 3 + 2] - blue;
      var cost = dr * dr + dg * dg + db * db;
      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = (byte)i;
    }

    return best;
  }
}
