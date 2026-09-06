using System;
using System.IO;

namespace FileFormat.Codecs.Hap;

/// <summary>
/// Writes one Snappy block — the "second-stage compressor" a Hap section may name — as Google's own
/// <c>format_description.txt</c> defines it: a little-endian varint holding the uncompressed length,
/// then a run of literal and back-reference elements.
/// </summary>
/// <remarks>
/// The mirror of <see cref="HapSnappyDecoder"/>, and read from the same document. Snappy's format is
/// entirely a decoder's contract — it states what an element means, never which element a compressor
/// must choose — so any stream whose elements reproduce the input is a correct one, and this produces
/// its own rather than reproducing the byte-for-byte output of Google's reference compressor.
/// <para/>
/// <b>What it chooses.</b> A four-byte hash table over the last <see cref="_WINDOW"/> bytes; at every
/// position, the most recent place the same four bytes began, taken as a back-reference and extended
/// as far as the two runs agree. Everything else accumulates into a literal. Offsets are held inside
/// sixteen bits and copies inside sixty-four bytes, so only the one- and two-byte copy forms are ever
/// written and no decoder has to reach the four-byte one. A run longer than sixty-four bytes becomes
/// several copies of the same offset, never one of them shorter than four, because a copy that short
/// costs more than the literal it replaces.
/// <para/>
/// <b>What it does not do.</b> No lazy matching, no second candidate per position, and no attempt at
/// the shortest encoding of a length — this is compressing a DXT texture that a caller may already
/// have decided not to compress at all, and the frame writer drops the result entirely whenever it
/// does not come out smaller than the texture it was made from.
/// </remarks>
internal static class HapSnappyEncoder {

  /// <summary>How far back a copy may reach: the widest offset the two-byte copy form can state.</summary>
  private const int _WINDOW = 65535;

  /// <summary>The longest run one copy element can name.</summary>
  private const int _LONGEST_COPY = 64;

  /// <summary>The shortest run worth naming at all — a copy element costs two bytes at best.</summary>
  private const int _SHORTEST_COPY = 4;

  /// <summary>The longest run the one-byte-offset copy form can name, and the widest offset it can reach.</summary>
  private const int _SHORT_COPY_LENGTH = 11;
  private const int _SHORT_COPY_OFFSET = 2047;

  /// <summary>How many literal bytes the tag byte alone can state, before a length field is needed.</summary>
  private const int _INLINE_LITERAL = 60;

  /// <summary>How many bits of the hash of a four-byte run are kept.</summary>
  private const int _HASH_BITS = 14;

  /// <summary>Knuth's multiplicative constant as Snappy's own reference compressor uses it.</summary>
  private const uint _HASH_MULTIPLIER = 0x1E35A7BD;

  /// <summary>Compresses one buffer into a single Snappy block.</summary>
  public static byte[] Compress(ReadOnlySpan<byte> input) {
    var output = new byte[_WorstCase(input.Length)];
    var at = _WriteVarint(output, 0, input.Length);

    var table = new int[1 << _HASH_BITS];
    var literalFrom = 0;
    var position = 0;

    while (position + _SHORTEST_COPY <= input.Length) {
      var hash = _Hash(input, position);
      var candidate = table[hash] - 1;
      table[hash] = position + 1;

      if (candidate < 0 || position - candidate > _WINDOW || !_AgreeOnFour(input, candidate, position)) {
        ++position;
        continue;
      }

      var length = _SHORTEST_COPY;
      while (position + length < input.Length && input[candidate + length] == input[position + length])
        ++length;

      at = _WriteLiteral(output, at, input[literalFrom..position]);
      at = _WriteCopy(output, at, position - candidate, length);

      // Every position the copy swallowed is still a place a later match could start from.
      for (var inside = position + 1; inside + _SHORTEST_COPY <= position + length; ++inside)
        table[_Hash(input, inside)] = inside + 1;

      position += length;
      literalFrom = position;
    }

    at = _WriteLiteral(output, at, input[literalFrom..]);
    return output[..at];
  }

  /// <summary>The most bytes a block of this length can take — Snappy's own stated bound, which is
  /// slack enough that no element sequence this writer can produce reaches it.</summary>
  private static int _WorstCase(int length) => 32 + length + length / 6;

  private static uint _Hash(ReadOnlySpan<byte> input, int at) {
    var word = (uint)(input[at] | (input[at + 1] << 8) | (input[at + 2] << 16) | (input[at + 3] << 24));
    return (word * _HASH_MULTIPLIER) >> (32 - _HASH_BITS);
  }

  private static bool _AgreeOnFour(ReadOnlySpan<byte> input, int first, int second)
    => input[first] == input[second]
      && input[first + 1] == input[second + 1]
      && input[first + 2] == input[second + 2]
      && input[first + 3] == input[second + 3];

  private static int _WriteVarint(Span<byte> output, int at, int value) {
    var remaining = (uint)value;
    while (remaining >= 0x80) {
      output[at++] = (byte)(remaining | 0x80);
      remaining >>= 7;
    }

    output[at++] = (byte)remaining;
    return at;
  }

  private static int _WriteLiteral(Span<byte> output, int at, ReadOnlySpan<byte> literal) {
    if (literal.Length == 0)
      return at;

    var stated = literal.Length - 1;
    if (stated < _INLINE_LITERAL)
      output[at++] = (byte)(stated << 2);
    else {
      var bytes = 1;
      while (stated >> (bytes * 8) != 0)
        ++bytes;

      output[at++] = (byte)((_INLINE_LITERAL - 1 + bytes) << 2);
      for (var i = 0; i < bytes; ++i)
        output[at++] = (byte)(stated >> (i * 8));
    }

    literal.CopyTo(output[at..]);
    return at + literal.Length;
  }

  private static int _WriteCopy(Span<byte> output, int at, int offset, int length) {
    if (offset <= 0 || offset > _WINDOW)
      throw new InvalidDataException($"A Snappy copy of {offset} bytes back cannot be stated in a two-byte offset.");

    // Long runs become several copies, and the last of them must still be worth writing: cutting a
    // 65-byte run into 64 and 1 would leave a copy shorter than any element can name.
    while (length > _LONGEST_COPY + _SHORTEST_COPY - 1) {
      at = _WriteOneCopy(output, at, offset, _LONGEST_COPY);
      length -= _LONGEST_COPY;
    }

    if (length > _LONGEST_COPY) {
      at = _WriteOneCopy(output, at, offset, length - _SHORTEST_COPY);
      length = _SHORTEST_COPY;
    }

    return _WriteOneCopy(output, at, offset, length);
  }

  private static int _WriteOneCopy(Span<byte> output, int at, int offset, int length) {
    if (length <= _SHORT_COPY_LENGTH && offset <= _SHORT_COPY_OFFSET) {
      output[at++] = (byte)(((length - _SHORTEST_COPY) << 2) | ((offset >> 8) << 5) | 0x01);
      output[at++] = (byte)offset;
      return at;
    }

    output[at++] = (byte)(((length - 1) << 2) | 0x02);
    output[at++] = (byte)offset;
    output[at++] = (byte)(offset >> 8);
    return at;
  }
}
