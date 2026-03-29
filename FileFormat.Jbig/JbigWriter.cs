using System;
using System.Collections.Generic;

namespace FileFormat.Jbig;

/// <summary>Encodes 1bpp pixel data to JBIG1 (ITU-T T.82) BIE format.</summary>
public static class JbigWriter {

  public static byte[] ToBytes(JbigFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var bytesPerRow = (width + 7) / 8;
    var l0 = height; // single stripe for simplicity

    // Build header
    var header = new JbigHeader(
      DL: 0,
      D: 0,
      P: 1,
      Reserved: 0,
      XD: width,
      YD: height,
      L0: l0,
      MX: 8,
      MY: 0,
      Options: JbigHeader.OptionTPBON,
      Order: 0
    );

    var result = new List<byte>();
    var headerBytes = new byte[JbigHeader.StructSize];
    header.WriteTo(headerBytes.AsSpan());
    result.AddRange(headerBytes);

    // Encode all stripes
    var stripeStart = 0;
    while (stripeStart < height) {
      var stripeEnd = Math.Min(stripeStart + l0, height);
      var stripeData = _EncodeStripe(pixelData, width, height, bytesPerRow, stripeStart, stripeEnd);
      result.AddRange(stripeData);

      // Write SDNORM marker
      result.Add(0xFF);
      result.Add(0x02);

      stripeStart = stripeEnd;
    }

    return [.. result];
  }

  private static byte[] _EncodeStripe(byte[] pixelData, int width, int height, int bytesPerRow, int stripeStart, int stripeEnd) {
    var states = new int[JbigContext.ContextCount];
    var mps = new int[JbigContext.ContextCount];
    var encoder = new ArithmeticCoder.Encoder();

    var cur = new byte[width];
    var prev1 = new byte[width];
    var prev2 = new byte[width];

    const int tpContext = 0x01E3;

    byte[] prevLine = new byte[width];
    var ltp = false;

    for (var y = stripeStart; y < stripeEnd; ++y) {
      // Unpack current row
      Array.Clear(cur);
      var rowOffset = y * bytesPerRow;
      for (var x = 0; x < width; ++x)
        cur[x] = (byte)((pixelData[rowOffset + (x >> 3)] >> (7 - (x & 7))) & 1);

      // Check if this line is the same as previous (for TPBON)
      var isTypical = y > 0 && _LinesEqual(cur, prevLine, width);

      // TPBON: encode whether typical prediction changes
      var slntp = isTypical != ltp ? 1 : 0;
      encoder.EncodeBit(slntp, tpContext, states, mps);
      ltp = isTypical;

      if (!isTypical) {
        for (var x = 0; x < width; ++x) {
          var cx = JbigContext.GetContext(cur, prev1, prev2, x, width);
          encoder.EncodeBit(cur[x], cx, states, mps);
        }
      }

      // Save for typical prediction check
      cur.AsSpan(0, width).CopyTo(prevLine);

      // Shift line buffers
      var temp = prev2;
      prev2 = prev1;
      prev1 = cur;
      cur = temp;
    }

    return encoder.Flush();
  }

  private static bool _LinesEqual(byte[] a, byte[] b, int length) {
    for (var i = 0; i < length; ++i)
      if (a[i] != b[i])
        return false;

    return true;
  }
}
