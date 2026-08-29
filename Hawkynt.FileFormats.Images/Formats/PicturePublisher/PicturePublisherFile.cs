using System;
using FileFormat.Core;

namespace FileFormat.PicturePublisher;

/// <summary>In-memory representation of a Micrografx Picture Publisher 5 document.</summary>
/// <remarks>
/// Reading composites the document's object stack. Writing uses the smallest faithful subset: one
/// opaque full-canvas RGB object in the format's verified zlib-compressed TIFF-like raster record.
/// </remarks>
public readonly record struct PicturePublisherFile
  : IImageFormatReader<PicturePublisherFile>, IImageToRawImage<PicturePublisherFile>, IImageFromRawImage<PicturePublisherFile>, IImageFormatWriter<PicturePublisherFile> {

  internal static ReadOnlySpan<byte> Signature => "PPUBII"u8;
  internal const int HeaderSize = 48;

  static string IImageFormatMetadata<PicturePublisherFile>.PrimaryExtension => ".pp5";
  static string[] IImageFormatMetadata<PicturePublisherFile>.FileExtensions => [".pp5"];

  static bool? IImageFormatMetadata<PicturePublisherFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= Signature.Length && header[..Signature.Length].SequenceEqual(Signature) ? true : false;

  static PicturePublisherFile IImageFormatReader<PicturePublisherFile>.FromSpan(ReadOnlySpan<byte> data)
    => PicturePublisherReader.FromSpan(data);
  static byte[] IImageFormatWriter<PicturePublisherFile>.ToBytes(PicturePublisherFile file)
    => PicturePublisherWriter.ToBytes(file);

  static VideoMode[] IImageFormatMetadata<PicturePublisherFile>.VideoModes
    => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])];

  public int Width { get; init; }
  public int Height { get; init; }
  public int Resolution { get; init; }
  public int ObjectCount { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(PicturePublisherFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Rgb24,
    PixelData = file.PixelData ?? [],
  };

  public static PicturePublisherFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var rgb = image.EnsureFormat(PixelFormat.Rgb24);
    return new() {
      Width = rgb.Width,
      Height = rgb.Height,
      Resolution = 96,
      ObjectCount = 1,
      PixelData = rgb.PixelData[..],
    };
  }
}
