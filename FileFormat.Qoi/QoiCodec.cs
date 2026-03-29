using System;

namespace FileFormat.Qoi;

/// <summary>QOI pixel data encoder and decoder.</summary>
internal static class QoiCodec {

  private const byte _QOI_OP_INDEX = 0x00; // 0b00xxxxxx
  private const byte _QOI_OP_DIFF = 0x40;  // 0b01xxxxxx
  private const byte _QOI_OP_LUMA = 0x80;  // 0b10xxxxxx
  private const byte _QOI_OP_RUN = 0xC0;   // 0b11xxxxxx
  private const byte _QOI_OP_RGB = 0xFE;
  private const byte _QOI_OP_RGBA = 0xFF;
  private const byte _QOI_MASK_2 = 0xC0;

  private static int _HashPixel(byte r, byte g, byte b, byte a) => (r * 3 + g * 5 + b * 7 + a * 11) % 64;

  public static byte[] Encode(byte[] pixelData, int width, int height, QoiChannels channels) {
    var channelCount = (int)channels;
    var pixelCount = width * height;
    // Worst case: header + all RGBA ops + end marker
    var maxSize = QoiHeader.StructSize + pixelCount * 5 + 8;
    var output = new byte[maxSize];
    var pos = 0;

    // Write header
    var header = new QoiHeader(
      (byte)'q', (byte)'o', (byte)'i', (byte)'f',
      (uint)width, (uint)height, channels, QoiColorSpace.Srgb
    );
    header.WriteTo(output.AsSpan(0, QoiHeader.StructSize));
    pos = QoiHeader.StructSize;

    var index = new byte[64 * 4]; // 64 RGBA entries
    byte prevR = 0, prevG = 0, prevB = 0, prevA = 255;
    var run = 0;

    for (var px = 0; px < pixelCount; ++px) {
      var offset = px * channelCount;
      var r = pixelData[offset];
      var g = pixelData[offset + 1];
      var b = pixelData[offset + 2];
      var a = channelCount == 4 ? pixelData[offset + 3] : (byte)255;

      if (r == prevR && g == prevG && b == prevB && a == prevA) {
        ++run;
        if (run == 62 || px == pixelCount - 1) {
          output[pos++] = (byte)(_QOI_OP_RUN | (run - 1));
          run = 0;
        }
      } else {
        if (run > 0) {
          output[pos++] = (byte)(_QOI_OP_RUN | (run - 1));
          run = 0;
        }

        var hash = _HashPixel(r, g, b, a);
        var idxOff = hash * 4;

        if (index[idxOff] == r && index[idxOff + 1] == g && index[idxOff + 2] == b && index[idxOff + 3] == a) {
          output[pos++] = (byte)(_QOI_OP_INDEX | hash);
        } else {
          index[idxOff] = r;
          index[idxOff + 1] = g;
          index[idxOff + 2] = b;
          index[idxOff + 3] = a;

          if (a == prevA) {
            var dr = r - prevR;
            var dg = g - prevG;
            var db = b - prevB;

            var drDg = dr - dg;
            var dbDg = db - dg;

            if (dr >= -2 && dr <= 1 && dg >= -2 && dg <= 1 && db >= -2 && db <= 1) {
              output[pos++] = (byte)(_QOI_OP_DIFF | ((dr + 2) << 4) | ((dg + 2) << 2) | (db + 2));
            } else if (dg >= -32 && dg <= 31 && drDg >= -8 && drDg <= 7 && dbDg >= -8 && dbDg <= 7) {
              output[pos++] = (byte)(_QOI_OP_LUMA | (dg + 32));
              output[pos++] = (byte)(((drDg + 8) << 4) | (dbDg + 8));
            } else {
              output[pos++] = _QOI_OP_RGB;
              output[pos++] = r;
              output[pos++] = g;
              output[pos++] = b;
            }
          } else {
            output[pos++] = _QOI_OP_RGBA;
            output[pos++] = r;
            output[pos++] = g;
            output[pos++] = b;
            output[pos++] = a;
          }
        }

        prevR = r;
        prevG = g;
        prevB = b;
        prevA = a;
      }
    }

    // End marker: 7 x 0x00 + 0x01
    for (var i = 0; i < 7; ++i)
      output[pos++] = 0x00;
    output[pos++] = 0x01;

    var result = new byte[pos];
    Array.Copy(output, result, pos);
    return result;
  }

  public static byte[] Decode(byte[] encoded, int width, int height, QoiChannels channels) {
    var channelCount = (int)channels;
    var pixelCount = width * height;
    var pixelData = new byte[pixelCount * channelCount];

    var index = new byte[64 * 4]; // 64 RGBA entries
    byte r = 0, g = 0, b = 0, a = 255;
    var pos = 0;
    var px = 0;

    while (px < pixelCount && pos < encoded.Length) {
      var tag = encoded[pos];

      if (tag == _QOI_OP_RGB) {
        r = encoded[pos + 1];
        g = encoded[pos + 2];
        b = encoded[pos + 3];
        pos += 4;
      } else if (tag == _QOI_OP_RGBA) {
        r = encoded[pos + 1];
        g = encoded[pos + 2];
        b = encoded[pos + 3];
        a = encoded[pos + 4];
        pos += 5;
      } else if ((tag & _QOI_MASK_2) == _QOI_OP_INDEX) {
        var idx = tag & 0x3F;
        var idxOff = idx * 4;
        r = index[idxOff];
        g = index[idxOff + 1];
        b = index[idxOff + 2];
        a = index[idxOff + 3];
        ++pos;
      } else if ((tag & _QOI_MASK_2) == _QOI_OP_DIFF) {
        r += (byte)(((tag >> 4) & 0x03) - 2);
        g += (byte)(((tag >> 2) & 0x03) - 2);
        b += (byte)((tag & 0x03) - 2);
        ++pos;
      } else if ((tag & _QOI_MASK_2) == _QOI_OP_LUMA) {
        var dg = (tag & 0x3F) - 32;
        var next = encoded[pos + 1];
        var drDg = ((next >> 4) & 0x0F) - 8;
        var dbDg = (next & 0x0F) - 8;
        r += (byte)(dg + drDg);
        g += (byte)dg;
        b += (byte)(dg + dbDg);
        pos += 2;
      } else if ((tag & _QOI_MASK_2) == _QOI_OP_RUN) {
        var runLen = (tag & 0x3F) + 1;
        for (var i = 0; i < runLen && px < pixelCount; ++i) {
          var offset = px * channelCount;
          pixelData[offset] = r;
          pixelData[offset + 1] = g;
          pixelData[offset + 2] = b;
          if (channelCount == 4)
            pixelData[offset + 3] = a;
          ++px;
        }
        ++pos;
        continue;
      }

      // Store in index
      var hash = _HashPixel(r, g, b, a);
      var hOff = hash * 4;
      index[hOff] = r;
      index[hOff + 1] = g;
      index[hOff + 2] = b;
      index[hOff + 3] = a;

      // Write pixel
      var pxOffset = px * channelCount;
      pixelData[pxOffset] = r;
      pixelData[pxOffset + 1] = g;
      pixelData[pxOffset + 2] = b;
      if (channelCount == 4)
        pixelData[pxOffset + 3] = a;
      ++px;
    }

    return pixelData;
  }
}
