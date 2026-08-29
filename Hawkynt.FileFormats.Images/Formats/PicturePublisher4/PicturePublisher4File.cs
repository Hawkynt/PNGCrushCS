using System;
using FileFormat.Core;
using FileFormat.Tiff;

namespace FileFormat.PicturePublisher4;

/// <summary>In-memory representation of a Micrografx Picture Publisher 4 document (.pp4).</summary>
/// <remarks>The verified readable form is a short wrapper whose pointer at 0x2A leads to one complete TIFF.</remarks>
public readonly record struct PicturePublisher4File
  : IImageFormatReader<PicturePublisher4File>, IImageToRawImage<PicturePublisher4File>, IImageFromRawImage<PicturePublisher4File>, IImageFormatWriter<PicturePublisher4File> {

  public static ReadOnlySpan<byte> Signature => "II"u8;
  public const int PointerOffset = 0x2A;
  public const int MinFileSize = PointerOffset + 4;

  static string IImageFormatMetadata<PicturePublisher4File>.PrimaryExtension => ".pp4";
  static string[] IImageFormatMetadata<PicturePublisher4File>.FileExtensions => [".pp4"];
  static PicturePublisher4File IImageFormatReader<PicturePublisher4File>.FromSpan(ReadOnlySpan<byte> data)
    => PicturePublisher4Reader.FromSpan(data);
  static byte[] IImageFormatWriter<PicturePublisher4File>.ToBytes(PicturePublisher4File file)
    => PicturePublisher4Writer.ToBytes(file);

  static VideoMode[] IImageFormatMetadata<PicturePublisher4File>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  static bool? IImageFormatMetadata<PicturePublisher4File>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < Signature.Length)
      return null;
    return header[..Signature.Length].SequenceEqual(Signature) ? null : false;
  }

  public int Width { get; init; }
  public int Height { get; init; }
  public int PictureOffset { get; init; }
  public byte[] Embedded { get; init; }

  public static RawImage ToRawImage(PicturePublisher4File file) => PicturePublisher4Reader.Decode(file);

  /// <summary>Creates a minimal PP4 document carrying one standards-valid TIFF.</summary>
  public static PicturePublisher4File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    return new() {
      Width = image.Width,
      Height = image.Height,
      PictureOffset = MinFileSize,
      Embedded = TiffWriter.ToBytes(TiffFile.FromRawImage(image)),
    };
  }
}
