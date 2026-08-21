using System;
using System.IO;

namespace FileFormat.Codecs.Cscd;

/// <summary>
/// Decompresses LZO1X — literal runs and back-references into the output already produced, the
/// compression CamStudio's reference encoder used before it offered zlib as an alternative.
/// </summary>
/// <remarks>
/// Written from "LZO stream format as understood by Linux's LZO decompressor" (Willy Tarreau, 2014;
/// updated by Dave Rodgman, 2018), which opens by saying plainly that no specification is publicly
/// available for the format and that the document describes what the Linux kernel's own decompressor
/// accepts. That is the closest thing to a specification this format has, and one place in it reads
/// two ways at once: the bit diagram for the sixteen-bit distance-and-state word that follows several
/// of the instructions could be read either as two bytes' own bit patterns in transmission order, or
/// as the reconstructed value's bits from most to least significant with "LE16" describing only how
/// the two bytes assemble into it. The two readings disagree about which four bits are distance and
/// which are state. The <c>lzop</c> command-line tool's own encoder, used to build streams of known
/// content and decode them against both readings, settles it: the state is the low two bits of the
/// reconstructed sixteen-bit value and the distance is everything above them — the second reading.
/// <para/>
/// This reads version 0 only. Version 1 adds a run-length extension for zeroes that answers a need
/// (zram) which postdates CamStudio by a decade; no version-1 stream has been found or is expected,
/// and one that named itself as such would be refused rather than guessed at.
/// <para/>
/// The decompressed length is known before this is called — it is the picture's own byte count — so
/// this fills exactly that many bytes and stops, rather than trusting the stream's own end marker to
/// arrive at the right place.
/// </remarks>
internal static class Lzo1x {

  /// <summary>Decompresses into a buffer of exactly <paramref name="outputLength"/> bytes.</summary>
  internal static byte[] Decompress(ReadOnlySpan<byte> data, int outputLength) {
    var output = new byte[outputLength];
    var op = 0;
    var ip = 0;
    var state = 0;

    if (data.Length == 0)
      return output;

    // The first byte only, before any instruction has run: version marker, a short literal copy,
    // or a long literal copy, in that priority — everything else falls through to the ordinary
    // instruction dispatch below with state left at zero, exactly as if a previous instruction had
    // copied no literals.
    var first = data[0];
    if (first == 17) {
      if (data.Length < 5)
        throw new InvalidDataException(
          "An LZO1X stream opens with the version marker (17) but is too short to carry one.");

      var version = data[1];
      if (version != 0)
        throw new NotSupportedException($"An LZO1X stream states bitstream version {version}. Only version 0 is read.");

      ip = 2;
    } else if (first is >= 18 and <= 21) {
      state = first - 17;
      ip = 1;
      _CopyLiterals(data, output, ref ip, ref op, state);
    } else if (first >= 22) {
      var length = first - 17;
      ip = 1;
      _CopyLiterals(data, output, ref ip, ref op, length);
      state = 4;
    }

    while (op < outputLength) {
      if (ip >= data.Length)
        throw new InvalidDataException(
          $"An LZO1X stream ran out of instructions {outputLength - op} byte(s) short of the picture it is coding.");

      var instruction = data[ip++];
      int length;
      int distance;

      if (instruction < 16) {
        if (state == 0) {
          length = 3 + _VariableLength(data, ref ip, instruction, 15);
          state = 4;
          _CopyLiterals(data, output, ref ip, ref op, length);
          continue;
        }

        var d = (instruction >> 2) & 3;
        var s = instruction & 3;
        var h = _ReadByte(data, ref ip);
        if (state == 4) {
          length = 3;
          distance = (h << 2) + d + 2049;
        } else {
          length = 2;
          distance = (h << 2) + d + 1;
        }

        _CopyMatch(output, ref op, distance, length);
        state = s;
        _CopyLiterals(data, output, ref ip, ref op, state);
        continue;
      }

      if (instruction < 32) {
        var h = (instruction >> 3) & 1;
        length = 2 + _VariableLength(data, ref ip, instruction & 7, 7);
        var word = _ReadWord(data, ref ip);
        var d14 = word >> 2;
        var s = word & 3;

        distance = 16384 + (h << 14) + d14;
        if (distance == 16384)
          return output; // end of stream

        _CopyMatch(output, ref op, distance, length);
        state = s;
        _CopyLiterals(data, output, ref ip, ref op, state);
        continue;
      }

      if (instruction < 64) {
        length = 2 + _VariableLength(data, ref ip, instruction & 0x1F, 31);
        var word = _ReadWord(data, ref ip);
        var d14 = word >> 2;
        var s = word & 3;

        distance = d14 + 1;
        _CopyMatch(output, ref op, distance, length);
        state = s;
        _CopyLiterals(data, output, ref ip, ref op, state);
        continue;
      }

      if (instruction < 128) {
        var l = (instruction >> 5) & 1;
        var d = (instruction >> 2) & 7;
        var s = instruction & 3;
        var h = _ReadByte(data, ref ip);

        length = 3 + l;
        distance = (h << 3) + d + 1;
        _CopyMatch(output, ref op, distance, length);
        state = s;
        _CopyLiterals(data, output, ref ip, ref op, state);
        continue;
      }

      {
        var l = (instruction >> 5) & 3;
        var d = (instruction >> 2) & 7;
        var s = instruction & 3;
        var h = _ReadByte(data, ref ip);

        length = 5 + l;
        distance = (h << 3) + d + 1;
        _CopyMatch(output, ref op, distance, length);
        state = s;
        _CopyLiterals(data, output, ref ip, ref op, state);
      }
    }

    return output;
  }

  /// <summary>
  /// The variable-length extension every opcode class with an <c>L</c> field falls back on when that
  /// field reads zero: a run of 255-valued zero bytes, each worth 255, ended by the first non-zero
  /// byte, which is added on top.
  /// </summary>
  private static int _VariableLength(ReadOnlySpan<byte> data, ref int ip, int l, int baseExtend) {
    if (l != 0)
      return l;

    var extra = baseExtend;
    while (true) {
      var b = _ReadByte(data, ref ip);
      if (b != 0) {
        extra += b;
        return extra;
      }

      extra += 255;
    }
  }

  private static void _CopyLiterals(ReadOnlySpan<byte> data, byte[] output, ref int ip, ref int op, int count) {
    if (count == 0)
      return;

    if (ip + count > data.Length)
      throw new InvalidDataException(
        $"An LZO1X stream names {count} literal byte(s) at compressed offset {ip} and only {data.Length - ip} remain.");

    data.Slice(ip, count).CopyTo(output.AsSpan(op, count));
    ip += count;
    op += count;
  }

  private static byte _ReadByte(ReadOnlySpan<byte> data, ref int ip) {
    if (ip >= data.Length)
      throw new InvalidDataException("An LZO1X instruction names an extra byte that is not there.");

    return data[ip++];
  }

  private static int _ReadWord(ReadOnlySpan<byte> data, ref int ip) {
    if (ip + 1 >= data.Length)
      throw new InvalidDataException("An LZO1X instruction names a two-byte distance word that is not there.");

    var word = data[ip] | (data[ip + 1] << 8);
    ip += 2;
    return word;
  }

  /// <summary>Copies a back-reference, byte by byte so a distance shorter than the length repeats
  /// correctly rather than reading ahead of what has been written.</summary>
  private static void _CopyMatch(byte[] output, ref int op, int distance, int length) {
    if (distance <= 0 || distance > op)
      throw new InvalidDataException(
        $"An LZO1X match at output offset {op} names a distance of {distance}, which reaches before the start of "
        + "the picture.");

    if (op + length > output.Length)
      throw new InvalidDataException(
        $"An LZO1X match at output offset {op} of length {length} would write past the end of the {output.Length}-byte picture.");

    var source = op - distance;
    for (var i = 0; i < length; ++i)
      output[op + i] = output[source + i];

    op += length;
  }
}
