using System;

namespace FileFormat.RawGreyscale;

/// <summary>Writes a raw greyscale dump, which is the pixels and nothing else.</summary>
/// <remarks>
/// There is no header to assemble, so the only thing this can get wrong is the order — and the order
/// was settled against XnView's own converter, which writes these: a 320 by 240 picture handed to it
/// comes back as 76,800 bytes byte-identical to the pixels that went in, top row first.
/// </remarks>
public static class RawGreyscaleWriter {

  public static byte[] ToBytes(RawGreyscaleFile file) {
    var pixels = file.PixelData ?? [];
    var length = file.Width * file.Height * RawGreyscaleFile.BytesPerPixel;
    if (pixels.Length == length)
      return pixels[..];

    // A file built by hand rather than by the encoder can be short. Padding it with black is better
    // than writing a length that is none of the sizes the reader recognises, which nothing could open.
    var result = new byte[length];
    pixels.AsSpan(0, Math.Min(pixels.Length, length)).CopyTo(result);

    return result;
  }
}
