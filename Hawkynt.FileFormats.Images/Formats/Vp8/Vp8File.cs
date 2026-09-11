using System;
using FileFormat.Core;
using FileFormat.WebP.Vp8;

namespace FileFormat.Vp8;

/// <summary>A single VP8 key frame stored as a raw elementary bitstream.</summary>
/// <remarks>
/// VP8 itself carries no alpha plane or metadata. The WebP container can add those around a VP8
/// payload, but a bare <c>.vp8</c> file is just the payload, so converting an image to this format
/// deliberately discards alpha and encodes RGB at the codec's default quality.
/// </remarks>
public readonly record struct Vp8File :
  IImageFormatReader<Vp8File>, IImageToRawImage<Vp8File>, IImageFromRawImage<Vp8File>, IImageFormatWriter<Vp8File> {

  static string IImageFormatMetadata<Vp8File>.PrimaryExtension => ".vp8";
  static string[] IImageFormatMetadata<Vp8File>.FileExtensions => [".vp8"];
  static Vp8File IImageFormatReader<Vp8File>.FromSpan(ReadOnlySpan<byte> data) => Vp8Reader.FromSpan(data);
  static byte[] IImageFormatWriter<Vp8File>.ToBytes(Vp8File file) => Vp8Writer.ToBytes(file);
  static bool? IImageFormatMetadata<Vp8File>.MatchesSignature(ReadOnlySpan<byte> header) => Vp8Reader.MatchesSignature(header);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] Bitstream { get; init; }

  public static RawImage ToRawImage(Vp8File file)
    => new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = Vp8Decoder.Decode(file.Bitstream, file.Width, file.Height),
    };

  public static Vp8File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureAnyFormat(PixelFormat.Rgb24);

    return new() {
      Width = image.Width,
      Height = image.Height,
      Bitstream = Vp8Encoder.Encode(image),
    };
  }
}
