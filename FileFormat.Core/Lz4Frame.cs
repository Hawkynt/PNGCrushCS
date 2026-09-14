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
      if (end < at || end > data.Length)
        throw new InvalidDataException("An LZ4 block runs past the end of the frame.");

      target += Lz4Block.DecodeInto(data[at..end], unpacked.AsSpan(target));
      at = end;

      if ((data[4] & 16) != 0) {
        if (at > data.Length - 4)
          throw new InvalidDataException("An LZ4 frame ends inside a block checksum.");
        at += 4;
      }
    }

    if ((data[4] & 4) != 0) {
      if (at > data.Length - 4)
        throw new InvalidDataException("An LZ4 frame ends inside its content checksum.");
      at += 4;
    }

    if (at != data.Length || target != unpackedLength)
      throw new InvalidDataException("An LZ4 frame does not account for exactly its file and its picture.");

    return unpacked;
  }

  private static void _CopyLiterals(
    ReadOnlySpan<byte> data, ref int at, int end, Span<byte> unpacked, ref int target, int count) {
    if (count < 0 || at > end - count || target > unpacked.Length - count)
      throw new InvalidDataException("An LZ4 run of literals runs past the end.");

    data.Slice(at, count).CopyTo(unpacked[target..]);
    at += count;
    target += count;
  }
}
