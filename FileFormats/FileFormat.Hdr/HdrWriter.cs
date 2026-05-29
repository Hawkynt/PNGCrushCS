using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace FileFormat.Hdr;

/// <summary>Assembles Radiance HDR file bytes from float pixel data.</summary>
public static class HdrWriter {

  public static byte[] ToBytes(HdrFile file) {
    ArgumentNullException.ThrowIfNull(file);

    using var ms = new MemoryStream();
    _WriteHeader(ms, file.Width, file.Height, file.Exposure);
    _WriteScanlines(ms, file.PixelData, file.Width, file.Height);
    return ms.ToArray();
  }

  private static void _WriteHeader(MemoryStream ms, int width, int height, float exposure) {
    var sb = new StringBuilder();
    sb.Append("#?RADIANCE\n");
    sb.Append("FORMAT=32-bit_rle_rgbe\n");
    if (Math.Abs(exposure - 1.0f) > 1e-6f)
      sb.Append(string.Format(CultureInfo.InvariantCulture, "EXPOSURE={0}\n", exposure));
    sb.Append('\n');
    sb.Append(string.Format(CultureInfo.InvariantCulture, "-Y {0} +X {1}\n", height, width));

    var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
    ms.Write(headerBytes, 0, headerBytes.Length);
  }

  private static void _WriteScanlines(MemoryStream ms, float[] pixels, int width, int height) {
    var rgbe = new byte[width * 4];

    for (var y = 0; y < height; ++y) {
      var rowOffset = y * width * 3;

      // Encode all pixels in this scanline to RGBE
      for (var x = 0; x < width; ++x) {
        var (r, g, b, e) = RgbeCodec.EncodePixel(
          pixels[rowOffset + x * 3],
          pixels[rowOffset + x * 3 + 1],
          pixels[rowOffset + x * 3 + 2]
        );
        rgbe[x * 4] = r;
        rgbe[x * 4 + 1] = g;
        rgbe[x * 4 + 2] = b;
        rgbe[x * 4 + 3] = e;
      }

      if (width >= 8 && width <= 0x7FFF)
        _WriteAdaptiveRleScanline(ms, rgbe, width);
      else
        _WriteOldStyleScanline(ms, rgbe, width);
    }
  }

  private static void _WriteAdaptiveRleScanline(MemoryStream ms, byte[] rgbe, int width) {
    // Write marker
    ms.WriteByte(2);
    ms.WriteByte(2);
    ms.WriteByte((byte)(width >> 8));
    ms.WriteByte((byte)(width & 0xFF));

    // Write 4 channels separately
    for (var ch = 0; ch < 4; ++ch) {
      var channel = new byte[width];
      for (var x = 0; x < width; ++x)
        channel[x] = rgbe[x * 4 + ch];

      _WriteRleChannel(ms, channel);
    }
  }

  private static void _WriteRleChannel(MemoryStream ms, byte[] channel) {
    var i = 0;
    while (i < channel.Length) {
      // Look for runs
      if (i + 1 < channel.Length && channel[i] == channel[i + 1]) {
        var value = channel[i];
        var runStart = i;
        while (i < channel.Length && i - runStart < 127 && channel[i] == value)
          ++i;

        var runLength = i - runStart;
        ms.WriteByte((byte)(runLength + 128));
        ms.WriteByte(value);
      } else {
        // Literal run
        var literalStart = i;
        while (i < channel.Length && i - literalStart < 128) {
          if (i + 1 < channel.Length && channel[i] == channel[i + 1])
            break;
          ++i;
        }

        var literalLength = i - literalStart;
        ms.WriteByte((byte)literalLength);
        ms.Write(channel, literalStart, literalLength);
      }
    }
  }

  private static void _WriteOldStyleScanline(MemoryStream ms, byte[] rgbe, int width) {
    for (var x = 0; x < width; ++x) {
      ms.WriteByte(rgbe[x * 4]);
      ms.WriteByte(rgbe[x * 4 + 1]);
      ms.WriteByte(rgbe[x * 4 + 2]);
      ms.WriteByte(rgbe[x * 4 + 3]);
    }
  }
}
