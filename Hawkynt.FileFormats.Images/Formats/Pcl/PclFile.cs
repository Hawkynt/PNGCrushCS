using System;
using FileFormat.Core;

namespace FileFormat.Pcl;

/// <summary>A Hewlett-Packard Printer Command Language print job (.pcl, .prn).</summary>
/// <remarks>
/// Built from HP's <em>PCL 5 Printer Language Technical Reference Manual</em> (part number
/// 5961-0509, first edition October 1992), chapter 15, <em>Raster Graphics</em>, and from the
/// <em>PCL Implementor's Guide</em> version 6.0 chapter 13, which is where compression methods 4, 5
/// and 9 and the scaled forms of <c>ESC*r#A</c> are defined.
/// <para/>
/// What a print job is, is a stream of bytes with escape sequences in it. A parameterised sequence
/// is <c>ESC</c>, a character between <c>!</c> and <c>/</c>, a group character, a number, and a
/// terminator: an upper-case terminator ends the sequence and a lower-case one carries the same two
/// characters on to another command. The raster part of that stream is self-contained —
/// <c>ESC*r#A</c> starts a picture, <c>ESC*b#W</c> hands over one row of it and the number is how
/// many bytes follow, <c>ESC*b#M</c> says how those bytes are packed, and <c>ESC*rB</c> or
/// <c>ESC*rC</c> ends it.
/// <para/>
/// Only the raster part is read. A job also carries text, fonts, rules and, in PCL 5, vector
/// drawings written in HP-GL/2, and none of those is a picture the file holds: the text needs the
/// printer's fonts and the drawing is a language of its own. A job that prints only text therefore
/// gives nothing rather than a wrong page.
/// <para/>
/// The packings read are the ones the manual defines completely: 0 unencoded, 1 run-length pairs
/// whose count is repetitions less one, 2 the TIFF rule, 3 delta row against the row before, and 5
/// adaptive, which is a block of rows each carrying its own method. Methods 4, 6, 7, 8 and 9 —
/// block-unencoded, the three CCITT ones and the compressed-replacement delta — are refused by name.
/// So is <c>ESC*v#W</c>, which configures a palette out of commands this does not read; guessing at
/// the colours would be worse than saying so.
/// <para/>
/// Colour comes from <c>ESC*r#U</c> alone: 1 is the black and white the printer starts in, 3 is the
/// eight-entry device RGB palette and -3 the device CMY one, each sent as three planes a row. The
/// four-plane KCMY value -4 appears only in second-hand descriptions and not in HP's own colour
/// manual, so it is refused rather than assumed.
/// <para/>
/// A job may print several pages. What is read is the first picture in it: the raster that the
/// first <c>ESC*r#A</c> opens and that the end-of-raster or the next printer reset closes.
/// <para/>
/// No sample was available to check any of this against. The fixtures in the tests are jobs built
/// byte by byte from the manual's own tables.
/// <para/>
/// Writing emits the raster half and nothing else: a reset, the resolution, the colour mode, the
/// source size, the raster in TIFF packing and the end of it. Only the two colour modes the reader
/// reads are available, because they are the only two a job can state without <c>ESC*v#W</c>, so a
/// picture goes out either as black and white or on the eight-entry device palette — a grey takes the
/// former, since that palette holds no greys and reducing one onto it would tint it.
/// </remarks>
public readonly record struct PclFile
  : IImageFormatReader<PclFile>, IImageToRawImage<PclFile>,
    IImageFromRawImage<PclFile>, IImageFormatWriter<PclFile> {

  /// <summary>The escape every command opens with.</summary>
  public const byte Escape = 0x1B;

  /// <summary>Black and white, which is what a printer starts in and what <c>ESC*r1U</c> selects.</summary>
  internal static byte[] BilevelPalette => [255, 255, 255, 0, 0, 0];

  /// <summary>
  /// The eight-entry device RGB palette of <c>ESC*r3U</c>: black, red, green, yellow, blue, magenta,
  /// cyan, white. Plane one is the least significant bit of the index, which is what makes index one
  /// red.
  /// </summary>
  internal static byte[] DeviceRgbPalette => [
    0, 0, 0,
    255, 0, 0,
    0, 255, 0,
    255, 255, 0,
    0, 0, 255,
    255, 0, 255,
    0, 255, 255,
    255, 255, 255
  ];

  static string IImageFormatMetadata<PclFile>.PrimaryExtension => ".pcl";
  static string[] IImageFormatMetadata<PclFile>.FileExtensions => [".pcl", ".prn"];
  static PclFile IImageFormatReader<PclFile>.FromSpan(ReadOnlySpan<byte> data) => PclReader.FromSpan(data);
  static byte[] IImageFormatWriter<PclFile>.ToBytes(PclFile file) => PclWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<PclFile>.VideoModes => [
    new("Bilevel", [(IntegerRange.Any, IntegerRange.Any)], [2]),
    new("Simple colour", [(IntegerRange.Any, IntegerRange.Any)], [8])
  ];

  /// <summary>
  /// A print job starts by resetting the printer, and <c>ESC E</c> is how that is written.
  /// </summary>
  static bool? IImageFormatMetadata<PclFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 2 && header[0] == Escape && header[1] == (byte)'E' ? true : null;

  /// <summary>How wide the picture is, in pixels.</summary>
  public int Width { get; init; }

  /// <summary>How tall it is, in rows.</summary>
  public int Height { get; init; }

  /// <summary>How many bit planes a row was sent in.</summary>
  public int Planes { get; init; }

  /// <summary>One palette index a pixel.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>The palette as red, green and blue triplets.</summary>
  public byte[] Palette { get; init; }

  /// <summary>How many entries it holds.</summary>
  public int PaletteCount { get; init; }

  public static RawImage ToRawImage(PclFile file) {
    if (file.PixelData == null)
      throw new InvalidOperationException("No picture was read.");

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData,
      Palette = file.Palette,
      PaletteCount = file.PaletteCount
    };
  }

  /// <summary>Reduces the picture to one of the two palettes a job can state on its own.</summary>
  /// <remarks>
  /// A grey goes out black and white. The eight-entry device palette holds black, white and six
  /// saturated colours and no grey at all, so reducing a grey onto it would put red and cyan into a
  /// photograph that had none; two levels is what a printer does with one anyway.
  /// </remarks>
  public static PclFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var bilevel = image.Format is PixelFormat.Gray8 or PixelFormat.Indexed1
                  || image is { PaletteCount: > 0 and <= 2 };

    if (bilevel)
      return new() {
        Width = image.Width,
        Height = image.Height,
        Planes = 1,
        PixelData = BilevelRows.Threshold(image, setWhenDark: true),
        Palette = BilevelPalette,
        PaletteCount = 2
      };

    var palette = DeviceRgbPalette;
    var indexed = image.EnsureIndexed(PixelFormat.Indexed8, palette);

    return new() {
      Width = image.Width,
      Height = image.Height,
      Planes = 3,
      PixelData = indexed.PixelData,
      Palette = palette,
      PaletteCount = 8
    };
  }
}
