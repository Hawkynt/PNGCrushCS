using System;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Pixia;

/// <summary>In-memory representation of a Pixia picture (.pxa).</summary>
/// <remarks>
/// The layered document of Pixia, a Japanese paint program. A fixed header of 45796 bytes — a name,
/// a version, a layer count and three tables with room for 64 layers — then a preview JPEG whose
/// length the header states, then the layers themselves, run-length compressed.
/// <para/>
/// The preview is the trap. Nine of the ten samples carry exactly one JPEG and always at offset
/// 45800, which looks like the picture and is not: it is the canvas rescaled so the shorter side
/// becomes 256, and in four samples that makes it larger than the canvas — <c>grass</c> is 50 by 100
/// and its preview is 256 by 512. Nothing 5.12 times the size of the picture is the picture. The
/// tenth sample carries no JPEG at all and has complete layer data, which settles it the other way
/// round as well.
/// <para/>
/// What accounts for the file is the layers. Each is a 140-byte record stating how many 8-bit planes
/// follow, then the colours as 4-byte runs of <c>count, blue, green, red</c>, then that many planes
/// as 2-byte runs, each run list ending on a count of 255. Every plane decodes to exactly the
/// layer's width times its height plus one — the extra row is a copy of the last, appended at the
/// bottom — and the layers together run to the end of the file to the byte in all nine.
/// <para/>
/// One plane of each layer is opacity and the others are inverted masks, so the alpha of a pixel is
/// the opacity scaled by 255 less each mask. Which plane is the opacity follows the version: the
/// first from version 3, the second at version 2. Layers composite bottom to top at the offsets the
/// property table gives, and a layer whose visible flag is clear is left out — one sample has a
/// hidden layer that puts a second outfit on the character if it is drawn.
/// </remarks>
public readonly record struct PixiaFile
  : IImageFormatReader<PixiaFile>, IImageToRawImage<PixiaFile>,
    IImageFromRawImage<PixiaFile>, IImageFormatWriter<PixiaFile> {

  /// <summary>The name every one of these opens with.</summary>
  public const string Signature = "Pixia";

  /// <summary>Where the header states the version and how many layers there are.</summary>
  internal const int VersionAt = 0x50, LayerCountAt = 0xE0;

  /// <summary>Where the three tables begin, and how long each of their entries is.</summary>
  internal const int GeometryAt = 0xE4, GeometryEntrySize = 140;
  internal const int PropertiesAt = 19940, PropertyEntrySize = 404;

  /// <summary>Within a property entry: where the offset on the canvas and the visible flag stand.</summary>
  internal const int PropertyXAt = 0x100, PropertyYAt = 0x104, PropertyVisibleAt = 0x110;

  /// <summary>How many layers the tables have room for.</summary>
  public const int MaximumLayers = 64;

  /// <summary>The most pixels one layer may state, runs being able to claim more than they cost.</summary>
  internal const long LargestLayer = 1L << 28;

  /// <summary>The longest run a count expresses, which bounds what the bytes left can expand to.</summary>
  internal const int LongestRun = 200;

  /// <summary>The header, after which the length of the preview stands and then the preview.</summary>
  public const int HeaderSize = 45796;

  /// <summary>Where the preview begins, the header having stated its length in the four bytes before.</summary>
  public const int PreviewAt = HeaderSize + 4;

  /// <summary>The record ahead of each layer's runs, stating how many 8-bit planes follow.</summary>
  internal const int LayerRecordSize = 140;

  /// <summary>The count that ends a list of runs.</summary>
  internal const byte RunTerminator = 0xFF;

  /// <summary>The earliest version whose first plane is the opacity rather than its second.</summary>
  internal const int FirstVersionWithLeadingOpacity = 3;

  /// <summary>The version this writes, whose opacity plane is the first.</summary>
  internal const int WrittenVersion = 3;

  static string IImageFormatMetadata<PixiaFile>.PrimaryExtension => ".pxa";
  /// <summary>Also .pxs, which is the same file under the name XnView files Pegs pictures under.</summary>
  /// <remarks>
  /// The catalogue keeps two rows for these — one for Pixia on <c>.pxa</c> and one for Pegs on
  /// <c>.pxa</c> and <c>.pxs</c> — which reads like two formats sharing a name. It is one reader:
  /// a Pixia picture renamed <c>.pxs</c> and handed to the converter comes back at the same size,
  /// pixel for pixel, as the same file under <c>.pxa</c>. The signature is still what identifies it,
  /// so a file that is not a Pixia picture is refused under either name.
  /// </remarks>
  static string[] IImageFormatMetadata<PixiaFile>.FileExtensions => [".pxa", ".pxs"];
  static PixiaFile IImageFormatReader<PixiaFile>.FromSpan(ReadOnlySpan<byte> data) => PixiaReader.FromSpan(data);
  static byte[] IImageFormatWriter<PixiaFile>.ToBytes(PixiaFile file) => PixiaWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<PixiaFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>The name at the front, which nothing else opens with.</summary>
  static bool? IImageFormatMetadata<PixiaFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= Signature.Length
       && header[..Signature.Length].SequenceEqual(Encoding.ASCII.GetBytes(Signature))
      ? true
      : null;

  /// <summary>Pixels across, which is the first layer's width.</summary>
  public int Width { get; init; }

  /// <summary>Pixels down, which is the first layer's height.</summary>
  public int Height { get; init; }

  /// <summary>Which version of the layout the header states; the samples show 1, 2 and 3.</summary>
  public int Version { get; init; }

  /// <summary>How many layers the header states.</summary>
  public int LayerCount { get; init; }

  /// <summary>The preview the header points at, exactly as it stands in the file.</summary>
  public byte[]? Preview { get; init; }

  /// <summary>The layers composited bottom to top over white, packed three bytes to a pixel.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(PixiaFile file)
    => new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData ?? new byte[file.Width * file.Height * 3],
    };

  public static PixiaFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return new() {
      Width = image.Width,
      Height = image.Height,
      Version = WrittenVersion,
      LayerCount = 1,
      PixelData = image.ToRgb24(),
    };
  }
}
