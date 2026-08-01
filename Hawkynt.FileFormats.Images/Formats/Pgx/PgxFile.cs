using System;
using FileFormat.Core;

namespace FileFormat.Pgx;

/// <summary>In-memory representation of a PGX image (.pgx), the JPEG 2000 conformance format.</summary>
/// <remarks>
/// One component, stored raw behind a single line of text: the byte order, whether samples are
/// signed, how many bits each takes, and the size. It exists so that a codec's output can be
/// compared against something with no coding of its own, which is why it carries no palette, no
/// compression and no second component.
/// <para/>
/// Samples wider than eight bits take two bytes each in the order the header names. Signed samples
/// are stored biased so that the darkest is the smallest, which is what makes them displayable
/// without knowing the sign in advance.
/// </remarks>
public readonly record struct PgxFile
  : IImageFormatReader<PgxFile>, IImageToRawImage<PgxFile>,
    IImageFromRawImage<PgxFile>, IImageFormatWriter<PgxFile> {

  /// <summary>The two characters every file starts with.</summary>
  public const string Signature = "PG";

  static string IImageFormatMetadata<PgxFile>.PrimaryExtension => ".pgx";
  static string[] IImageFormatMetadata<PgxFile>.FileExtensions => [".pgx"];
  static PgxFile IImageFormatReader<PgxFile>.FromSpan(ReadOnlySpan<byte> data) => PgxReader.FromSpan(data);
  static byte[] IImageFormatWriter<PgxFile>.ToBytes(PgxFile file) => PgxWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<PgxFile>.VideoModes => [
    new("PGX", [(IntegerRange.Any, IntegerRange.Any)], [256])
  ];

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>Bits each sample takes.</summary>
  public int Depth { get; init; }

  /// <summary>Whether samples are signed.</summary>
  public bool IsSigned { get; init; }

  /// <summary>Whether a wide sample's high byte comes first.</summary>
  public bool IsBigEndian { get; init; }

  /// <summary>One sample a pixel, already widened to eight bits.</summary>
  public byte[] Samples { get; init; }

  public static RawImage ToRawImage(PgxFile file) {
    var samples = file.Samples ?? [];
    var rgb = new byte[file.Width * file.Height * 3];

    for (var i = 0; i < file.Width * file.Height; ++i) {
      var level = i < samples.Length ? samples[i] : (byte)0;
      rgb[i * 3] = rgb[i * 3 + 1] = rgb[i * 3 + 2] = level;
    }

    return new() { Width = file.Width, Height = file.Height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  /// <summary>Builds a one-component picture, which is to say a grey one.</summary>
  /// <remarks>
  /// The format carries a single component and no palette, so colour cannot survive it; what is
  /// written is the luminance. Eight bits and unsigned, because a deeper or signed file describes
  /// nothing more when the source was eight bits of grey to begin with.
  /// </remarks>
  public static PgxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1)
      throw new ArgumentException("A picture needs at least one pixel.", nameof(image));

    var rgb = PixelConverter.Convert(image, PixelFormat.Rgb24);
    var samples = new byte[image.Width * image.Height];

    for (var i = 0; i < samples.Length; ++i) {
      var at = i * 3;
      var luminance = rgb.PixelData[at] * 77 + rgb.PixelData[at + 1] * 150 + rgb.PixelData[at + 2] * 29;
      samples[i] = (byte)(luminance >> 8);
    }

    return new() {
      Width = image.Width,
      Height = image.Height,
      Depth = 8,
      IsSigned = false,
      IsBigEndian = true,
      Samples = samples,
    };
  }
}
