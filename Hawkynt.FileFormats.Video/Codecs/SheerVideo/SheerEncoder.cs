using System;
using FileFormat.Codecs.Hqx;

namespace FileFormat.Codecs.SheerVideo;

/// <summary>
/// Writes three SheerVideo layouts losslessly — <c> RGB</c>, <c> YbR</c> (4:4:4) and <c>BYRY</c> (4:2:2), all
/// eight bits — every row predicted exactly as FFmpeg's <c>decode_rgb</c>, <c>decode_ybr</c> and
/// <c>decode_byry</c> predict it, each residual the one that lands on the sample given.
/// </summary>
internal static class SheerEncoder {

  internal const uint Rgb = 0x42475220;   // " RGB"
  internal const uint Ybr = 0x52625920;   // " YbR"
  internal const uint Byry = 0x59525942;  // "BYRY"

  private static int _Gradient(int top, int left, int topLeft) => (3 * (top + left) - 2 * topLeft) >> 2;

  /// <summary>One frame: the header, then the rows. <paramref name="planes"/> are as the decoder holds them, with room past each row.</summary>
  internal static byte[] Encode(uint tag, int width, int height, int[][] planes, int[] strides) {
    var format = SheerFrame.Find(tag)!.Value;
    var codes = SheerFrame.Codes(format.Table);
    var writer = new HqxBitWriter();
    void Put(int table, int residual) => codes[table].TryWrite(writer, residual & 0xFF);

    for (var row = 0; row < height; ++row) {
      writer.Write(0, 1); // predicted
      switch (tag) {
        case Rgb: {
          var p = planes[0];
          var o = row * strides[0];
          int[] left = [-128, -128, -128];
          int[] topLeft = new int[3];
          if (row > 0)
            for (var c = 0; c < 3; ++c)
              left[c] = topLeft[c] = p[o - strides[0] + c];
          for (var x = 0; x < width; ++x) {
            Span<int> predicted = stackalloc int[3];
            for (var c = 0; c < 3; ++c) {
              if (row == 0)
                predicted[c] = left[c];
              else {
                var top = p[o - strides[0] + 4 * x + c];
                predicted[c] = _Gradient(top, left[c], topLeft[c]);
                topLeft[c] = top;
              }
            }
            int t0 = p[o + 4 * x], t1 = p[o + 4 * x + 1], t2 = p[o + 4 * x + 2];
            var r = t0 - predicted[0];
            var g = t1 - predicted[1] - r;
            var b = t2 - predicted[2] - r - g;
            Put(0, r);
            Put(1, g);
            Put(1, b);
            left[0] = t0;
            left[1] = t1;
            left[2] = t2;
          }
          break;
        }
        case Ybr: {
          int[] left = [-146, -128, -128];
          int[] topLeft = new int[3];
          if (row > 0)
            for (var c = 0; c < 3; ++c)
              left[c] = topLeft[c] = planes[c][row * strides[c] - strides[c]];
          for (var x = 0; x < width; ++x)
            for (var c = 0; c < 3; ++c) {
              var at = row * strides[c] + x;
              int predicted;
              if (row == 0)
                predicted = left[c];
              else {
                var top = planes[c][at - strides[c]];
                predicted = _Gradient(top, left[c], topLeft[c]);
                topLeft[c] = top;
              }
              Put(c == 0 ? 0 : 1, planes[c][at] - predicted);
              left[c] = planes[c][at];
            }
          break;
        }
        default: {
          int[] y = planes[0], u = planes[1], v = planes[2];
          int oy = row * strides[0], ou = row * strides[1], ov = row * strides[2];
          if (row == 0) {
            int ly = 125, lu = -128, lv = -128;
            for (var x = 0; x < width; x += 2) {
              Put(0, y[oy + x] - ly);
              ly = y[oy + x];
              Put(1, u[ou + x / 2] - lu);
              lu = u[ou + x / 2];
              Put(0, y[oy + x + 1] - ly);
              ly = y[oy + x + 1];
              Put(1, v[ov + x / 2] - lv);
              lv = v[ov + x / 2];
            }
            break;
          }
          int ly0 = y[oy - strides[0]], tly = ly0;
          int lu0 = u[ou - strides[1]], tlu = lu0;
          int lv0 = v[ov - strides[2]], tlv = lv0;
          for (var x = 0; x < width; x += 2) {
            int t0 = y[oy - strides[0] + x], t3 = y[oy - strides[0] + x + 1];
            int t1 = u[ou - strides[1] + x / 2], t2 = v[ov - strides[2] + x / 2];
            Put(0, y[oy + x] - _Gradient(t0, ly0, tly));
            ly0 = y[oy + x];
            Put(1, u[ou + x / 2] - (((lu0 - tlu) >> 1) + t1));
            lu0 = u[ou + x / 2];
            Put(0, y[oy + x + 1] - _Gradient(t3, ly0, t0));
            ly0 = y[oy + x + 1];
            Put(1, v[ov + x / 2] - (((lv0 - tlv) >> 1) + t2));
            lv0 = v[ov + x / 2];
            tly = t3;
            tlu = t1;
            tlv = t2;
          }
          break;
        }
      }
    }

    var body = writer.ToArray();
    var minimum = width * height / 16;
    var packet = new byte[SheerFrame.HeaderLength + Math.Max(body.Length, minimum)];
    "Shir"u8.CopyTo(packet);
    BitConverter.TryWriteBytes(packet.AsSpan(16), tag);
    body.CopyTo(packet, SheerFrame.HeaderLength);
    return packet;
  }
}
