using System;
using System.IO;

namespace FileFormat.Jbig;

/// <summary>Reads JBIG1 (ITU-T T.82) BIE files from bytes, streams, or file paths.</summary>
public static class JbigReader {

  public static JbigFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("JBIG file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static JbigFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static JbigFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static JbigFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < JbigHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid JBIG file.");

    var header = JbigHeader.ReadFrom(data.AsSpan());
    var width = header.XD;
    var height = header.YD;

    if (width <= 0)
      throw new InvalidDataException($"Invalid JBIG width: {width}.");

    if (height <= 0)
      throw new InvalidDataException($"Invalid JBIG height: {height}.");

    if (header.D != 0)
      throw new InvalidDataException("Only single-resolution JBIG (D=0) is supported.");

    if (header.P != 1)
      throw new InvalidDataException("Only single-plane JBIG (P=1) is supported.");

    var l0 = header.L0;
    if (l0 <= 0)
      l0 = height;

    var tpbon = (header.Options & JbigHeader.OptionTPBON) != 0;
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];

    _Decode(data, JbigHeader.StructSize, data.Length - JbigHeader.StructSize, width, height, l0, tpbon, pixelData);

    return new JbigFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }

  private static void _Decode(byte[] data, int offset, int length, int width, int height, int l0, bool tpbon, byte[] pixelData) {
    var bytesPerRow = (width + 7) / 8;
    var states = new int[JbigContext.ContextCount];
    var mps = new int[JbigContext.ContextCount];

    var cur = new byte[width];
    var prev1 = new byte[width];
    var prev2 = new byte[width];

    // TPBON context index (context for detecting typical line)
    const int tpContext = 0x01E3; // typical prediction context marker

    var dataPos = offset;
    var stripeStart = 0;

    while (stripeStart < height) {
      var stripeEnd = Math.Min(stripeStart + l0, height);
      var stripeDataLen = _FindStripeEnd(data, dataPos, offset + length);
      var decoder = new ArithmeticCoder.Decoder(data, dataPos, stripeDataLen);

      // Reset states for each stripe
      Array.Clear(states);
      Array.Clear(mps);

      var ltp = false;
      for (var y = stripeStart; y < stripeEnd; ++y) {
        if (tpbon) {
          var slntp = decoder.DecodeBit(tpContext, states, mps);
          ltp ^= slntp != 0;
        }

        if (tpbon && ltp && y > 0) {
          // Copy previous line
          var srcOffset = (y - 1) * bytesPerRow;
          var dstOffset = y * bytesPerRow;
          pixelData.AsSpan(srcOffset, bytesPerRow).CopyTo(pixelData.AsSpan(dstOffset));
          prev1.AsSpan(0, width).CopyTo(cur);
        } else {
          Array.Clear(cur);
          for (var x = 0; x < width; ++x) {
            var cx = JbigContext.GetContext(cur, prev1, prev2, x, width);
            var bit = decoder.DecodeBit(cx, states, mps);
            cur[x] = (byte)(bit & 1);
          }

          // Pack the current scanline into pixel data
          var rowOffset = y * bytesPerRow;
          for (var x = 0; x < width; ++x) {
            if (cur[x] != 0)
              pixelData[rowOffset + (x >> 3)] |= (byte)(0x80 >> (x & 7));
          }
        }

        // Shift line buffers
        var temp = prev2;
        prev2 = prev1;
        prev1 = cur;
        cur = temp;
        Array.Clear(cur);
      }

      dataPos += stripeDataLen;
      // Skip past any stripe marker (FF 02 SDNORM or FF 03 SDRST)
      if (dataPos + 1 < offset + length && data[dataPos] == 0xFF && (data[dataPos + 1] == 0x02 || data[dataPos + 1] == 0x03))
        dataPos += 2;

      stripeStart = stripeEnd;
    }
  }

  private static int _FindStripeEnd(byte[] data, int start, int end) {
    // Look for stripe marker FF 02 or FF 03
    for (var i = start; i < end - 1; ++i)
      if (data[i] == 0xFF && (data[i + 1] == 0x02 || data[i + 1] == 0x03))
        return i - start;

    // No marker found; use all remaining data
    return end - start;
  }
}
