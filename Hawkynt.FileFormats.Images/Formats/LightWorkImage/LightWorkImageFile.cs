using System;
using FileFormat.Core;

namespace FileFormat.LightWorkImage;

/// <summary>In-memory representation of a LightWork Design texture (.lwi).</summary>
/// <remarks>
/// A record stream, not a header and a blob. Every record is a tag byte and a payload whose shape the
/// tag decides: the string tags carry a length byte and that many characters, and the numeric ones a
/// fixed count of 32-bit big-endian words. A file opens with the copyright string, names the program
/// that made it, who ran it, what it read and when, then states the picture — one word that says a
/// picture follows, two that give its width and height, three that give the origin and the row length
/// — and the pixels come straight after the last of those.
/// <para/>
/// The pixels are runs of four bytes: a count of one to 255 and the red, green and blue it repeats.
/// There is no compression beyond that and no palette, so what comes out is 24-bit colour exactly as
/// stored.
/// <para/>
/// The reading is settled by arithmetic rather than by eye. Across the fourteen files there are to go
/// by the run counts add up to the stated width times the stated height <em>exactly</em> — 65536 for
/// the 256×256 ones, 262144 for the 512×512 one, 422500 for the 650×650 one — and the bytes left over
/// after the last run are a well-formed tail of the same record stream ending at the last byte of the
/// file. A layout that were wrong by a byte anywhere would miss on all three counts.
/// <para/>
/// Rows are taken as running top-down, and that one part is not proved by the files. Every sample is a
/// seamless texture — chipboard, denim, limestone, wrought iron — and a texture looks like itself
/// either way up, so nothing in them settles it. What there is: the record naming the program that
/// wrote each one says <c>ppmtolwi</c> or <c>tiff2lwi</c>, and both of those read a top-down source, so
/// a converter storing them the other way round would be flipping rows for no reason; and the window
/// record gives an origin of (0, 0), which is a top-left corner. Should a picture with an up and a
/// down ever turn up, that is the thing to check first.
/// </remarks>
[FormatMagicBytes([
  (byte)'C', (byte)'o', (byte)'p', (byte)'y', (byte)'r', (byte)'i', (byte)'g', (byte)'h', (byte)'t',
  (byte)'_', (byte)'l', (byte)'i', (byte)'g', (byte)'h', (byte)'t', (byte)'W', (byte)'o', (byte)'r',
  (byte)'k', (byte)'_', (byte)'D', (byte)'e', (byte)'s', (byte)'i', (byte)'g', (byte)'n'
], 2)]
public readonly record struct LightWorkImageFile
  : IImageFormatReader<LightWorkImageFile>, IImageToRawImage<LightWorkImageFile>,
    IImageFromRawImage<LightWorkImageFile>, IImageFormatWriter<LightWorkImageFile> {

  /// <summary>The string every one of these opens with, without its terminator.</summary>
  public const string Copyright = "Copyright_lightWork_Design_Limited:LightWorkImage";

  /// <summary>Tags for the records this reads and writes.</summary>
  public const byte TagSize = 0x01, TagWindow = 0x11, TagPicture = 0x13,
    TagCreator = 0x15, TagSource = 0x16, TagAuthor = 0x17, TagCopyright = 0x18, TagDate = 0x19;

  /// <summary>Bigger than any of these and it keeps a false match cheap.</summary>
  public const int MaxDimension = 32768;

  static string IImageFormatMetadata<LightWorkImageFile>.PrimaryExtension => ".lwi";
  static string[] IImageFormatMetadata<LightWorkImageFile>.FileExtensions => [".lwi"];
  static LightWorkImageFile IImageFormatReader<LightWorkImageFile>.FromSpan(ReadOnlySpan<byte> data)
    => LightWorkImageReader.FromSpan(data);
  static byte[] IImageFormatWriter<LightWorkImageFile>.ToBytes(LightWorkImageFile file)
    => LightWorkImageWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<LightWorkImageFile>.VideoModes => [
    new("Texture", [(new IntegerRange(1, MaxDimension), new IntegerRange(1, MaxDimension))], [16777216])
  ];

  /// <summary>The picture's width in pixels, as the size record states it.</summary>
  public int Width { get; init; }

  /// <summary>The picture's height in pixels, as the size record states it.</summary>
  public int Height { get; init; }

  /// <summary>The pixels, three bytes each, rows top-down.</summary>
  public byte[] Pixels { get; init; }

  /// <summary>The program that wrote the file, e.g. <c>ppmtolwi</c>. Empty when the file names none.</summary>
  public string Creator { get; init; }

  /// <summary>Who ran that program.</summary>
  public string Author { get; init; }

  /// <summary>What it read, which for the converted ones is a file name or <c>stdin</c>.</summary>
  public string Source { get; init; }

  /// <summary>When it ran, in the form the files use, e.g. <c>Fri_Sep_18_12:29:22_1992</c>.</summary>
  public string Date { get; init; }

  public static RawImage ToRawImage(LightWorkImageFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Rgb24,
    PixelData = file.Pixels ?? [],
  };

  public static LightWorkImageFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1)
      throw new ArgumentException("A LightWork image needs a width and a height.", nameof(image));

    var rgb = image.EnsureFormat(PixelFormat.Rgb24);

    return new() {
      Width = rgb.Width,
      Height = rgb.Height,
      Pixels = rgb.PixelData,
      Creator = string.Empty,
      Author = string.Empty,
      Source = string.Empty,
      Date = string.Empty,
    };
  }
}
