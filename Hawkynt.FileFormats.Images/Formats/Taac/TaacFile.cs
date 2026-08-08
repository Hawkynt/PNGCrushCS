using System;
using FileFormat.Core;

namespace FileFormat.Taac;

/// <summary>A Sun TAAC bitmap, also called a Visualization File Format image (.vff, .taac, .suniff).</summary>
/// <remarks>
/// The picture format of the Trancept Application Accelerator, the graphics board Sun sold for its
/// workstations from 1987. No specification of it was published; what it is built from is
/// xloadimage's <c>vff.c</c> (K. Prince, Southwest Research Institute, 1991), which is the oldest
/// reader of it that states the rules, together with the description on the file-format wiki, and it
/// was then checked against a sample.
/// <para/>
/// A file opens with the four letters <c>ncaa</c> and then a plain-text header of
/// <c>name=value;</c> lines. The header ends at a form feed, and the bytes after it are the picture,
/// uncompressed and one row after another. <c>rank</c> says how many dimensions the data has and
/// <c>size</c> gives that many extents, <c>bands</c> how many samples a pixel has, <c>bits</c> how
/// wide a sample is, and <c>colormap</c> a list of entries for a picture whose single band is an
/// index rather than a grey.
/// <para/>
/// A colour map entry is six hexadecimal digits, and they are blue, green and red in that order
/// rather than the other way about. That is what xloadimage reads them as, and the sample here
/// settles it: taken the other way round the skin in the photograph comes out blue.
/// <para/>
/// What is read is the two-dimensional raster of eight-bit samples the board itself displayed:
/// <c>rank=2</c>, <c>bits=8</c>, and one band or three. A file that says anything else — a volume,
/// wider samples, the two-band case nobody defines — is refused by name rather than read as though
/// it were one of these, and the file has to carry as many bytes as the size it states.
/// <para/>
/// Only the single-band case could be checked against a sample. Three-band files are read blue,
/// green, red per pixel, which is the order xloadimage reads them in and the order the colour map in
/// the same file uses, but nothing here has confirmed it against a picture.
/// <para/>
/// It does not write.
/// </remarks>
[FormatMagicBytes([0x6E, 0x63, 0x61, 0x61])]
public readonly record struct TaacFile : IImageFormatReader<TaacFile>, IImageToRawImage<TaacFile> {

  /// <summary>The four letters a file opens with.</summary>
  public const string Magic = "ncaa";

  /// <summary>The byte that ends the header, after which the picture starts.</summary>
  public const byte HeaderTerminator = 0x0C;

  static string IImageFormatMetadata<TaacFile>.PrimaryExtension => ".vff";
  static string[] IImageFormatMetadata<TaacFile>.FileExtensions => [".vff", ".taac", ".suniff"];
  static TaacFile IImageFormatReader<TaacFile>.FromSpan(ReadOnlySpan<byte> data) => TaacReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<TaacFile>.VideoModes => [
    new("Indexed", [(IntegerRange.Any, IntegerRange.Any)], [256]),
    new("Colour", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>How wide the picture is, from the first number of <c>size</c>.</summary>
  public int Width { get; init; }

  /// <summary>How tall it is, from the second.</summary>
  public int Height { get; init; }

  /// <summary>How many samples a pixel has, from <c>bands</c>.</summary>
  public int Bands { get; init; }

  /// <summary>The samples, one row after another with no padding.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>The colour map as red, green and blue triplets, or nothing when there is none.</summary>
  public byte[]? Palette { get; init; }

  /// <summary>How many entries that map holds.</summary>
  public int PaletteCount { get; init; }

  public static RawImage ToRawImage(TaacFile file) {
    if (file.PixelData == null)
      throw new InvalidOperationException("No picture was read.");

    if (file.Bands == 1 && file.Palette != null)
      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Indexed8,
        PixelData = file.PixelData,
        Palette = file.Palette,
        PaletteCount = file.PaletteCount
      };

    // A single band with no map is a grey, which is what the board showed for one; xloadimage
    // builds the same ramp when the header carries no colours.
    if (file.Bands == 1)
      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Gray8,
        PixelData = file.PixelData
      };

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Bgr24,
      PixelData = file.PixelData
    };
  }
}
