using System;
using System.IO;

namespace FileFormat.Core;

/// <summary>Reads and writes raw LZ4 blocks without the surrounding frame format.</summary>
/// <remarks>
/// Ut Video T2 uses one raw LZ4 block for each compressed control stream. The decoder is shared
/// with <see cref="Lz4Frame"/> so the token, length and overlapping-match rules have one
/// implementation. The writer deliberately emits literals only: that is a valid LZ4 block, keeps
/// the implementation deterministic and dependency-free, and leaves match finding as a compression
/// optimisation rather than an interoperability requirement.
/// </remarks>
public static class Lz4Block {

  /// <summary>Unpacks exactly one raw LZ4 block to exactly <paramref name="unpackedLength"/> bytes.</summary>
  public static byte[] Unpack(ReadOnlySpan<byte> data, int unpackedLength) {
    ArgumentOutOfRangeException.ThrowIfNegative(unpackedLength);

    var result = new byte[unpackedLength];
    var written = DecodeInto(data, result);
    if (written != unpackedLength)
      throw new InvalidDataException(
        $"An LZ4 block produced {written} bytes where {unpackedLength} were required.");

    return result;
  }

  /// <summary>
  /// Writes one valid raw LZ4 block containing only literals.
  /// </summary>
  public static byte[] PackLiteralOnly(ReadOnlySpan<byte> data) {
    var extra = data.Length < 15 ? 0 : (data.Length - 15) / 255 + 1;
    var result = new byte[checked(1 + extra + data.Length)];
    var at = 0;
    result[at++] = (byte)(Math.Min(data.Length, 15) << 4);

    if (data.Length >= 15) {
      var remaining = data.Length - 15;
      while (remaining >= 255) {
        result[at++] = 255;
        remaining -= 255;
      }

      result[at++] = (byte)remaining;
    }

    data.CopyTo(result.AsSpan(at));
    return result;
  }

  /// <summary>
  /// Decodes one complete raw LZ4 block into the beginning of <paramref name="target"/> and returns
  /// how many bytes it produced.
  /// </summary>
  internal static int DecodeInto(ReadOnlySpan<byte> data, Span<byte> target) {
    var at = 0;
    var written = 0;

    while (at < data.Length) {
      var token = data[at++];
      var literals = _ReadCount(data, ref at, token >> 4);
      if (at > data.Length - literals || written > target.Length - literals)
        throw new InvalidDataException("An LZ4 run of literals runs past the end of its block or target.");

      data.Slice(at, literals).CopyTo(target[written..]);
      at += literals;
      written += literals;

      // A block ends with literals and has no distance after the final sequence.
      if (at == data.Length)
        break;

      if (at > data.Length - 2)
        throw new InvalidDataException("An LZ4 match has no distance.");

      var distance = data[at] | data[at + 1] << 8;
      at += 2;
      if (distance == 0 || distance > written)
        throw new InvalidDataException("An LZ4 match points outside the bytes already produced by this block.");

      var count = checked(_ReadCount(data, ref at, token & 15) + 4);
      if (written > target.Length - count)
        throw new InvalidDataException("An LZ4 match runs past the end of its target.");

      for (var i = 0; i < count; ++i, ++written)
        target[written] = target[written - distance];
    }

    return written;
  }

  private static int _ReadCount(ReadOnlySpan<byte> data, ref int at, int count) {
    if (count != 15)
      return count;

    byte next;
    do {
      if (at >= data.Length)
        throw new InvalidDataException("An LZ4 length runs past the end of its block.");

      next = data[at++];
      count = checked(count + next);
    } while (next == 255);

    return count;
  }
}
