using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Tiny;

/// <summary>Compresses and decompresses Tiny format data using word-based RLE.</summary>
internal static class TinyCompressor {

  /// <summary>Decompresses Tiny compressed data into planar pixel data.</summary>
  /// <param name="data">The compressed data stream (after resolution byte and palette).</param>
  /// <param name="planeCount">Number of bitplanes (Low=4, Medium=2, High=1).</param>
  /// <param name="wordsPerPlane">Number of words per bitplane section.</param>
  /// <returns>Decompressed planar pixel data.</returns>
  public static byte[] Decompress(byte[] data, int planeCount, int wordsPerPlane) {
    var totalWords = planeCount * wordsPerPlane;
    var output = new short[totalWords];
    var span = data.AsSpan();
    var inIdx = 0;
    var outIdx = 0;

    for (var plane = 0; plane < planeCount; ++plane) {
      var planeStart = plane * wordsPerPlane;
      var planeEnd = planeStart + wordsPerPlane;
      outIdx = planeStart;

      var foundTerminator = false;
      while (outIdx < planeEnd && inIdx + 1 < span.Length) {
        var count = BinaryPrimitives.ReadInt16BigEndian(span[inIdx..]);
        inIdx += 2;

        if (count == 0) {
          foundTerminator = true;
          break;
        }

        if (count > 0) {
          for (var i = 0; i < count && outIdx < planeEnd && inIdx + 1 < span.Length; ++i) {
            output[outIdx++] = BinaryPrimitives.ReadInt16BigEndian(span[inIdx..]);
            inIdx += 2;
          }
        } else {
          var repeatCount = -count;
          if (inIdx + 1 >= span.Length)
            break;

          var value = BinaryPrimitives.ReadInt16BigEndian(span[inIdx..]);
          inIdx += 2;
          for (var i = 0; i < repeatCount && outIdx < planeEnd; ++i)
            output[outIdx++] = value;
        }
      }

      if (!foundTerminator)
        while (inIdx + 1 < span.Length) {
          var marker = BinaryPrimitives.ReadInt16BigEndian(span[inIdx..]);
          inIdx += 2;
          if (marker == 0)
            break;
        }
    }

    var result = new byte[totalWords * 2];
    for (var i = 0; i < totalWords; ++i)
      BinaryPrimitives.WriteInt16BigEndian(result.AsSpan(i * 2), output[i]);

    return result;
  }

  /// <summary>Compresses planar pixel data using word-based RLE.</summary>
  /// <param name="data">The decompressed planar pixel data.</param>
  /// <param name="planeCount">Number of bitplanes.</param>
  /// <param name="wordsPerPlane">Number of words per bitplane section.</param>
  /// <returns>Compressed data.</returns>
  public static byte[] Compress(byte[] data, int planeCount, int wordsPerPlane) {
    using var ms = new MemoryStream();
    var span = data.AsSpan();

    for (var plane = 0; plane < planeCount; ++plane) {
      var planeOffset = plane * wordsPerPlane * 2;
      var wordIdx = 0;

      while (wordIdx < wordsPerPlane) {
        var currentOffset = planeOffset + wordIdx * 2;
        var currentWord = BinaryPrimitives.ReadInt16BigEndian(span[currentOffset..]);

        var runLength = 1;
        while (wordIdx + runLength < wordsPerPlane && runLength < 32767) {
          var nextOffset = planeOffset + (wordIdx + runLength) * 2;
          if (BinaryPrimitives.ReadInt16BigEndian(span[nextOffset..]) != currentWord)
            break;
          ++runLength;
        }

        if (runLength >= 3) {
          _WriteInt16BigEndian(ms, (short)(-runLength));
          _WriteInt16BigEndian(ms, currentWord);
          wordIdx += runLength;
        } else {
          var literalStart = wordIdx;
          var literalCount = 0;

          while (wordIdx < wordsPerPlane && literalCount < 32767) {
            if (wordIdx + 2 < wordsPerPlane) {
              var o1 = planeOffset + wordIdx * 2;
              var o2 = planeOffset + (wordIdx + 1) * 2;
              var o3 = planeOffset + (wordIdx + 2) * 2;
              var w1 = BinaryPrimitives.ReadInt16BigEndian(span[o1..]);
              var w2 = BinaryPrimitives.ReadInt16BigEndian(span[o2..]);
              var w3 = BinaryPrimitives.ReadInt16BigEndian(span[o3..]);
              if (w1 == w2 && w2 == w3)
                break;
            }
            ++wordIdx;
            ++literalCount;
          }

          if (literalCount > 0) {
            _WriteInt16BigEndian(ms, (short)literalCount);
            for (var i = 0; i < literalCount; ++i) {
              var offset = planeOffset + (literalStart + i) * 2;
              _WriteInt16BigEndian(ms, BinaryPrimitives.ReadInt16BigEndian(span[offset..]));
            }
          }
        }
      }

      _WriteInt16BigEndian(ms, 0);
    }

    return ms.ToArray();
  }

  private static void _WriteInt16BigEndian(MemoryStream ms, short value) {
    Span<byte> buf = stackalloc byte[2];
    BinaryPrimitives.WriteInt16BigEndian(buf, value);
    ms.Write(buf);
  }
}
