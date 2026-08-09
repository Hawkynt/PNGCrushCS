using System;
using FileFormat.Core;

namespace FileFormat.Arn;

/// <summary>An Astronomical Research Network picture (.arn): a PDS-style keyword label and a palette in front of the rows.</summary>
/// <remarks>
/// The label is the plain-text keyword form the planetary community uses, and XnView's reader looks at
/// six keywords out of it: <c>SIMPLE</c>, whose value has to begin with the eighteen characters
/// <c>T  / ARN PROVISION</c> or the file is refused; <c>RECORD_BYTES</c> and <c>LABEL_RECORDS</c>,
/// whose product is where the label ends; and <c>LINES</c>, <c>LINE_SAMPLES</c> and <c>SAMPLE_BITS</c>,
/// which are only taken while an <c>OBJECT = IMAGE</c> is open. Anything but eight bits a sample is
/// refused outright with the converter's own words, "ARN: Bad BitsPerSample".
/// <para/>
/// The picture does not begin where the label ends, which is the part that cannot be guessed. XnView
/// seeks to the end of the label, skips 1024 bytes rounded up to a whole number of records, then reads
/// three colour tables of 256 bytes — red, then green, then blue — each padded to 256 bytes rounded up
/// to a whole number of records, and only then reads the rows, one byte a pixel, as wide as
/// <c>LINE_SAMPLES</c>. Files written with 256-, 512- and 1024-byte records all came back with the same
/// pixels, which is what fixes both roundings.
/// </remarks>
[FormatDetectionPriority(50)]
public readonly record struct ArnFile
  : IImageFormatReader<ArnFile>, IImageToRawImage<ArnFile> {

  /// <summary>The keyword that has to carry the format's own value.</summary>
  public const string SimpleKeyword = "SIMPLE";

  /// <summary>What the value of <see cref="SimpleKeyword"/> has to begin with.</summary>
  public const string SimpleValuePrefix = "T  / ARN PROVISION";

  /// <summary>The only sample size that is read.</summary>
  public const int SupportedSampleBits = 8;

  /// <summary>How many bytes are skipped between the label and the colour tables, before rounding to whole records.</summary>
  public const int GapBeforePalette = 1024;

  /// <summary>How many entries each colour table has.</summary>
  public const int PaletteEntries = 256;

  static string IImageFormatMetadata<ArnFile>.PrimaryExtension => ".arn";
  static string[] IImageFormatMetadata<ArnFile>.FileExtensions => [".arn"];
  static ArnFile IImageFormatReader<ArnFile>.FromSpan(ReadOnlySpan<byte> data) => ArnReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<ArnFile>.VideoModes => [
    new("Astronomical Research Network", [(IntegerRange.Any, IntegerRange.Any)], [PaletteEntries])
  ];

  /// <summary>FITS files also open with <c>SIMPLE</c>, so this insists on the value as well.</summary>
  static bool? IImageFormatMetadata<ArnFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => ArnReader.HasArnSimpleLine(header) ? true : null;

  /// <summary>Pixels across, from <c>LINE_SAMPLES</c>.</summary>
  public int Width { get; init; }

  /// <summary>Rows, from <c>LINES</c>.</summary>
  public int Height { get; init; }

  /// <summary>How long one record is, from <c>RECORD_BYTES</c>.</summary>
  public int RecordBytes { get; init; }

  /// <summary>How many records the label takes, from <c>LABEL_RECORDS</c>.</summary>
  public int LabelRecords { get; init; }

  /// <summary>The 256 colours as red, green and blue triplets.</summary>
  public byte[] Palette { get; init; }

  /// <summary>The rows, one byte a pixel.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(ArnFile file) {
    if (file.PixelData == null || file.Palette == null)
      throw new InvalidOperationException("No Astronomical Research Network picture was read.");

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = file.Palette[..],
      PaletteCount = PaletteEntries,
    };
  }
}
