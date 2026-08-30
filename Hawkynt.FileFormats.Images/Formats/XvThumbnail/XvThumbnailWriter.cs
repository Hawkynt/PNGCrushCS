using System;
using System.Text;

namespace FileFormat.XvThumbnail;

/// <summary>Assembles XV thumbnail file bytes from RGB332 pixel data.</summary>
public static class XvThumbnailWriter {

  private const int _MaximumPixels = 100_000_000;

  public static byte[] ToBytes(XvThumbnailFile file) => Assemble(file.PixelData, file.Width, file.Height);

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    ArgumentNullException.ThrowIfNull(pixelData);
    if (width <= 0 || height <= 0)
      throw new ArgumentException("XV thumbnail dimensions must be positive.");

    var pixelCount = (long)width * height;
    if (pixelCount > _MaximumPixels)
      throw new ArgumentException($"XV thumbnail exceeds the {_MaximumPixels:N0}-pixel implementation safety limit.");
    if (pixelData.Length != pixelCount)
      throw new ArgumentException($"XV thumbnail requires exactly {pixelCount} raster bytes, got {pixelData.Length}.", nameof(pixelData));

    // This is the header shape produced by XV-compatible tooling: the P7 332 discriminator, zero or
    // more comment lines, then width/height and the fixed maxval 255. #END_OF_COMMENTS is conventional
    // rather than structurally necessary, but emitting it improves compatibility with older readers.
    var header = Encoding.ASCII.GetBytes($"P7 332\n#END_OF_COMMENTS\n{width} {height} 255\n");
    var result = new byte[checked(header.Length + (int)pixelCount)];
    header.CopyTo(result, 0);
    pixelData.CopyTo(result, header.Length);
    return result;
  }
}
