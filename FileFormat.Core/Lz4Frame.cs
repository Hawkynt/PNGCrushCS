using System;
using System.IO;

namespace FileFormat.Core;

/// <summary>Unpacks the LZ4 frame format, as the few vintage formats that adopted it use it.</summary>
/// <remarks>
/// LZ4 is a modern compressor rather than a period one, and it turns up here because a picture
/// format written recently for an old machine has no reason to invent a packer. Only the subset
/// those files use is accepted: independent blocks, no dictionary, and the optional checksums
/// skipped rather than verified — a decoder that refused a file for a checksum it does not compute
/// would be worse than one that reads it.
/// </remarks>
public static class Lz4Frame {

  /// <summary>The four bytes a frame starts with.</summary>
  public static ReadOnlySpan<byte> Magic => [4, 34, 77, 24];

  /// <summary>
  /// Unpacks a frame that must produce exactly <paramref name="unpackedLength"/> bytes and consume
  /// exactly the input given.
  /// </summary>
  public static byte[] Unpack(ReadOnlySpan<byte> data, int unpackedLength) {
    if (data.Length < 11 || !data[..Magic.Length].SequenceEqual(Magic) || (data[4] & 195) != 64)
      throw new InvalidDataException("Not an LZ4 frame this reader accepts.");

    var unpacked = new byte[unpackedLength];
    var at = 7;

    // A stated content size occupies eight bytes the decoder does not need.
    if ((data[4] & 8) != 0)
      at += 8;

    var target = 0;

    for (;;) {
      if (at + 4 > data.Length)
        throw new InvalidDataException("An LZ4 frame ends without its terminating block.");

      var blockSize = data[at] | (data[at + 1] << 8) | (data[at + 2] << 16) | (data[at + 3] << 24);
      at += 4;

      if (blockSize == 0)
        break;

      // The top bit says the block was not worth compressing and is stored as it is.
      if (blockSize < 0) {
        _CopyLiterals(data, ref at, data.Length, unpacked, ref target, blockSize & int.MaxValue);
        continue;
      }

      var end = at + blockSize;
      if (end > data.Length)
        throw new InvalidDataException("An LZ4 block runs past the end of the frame.");

      while (true) {
        if (at >= end)
          throw new InvalidDataException("An LZ4 block ends without a token.");

        var token = data[at++];
        _CopyLiterals(data, ref at, end, unpacked, ref target, _ReadCount(data, ref at, end, token >> 4));

        // A block ends after its literals, with no match — which is how the last one always ends.
        if (at == end)
          break;

        if (at > end - 2)
          throw new InvalidDataException("An LZ4 match has no distance.");

        var distance = data[at] | (data[at + 1] << 8);
        at += 2;
        if (distance == 0)
          throw new InvalidDataException("An LZ4 match points at itself.");

        // Four is the shortest match worth encoding, so the stored length counts from there.
        var count = _ReadCount(data, ref at, end, token & 15) + 4;
        if (target + count > unpackedLength || target - distance < 0)
          throw new InvalidDataException("An LZ4 match runs outside the picture.");

        // Byte at a time, because a match may overlap what it is still writing.
        for (var i = 0; i < count; ++i, ++target)
          unpacked[target] = unpacked[target - distance];
      }

      if ((data[4] & 16) != 0)
        at += 4;
    }

    if ((data[4] & 4) != 0)
      at += 4;

    if (at != data.Length || target != unpackedLength)
      throw new InvalidDataException("An LZ4 frame does not account for exactly its file and its picture.");

    return unpacked;
  }

  private static void _CopyLiterals(
    ReadOnlySpan<byte> data, ref int at, int end, Span<byte> unpacked, ref int target, int count) {
    if (at + count > end || target + count > unpacked.Length)
      throw new InvalidDataException("An LZ4 run of literals runs past the end.");

    data.Slice(at, count).CopyTo(unpacked[target..]);
    at += count;
    target += count;
  }

  /// <summary>
  /// Reads a length whose small values live in half a byte, continuing into further bytes when it
  /// does not fit.
  /// </summary>
  /// <remarks>
  /// The continuation is by addition rather than by shifting, and a byte of 255 means another
  /// follows — so a length of 300 is written as 15, 255, 30. That costs more than a plain count for
  /// long runs and nothing at all for short ones, which is the trade the format is built around.
  /// </remarks>
  private static int _ReadCount(ReadOnlySpan<byte> data, ref int at, int end, int count) {
    if (count != 15)
      return count;

    byte b;
    do {
      if (at >= end)
        throw new InvalidDataException("An LZ4 length runs past the end of its block.");

      b = data[at++];
      count += b;
    } while (b == 255);

    return count;
  }
}
