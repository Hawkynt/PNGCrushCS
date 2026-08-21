using System;
using System.IO;

namespace FileFormat.Codecs.Hap;

/// <summary>
/// Snappy's block format — the "second-stage compressor" a Hap section or chunk may name — read from
/// the format itself, described in full in Google's own <c>format_description.txt</c>: no framing, no
/// entropy stage, a varint-prefixed length and a run of literal and back-reference elements.
/// </summary>
internal static class HapSnappyDecoder {

  /// <summary>Decompresses one Snappy block: a little-endian varint length followed by elements.</summary>
  public static byte[] Decompress(ReadOnlySpan<byte> input) {
    var pos = 0;
    var length = _ReadVarint(input, ref pos);
    var output = new byte[length];
    var outPos = 0;

    while (pos < input.Length) {
      var tag = input[pos++];
      switch (tag & 0x3) {
        case 0: { // literal
          var lenBits = tag >> 2;
          int len;
          if (lenBits < 60)
            len = lenBits + 1;
          else {
            var extraBytes = lenBits - 59;
            if (pos + extraBytes > input.Length)
              throw new InvalidDataException("A Snappy literal's length field runs past the end of the block.");

            len = 0;
            for (var i = 0; i < extraBytes; ++i)
              len |= input[pos++] << (8 * i);
            ++len;
          }

          if (pos + len > input.Length || outPos + len > output.Length)
            throw new InvalidDataException($"A Snappy literal of {len} bytes at output position {outPos} does not fit what remains of the block.");

          input.Slice(pos, len).CopyTo(output.AsSpan(outPos, len));
          pos += len;
          outPos += len;
          break;
        }

        case 1: { // copy, 1-byte offset
          if (pos >= input.Length)
            throw new InvalidDataException("A Snappy copy with a one-byte offset ends before that byte.");

          var len = ((tag >> 2) & 0x7) + 4;
          var offset = (((tag >> 5) & 0x7) << 8) | input[pos++];
          _CopyBack(output, ref outPos, offset, len);
          break;
        }

        case 2: { // copy, 2-byte offset
          if (pos + 2 > input.Length)
            throw new InvalidDataException("A Snappy copy with a two-byte offset ends before both bytes.");

          var len = (tag >> 2) + 1;
          var offset = input[pos] | (input[pos + 1] << 8);
          pos += 2;
          _CopyBack(output, ref outPos, offset, len);
          break;
        }

        default: { // copy, 4-byte offset
          if (pos + 4 > input.Length)
            throw new InvalidDataException("A Snappy copy with a four-byte offset ends before all four bytes.");

          var len = (tag >> 2) + 1;
          var offset = input[pos] | (input[pos + 1] << 8) | (input[pos + 2] << 16) | (input[pos + 3] << 24);
          pos += 4;
          _CopyBack(output, ref outPos, offset, len);
          break;
        }
      }
    }

    if (outPos != output.Length)
      throw new InvalidDataException($"A Snappy block's elements produced {outPos} bytes where its preamble states {output.Length}.");

    return output;
  }

  private static void _CopyBack(byte[] output, ref int outPos, int offset, int len) {
    if (offset <= 0 || offset > outPos)
      throw new InvalidDataException($"A Snappy copy names an offset of {offset} bytes back from output position {outPos}, which is not a place already written.");

    if (outPos + len > output.Length)
      throw new InvalidDataException($"A Snappy copy of {len} bytes at output position {outPos} does not fit what remains of the block.");

    var src = outPos - offset;
    for (var i = 0; i < len; ++i)
      output[outPos + i] = output[src + i];

    outPos += len;
  }

  private static int _ReadVarint(ReadOnlySpan<byte> input, ref int pos) {
    var result = 0;
    var shift = 0;
    while (true) {
      if (pos >= input.Length)
        throw new InvalidDataException("A Snappy block ends inside its length preamble.");

      var b = input[pos++];
      result |= (b & 0x7F) << shift;
      if ((b & 0x80) == 0)
        return result;

      shift += 7;
      if (shift > 35)
        throw new InvalidDataException("A Snappy block's length preamble is longer than any length it could encode.");
    }
  }
}
