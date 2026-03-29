using System.Globalization;
using System.IO;
using System.Text;
using FileFormat.Hdr;

namespace FileFormat.Hdr.Tests;

internal static class HdrTestHelper {

  public static byte[] BuildMinimalHdr(int width, int height) {
    using var ms = new MemoryStream();

    var header = $"#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y {height} +X {width}\n";
    var headerBytes = Encoding.ASCII.GetBytes(header);
    ms.Write(headerBytes, 0, headerBytes.Length);

    // Write uncompressed RGBE pixels (old-style for simplicity when width < 8)
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var (r, g, b, e) = RgbeCodec.EncodePixel(1.0f, 0.5f, 0.25f);
        ms.WriteByte(r);
        ms.WriteByte(g);
        ms.WriteByte(b);
        ms.WriteByte(e);
      }

    return ms.ToArray();
  }

  public static byte[] BuildHdrWithExposure(int width, int height, float exposure) {
    using var ms = new MemoryStream();

    var header = string.Format(
      CultureInfo.InvariantCulture,
      "#?RADIANCE\nFORMAT=32-bit_rle_rgbe\nEXPOSURE={0}\n\n-Y {1} +X {2}\n",
      exposure,
      height,
      width
    );
    var headerBytes = Encoding.ASCII.GetBytes(header);
    ms.Write(headerBytes, 0, headerBytes.Length);

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var (r, g, b, e) = RgbeCodec.EncodePixel(1.0f, 1.0f, 1.0f);
        ms.WriteByte(r);
        ms.WriteByte(g);
        ms.WriteByte(b);
        ms.WriteByte(e);
      }

    return ms.ToArray();
  }
}
