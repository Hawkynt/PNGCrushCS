using System;
using System.IO;

namespace FileFormat.Bmp;

internal static class RleCompressor {

  public static byte[] CompressRle8(ReadOnlySpan<byte> scanline) {
    if (scanline.Length == 0)
      return [];

    using var ms = new MemoryStream();
    var i = 0;

    while (i < scanline.Length) {
      if (i + 1 < scanline.Length && scanline[i] == scanline[i + 1]) {
        var value = scanline[i];
        var runStart = i;
        while (i < scanline.Length && i - runStart < 255 && scanline[i] == value)
          ++i;

        var count = i - runStart;
        ms.WriteByte((byte)count);
        ms.WriteByte(value);
      } else {
        var literalStart = i;
        while (i < scanline.Length && i - literalStart < 255) {
          if (i + 1 < scanline.Length && scanline[i] == scanline[i + 1])
            break;
          ++i;
        }

        var count = i - literalStart;
        if (count < 3) {
          for (var j = literalStart; j < literalStart + count; ++j) {
            ms.WriteByte(1);
            ms.WriteByte(scanline[j]);
          }
        } else {
          ms.WriteByte(0x00);
          ms.WriteByte((byte)count);
          ms.Write(scanline.Slice(literalStart, count));
          if (count % 2 != 0)
            ms.WriteByte(0x00);
        }
      }
    }

    ms.WriteByte(0x00);
    ms.WriteByte(0x00);
    return ms.ToArray();
  }

  public static byte[] CompressRle4(ReadOnlySpan<byte> indices, int pixelCount) {
    if (pixelCount == 0)
      return [];

    using var ms = new MemoryStream();
    var i = 0;

    while (i < pixelCount) {
      var current = indices[i];
      var runStart = i;
      while (i < pixelCount && i - runStart < 254 && indices[i] == current)
        ++i;

      var count = i - runStart;
      if (count >= 3) {
        var highNibble = (byte)(current >> 4);
        var lowNibble = (byte)(current & 0x0F);
        ms.WriteByte((byte)count);
        ms.WriteByte((byte)((highNibble << 4) | lowNibble));
      } else {
        i = runStart;
        var literalStart = i;
        while (i < pixelCount && i - literalStart < 254) {
          if (i + 2 < pixelCount && indices[i] == indices[i + 1] && indices[i] == indices[i + 2])
            break;
          ++i;
        }

        var literalCount = i - literalStart;
        if (literalCount < 3) {
          for (var j = literalStart; j < literalStart + literalCount; ++j) {
            ms.WriteByte(1);
            ms.WriteByte((byte)(indices[j] << 4));
          }
        } else {
          ms.WriteByte(0x00);
          ms.WriteByte((byte)literalCount);
          var nibbleBytes = (literalCount + 1) / 2;
          for (var j = 0; j < nibbleBytes; ++j) {
            var srcIdx = literalStart + j * 2;
            var high = indices[srcIdx];
            var low = srcIdx + 1 < literalStart + literalCount ? indices[srcIdx + 1] : (byte)0;
            ms.WriteByte((byte)((high << 4) | (low & 0x0F)));
          }

          if (nibbleBytes % 2 != 0)
            ms.WriteByte(0x00);
        }
      }
    }

    ms.WriteByte(0x00);
    ms.WriteByte(0x00);
    return ms.ToArray();
  }

  /// <summary>Unpacks an eight-bit run-length picture into one index per pixel.</summary>
  /// <remarks>
  /// The opcodes are <see cref="MicrosoftRle"/>'s, which an <c>MRLE</c> video frame is coded with
  /// too — the same counts, the same three escapes, the same word alignment. A still starts on an
  /// empty canvas where a frame starts on the frame before it, and that difference is the argument
  /// this passes rather than a second copy of the walk.
  /// <para/>
  /// Damaged coding is taken as far as it goes rather than refused. A still is a thing somebody
  /// wants to look at, and most of a picture beats none of it; a decoder feeding a film has the
  /// opposite duty and asks for the refusal instead.
  /// </remarks>
  public static byte[] DecompressRle8(ReadOnlySpan<byte> data, int width, int height) {
    var output = new byte[width * height];
    MicrosoftRle.Decode(data, output, width, height, 8, refuseMalformed: false);
    return output;
  }

  /// <summary>Unpacks a 4-bit run-length picture into the rows an uncompressed one would have had.</summary>
  /// <remarks>
  /// The opcodes are the 8-bit ones with the pixels half as wide, so it is the same walk
  /// (<see cref="MicrosoftRle"/>) told which depth it is reading.
  /// <para/>
  /// What comes back is laid out as an uncompressed bitmap body — each row on a four-byte boundary,
  /// rows in the order the file stores them — so that the row ordering and the un-padding after it
  /// stay one path rather than two.
  /// </remarks>
  public static byte[] DecompressRle4(ReadOnlySpan<byte> data, int width, int height) {
    var indices = new byte[width * height];
    MicrosoftRle.Decode(data, indices, width, height, 4, refuseMalformed: false);

    var stride = ((width + 1) / 2 + 3) & ~3;
    var packed = new byte[stride * height];
    for (var row = 0; row < height; ++row)
    for (var column = 0; column < width; ++column) {
      var index = indices[row * width + column];
      var at = row * stride + column / 2;
      packed[at] |= (byte)((column & 1) == 0 ? index << 4 : index);
    }

    return packed;
  }

  public static double EstimateCompressionRatio(ReadOnlySpan<byte> data) {
    if (data.Length == 0)
      return 1.0;

    var sampleSize = Math.Min(data.Length, 4096);
    var sample = data[..sampleSize];

    var compressedSize = 0;
    var i = 0;
    while (i < sample.Length)
      if (i + 1 < sample.Length && sample[i] == sample[i + 1]) {
        var value = sample[i];
        var runStart = i;
        while (i < sample.Length && i - runStart < 255 && sample[i] == value)
          ++i;
        compressedSize += 2;
      } else {
        var literalStart = i;
        while (i < sample.Length && i - literalStart < 255) {
          if (i + 1 < sample.Length && sample[i] == sample[i + 1])
            break;
          ++i;
        }

        compressedSize += 2 + (i - literalStart);
      }

    return (double)compressedSize / sampleSize;
  }
}
