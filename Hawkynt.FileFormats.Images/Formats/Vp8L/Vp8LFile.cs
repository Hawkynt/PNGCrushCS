using System;
using FileFormat.Core;
using FileFormat.WebP;

namespace FileFormat.Vp8L;

/// <summary>A single WebP-lossless VP8L image stored without a RIFF/WebP container.</summary>
/// <remarks>
/// The five-byte VP8L preamble belongs to the bitstream and is therefore present in a bare
/// <c>.vp8l</c> file. Its alpha bit is only an encoder hint; decoding uses the alpha samples that
/// are actually present in the bitstream.
/// </remarks>
public readonly record struct Vp8LFile :
  IImageFormatReader<Vp8LFile>, IImageToRawImage<Vp8LFile>, IImageFromRawImage<Vp8LFile>, IImageFormatWriter<Vp8LFile> {

  static string IImageFormatMetadata<Vp8LFile>.PrimaryExtension => ".vp8l";
  static string[] IImageFormatMetadata<Vp8LFile>.FileExtensions => [".vp8l"];
  static Vp8LFile IImageFormatReader<Vp8LFile>.FromSpan(ReadOnlySpan<byte> data) => Vp8LReader.FromSpan(data);
  static byte[] IImageFormatWriter<Vp8LFile>.ToBytes(Vp8LFile file) => Vp8LWriter.ToBytes(file);
  static bool? IImageFormatMetadata<Vp8LFile>.MatchesSignature(ReadOnlySpan<byte> header) => Vp8LReader.MatchesSignature(header);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>The header's alpha-is-used hint. It is not authoritative for decoding.</summary>
  public bool AlphaHint { get; init; }

  public byte[] Bitstream { get; init; }

  public static RawImage ToRawImage(Vp8LFile file) {
    var argb = global::FileFormat.WebP.Vp8L.Vp8LDecoder.DecodeArgbStream(
      file.Bitstream, Vp8LHeader.StructSize, file.Width, file.Height);
    var hasAlpha = file.AlphaHint;

    if (!hasAlpha)
      foreach (var pixel in argb)
        if ((pixel >> 24) != 0xFF) {
          hasAlpha = true;
          break;
        }

    var pixelCount = checked(file.Width * file.Height);
    var bytesPerPixel = hasAlpha ? 4 : 3;
    var pixels = new byte[checked(pixelCount * bytesPerPixel)];

    for (var i = 0; i < pixelCount; ++i) {
      var pixel = argb[i];
      var offset = i * bytesPerPixel;
      pixels[offset] = (byte)(pixel >> 16);
      pixels[offset + 1] = (byte)(pixel >> 8);
      pixels[offset + 2] = (byte)pixel;
      if (hasAlpha)
        pixels[offset + 3] = (byte)(pixel >> 24);
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = hasAlpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }

  public static Vp8LFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // Keep the shared WebP encoder as the one VP8L implementation, but derive the standalone
    // alpha_is_used hint from pixel content. WebP conservatively treats every RGBA source as
    // alpha-bearing; VP8L specifies that the hint should be clear when every alpha sample is 255.
    image = image.EnsureAnyFormat(PixelFormat.Rgba32, PixelFormat.Rgb24);
    var alphaHint = image.Format == PixelFormat.Rgba32 && _HasTransparency(image);
    var webp = WebPFile.FromRawImage(image);
    var bitstream = webp.ImageData[..];
    if (!alphaHint)
      bitstream[4] &= 0xEF; // Bit 28 of the little-endian VP8L header.

    return new() {
      Width = webp.Features.Width,
      Height = webp.Features.Height,
      AlphaHint = alphaHint,
      Bitstream = bitstream,
    };
  }

  private static bool _HasTransparency(RawImage image) {
    var pixelCount = checked(image.Width * image.Height);
    for (var i = 0; i < pixelCount; ++i)
      if (image.PixelData[i * 4 + 3] != 0xFF)
        return true;

    return false;
  }
}
