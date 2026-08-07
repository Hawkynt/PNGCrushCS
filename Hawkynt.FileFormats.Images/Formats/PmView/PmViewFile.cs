using System;
using FileFormat.Core;

namespace FileFormat.PmView;

/// <summary>In-memory representation of a PM picture (.pm), the one that opens with "VIEW".</summary>
/// <remarks>
/// A 28-byte header of big-endian integers, then the picture stored plane by plane, then whatever
/// comment the header said it carries. The bands are one whole plane after another rather than
/// interleaved — three of them for colour, one for grey.
/// <para/>
/// <c>.pm</c> was claimed only by Print Master, which is a different format under the same name and
/// read this one's header as a picture 150192 by 22341.
/// </remarks>
public readonly record struct PmViewFile
  : IImageFormatReader<PmViewFile>, IImageToRawImage<PmViewFile>,
    IImageFromRawImage<PmViewFile>, IImageFormatWriter<PmViewFile> {

  /// <summary>The four bytes every one of these opens with.</summary>
  public static ReadOnlySpan<byte> Magic => [(byte)'V', (byte)'I', (byte)'E', (byte)'W'];

  /// <summary>Bytes of header: the magic and six big-endian integers.</summary>
  public const int HeaderSize = 28;

  /// <summary>The storage code for one byte a band, which is the only one read here.</summary>
  public const int UnsignedByteForm = 0x8001;

  static string IImageFormatMetadata<PmViewFile>.PrimaryExtension => ".pm";
  static string[] IImageFormatMetadata<PmViewFile>.FileExtensions => [".pm"];
  static PmViewFile IImageFormatReader<PmViewFile>.FromSpan(ReadOnlySpan<byte> data) => PmViewReader.FromSpan(data);
  static byte[] IImageFormatWriter<PmViewFile>.ToBytes(PmViewFile file) => PmViewWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<PmViewFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)])
  ];

  public int Width { get; init; }

  public int Height { get; init; }

  /// <summary>How many bands the picture has: three for colour, one for grey.</summary>
  public int Bands { get; init; }

  /// <summary>The picture, already interleaved out of its planes.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>The comment trailing the picture, kept so writing it back preserves it.</summary>
  public byte[] Comment { get; init; }

  public static RawImage ToRawImage(PmViewFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = file.Bands == 1 ? PixelFormat.Gray8 : PixelFormat.Rgb24,
    PixelData = (file.PixelData ?? [])[..],
  };

  public static PmViewFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureAnyFormat(PixelFormat.Rgb24, PixelFormat.Gray8);

    return new() {
      Width = image.Width,
      Height = image.Height,
      Bands = image.Format == PixelFormat.Gray8 ? 1 : 3,
      PixelData = image.PixelData[..],
      Comment = [],
    };
  }
}
