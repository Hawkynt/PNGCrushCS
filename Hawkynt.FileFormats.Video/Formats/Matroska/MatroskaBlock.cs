using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Matroska;

/// <summary>The fixed part every block begins with.</summary>
/// <param name="TrackNumber">Which track the frames belong to.</param>
/// <param name="RelativeTimestamp">How far the block sits from its cluster's timestamp, in the
/// segment's ticks. Signed and sixteen bits wide, so a block may legitimately precede its own
/// cluster.</param>
/// <param name="Flags">The flag byte, whose meaning differs slightly between a <c>SimpleBlock</c> and
/// a <c>Block</c>.</param>
/// <param name="PayloadOffset">Where the frames begin, counted from the start of the block.</param>
internal readonly record struct MatroskaBlockHeader(
  ulong TrackNumber,
  int RelativeTimestamp,
  byte Flags,
  int PayloadOffset) {

  /// <summary>Whether decoding may begin at this block, which only a <c>SimpleBlock</c> states.</summary>
  /// <remarks>
  /// A <c>Block</c> inside a <c>BlockGroup</c> has no such flag: what makes it a keyframe is that the
  /// group carries no <c>ReferenceBlock</c>, which is decided a level up.
  /// </remarks>
  internal bool IsKeyFrame => (this.Flags & 0x80) != 0;

  /// <summary>Which of the four lacings the frames of this block are packed with.</summary>
  internal MatroskaLacing Lacing => (MatroskaLacing)((this.Flags >> 1) & 3);
}

/// <summary>How a block packs more than one frame into itself.</summary>
/// <remarks>
/// Lacing exists because a block costs a header and some codecs produce frames far smaller than one
/// — a Vorbis packet may be a few dozen bytes, and a header per packet would be a measurable part of
/// the file. A reader that ignored it would report one packet where the file holds several, with the
/// frames of the later ones stuck to the end of the first.
/// </remarks>
internal enum MatroskaLacing {

  /// <summary>One frame, and the payload is it.</summary>
  None = 0,

  /// <summary>Sizes as runs of 255 followed by a remainder, the way Ogg codes its segment table.</summary>
  Xiph = 1,

  /// <summary>No sizes at all: the frames are equal, so the payload divides by their number.</summary>
  Fixed = 2,

  /// <summary>The first size as a variable-length integer, the rest as signed differences from it.</summary>
  Ebml = 3,
}

/// <summary>
/// Takes a <c>SimpleBlock</c> or a <c>Block</c> apart into the frames it holds.
/// </summary>
/// <remarks>
/// The header is three fields and none of them are fixed width in the way that phrase usually means:
/// the track number is a variable-length integer, the timestamp is a signed sixteen-bit offset from
/// the cluster's own, and the flags are one byte whose middle two bits say how the rest of the block
/// is divided. The timestamp being an offset rather than an absolute is what keeps a block small, and
/// its being signed is not a formality — a block may be stored in a cluster whose timestamp is later
/// than the block's own, and reading it unsigned would put that frame some sixty-five seconds into
/// the future rather than a moment into the past.
/// <para/>
/// Nothing here decodes and nothing here guesses. A lace whose stated sizes do not add up to the
/// bytes that are there is refused by name rather than clamped: a frame cut short is not a frame, and
/// handing one back as though it were is how a demuxer turns a broken file into a broken picture
/// nobody can trace.
/// </remarks>
internal static class MatroskaBlock {

  /// <summary>Reads the fixed part, or fails when the block is too short to hold one.</summary>
  internal static bool TryReadHeader(ReadOnlySpan<byte> block, out MatroskaBlockHeader header) {
    header = default;

    var read = _ReadUnsignedVint(block, 0, out var track);
    if (read == 0 || block.Length < read + 3)
      return false;

    var relative = (short)((block[read] << 8) | block[read + 1]);
    header = new(track, relative, block[read + 2], read + 3);
    return true;
  }

  /// <summary>
  /// Finds where each frame of a block starts and how long it is, in storage order.
  /// </summary>
  /// <param name="block">The whole block, header included.</param>
  /// <param name="header">What <see cref="TryReadHeader"/> read out of it.</param>
  /// <param name="frames">Filled with one entry per frame, as offsets into <paramref name="block"/>.</param>
  /// <exception cref="InvalidDataException">The lace does not describe the bytes that are there.</exception>
  internal static void ReadFrames(ReadOnlySpan<byte> block, MatroskaBlockHeader header, List<(int Offset, int Length)> frames) {
    frames.Clear();

    var payload = header.PayloadOffset;
    var remaining = block.Length - payload;
    if (remaining < 0)
      throw new InvalidDataException($"A block of track {header.TrackNumber} states a header longer than the block itself.");

    if (header.Lacing == MatroskaLacing.None) {
      frames.Add((payload, remaining));
      return;
    }

    if (remaining < 1)
      throw new InvalidDataException($"A laced block of track {header.TrackNumber} carries no frame count.");

    // The count is stored one less than it is, because a lace of no frames cannot occur and the
    // byte would otherwise waste its zero.
    var laces = block[payload] + 1;
    ++payload;
    --remaining;

    switch (header.Lacing) {
      case MatroskaLacing.Fixed:
        _ReadFixed(header, laces, payload, remaining, frames);
        return;
      case MatroskaLacing.Xiph:
        _ReadXiph(block, header, laces, ref payload, ref remaining, frames);
        break;
      case MatroskaLacing.Ebml:
        _ReadEbml(block, header, laces, ref payload, ref remaining, frames);
        break;
      default:
        throw new InvalidDataException($"A block of track {header.TrackNumber} states a lacing this format has no fourth value for.");
    }

    // Whichever of the two size tables was read, it describes every frame but the last, which takes
    // what is left. That is what makes the tables short and what makes a wrong one fatal here rather
    // than silently absorbed: an over-long table leaves the last frame negative.
    if (remaining < 0)
      throw new InvalidDataException(
        $"A laced block of track {header.TrackNumber} states frame sizes totalling more than the {block.Length - header.PayloadOffset} bytes it holds.");

    frames.Add((payload, remaining));
  }

  /// <summary>Fixed lacing: no sizes are stored at all, so the payload has to divide by the count.</summary>
  private static void _ReadFixed(MatroskaBlockHeader header, int laces, int payload, int remaining, List<(int Offset, int Length)> frames) {
    if (remaining % laces != 0)
      throw new InvalidDataException(
        $"A fixed-lace block of track {header.TrackNumber} holds {remaining} bytes, which does not divide into {laces} frames of equal length.");

    var each = remaining / laces;
    for (var i = 0; i < laces; ++i)
      frames.Add((payload + (i * each), each));
  }

  /// <summary>Xiph lacing: each size is a run of 255s ended by a byte below 255, and they add up.</summary>
  private static void _ReadXiph(
    ReadOnlySpan<byte> block, MatroskaBlockHeader header, int laces,
    ref int payload, ref int remaining, List<(int Offset, int Length)> frames) {
    var sizes = new int[laces - 1];
    for (var i = 0; i < laces - 1; ++i) {
      var size = 0;
      while (true) {
        if (remaining < 1)
          throw new InvalidDataException($"A Xiph-laced block of track {header.TrackNumber} ends in the middle of its size table.");

        var part = block[payload];
        ++payload;
        --remaining;
        size += part;
        if (part != 0xFF)
          break;
      }

      sizes[i] = size;
    }

    _Place(header, sizes, ref payload, ref remaining, frames);
  }

  /// <summary>
  /// EBML lacing: the first size is a plain variable-length integer and the rest are differences.
  /// </summary>
  /// <remarks>
  /// The differences are signed, and signed in EBML's own way — the value is stored biased by half
  /// its range so that the bit pattern stays a normal variable-length integer. Frames of a lace are
  /// usually near enough the same length that a difference fits in one byte where a size would need
  /// two, which is the whole reason this lacing exists beside Xiph's.
  /// </remarks>
  private static void _ReadEbml(
    ReadOnlySpan<byte> block, MatroskaBlockHeader header, int laces,
    ref int payload, ref int remaining, List<(int Offset, int Length)> frames) {
    var sizes = new int[laces - 1];
    if (laces > 1) {
      var read = _ReadUnsignedVint(block, payload, out var first);
      if (read == 0 || first > int.MaxValue)
        throw new InvalidDataException($"An EBML-laced block of track {header.TrackNumber} states an unreadable first frame size.");

      payload += read;
      remaining -= read;
      sizes[0] = (int)first;

      for (var i = 1; i < laces - 1; ++i) {
        read = _ReadSignedVint(block, payload, out var difference);
        if (read == 0)
          throw new InvalidDataException($"An EBML-laced block of track {header.TrackNumber} ends in the middle of its size table.");

        payload += read;
        remaining -= read;

        var size = sizes[i - 1] + difference;
        if (size < 0 || size > int.MaxValue)
          throw new InvalidDataException($"An EBML-laced block of track {header.TrackNumber} states a frame of {size} bytes.");

        sizes[i] = (int)size;
      }
    }

    _Place(header, sizes, ref payload, ref remaining, frames);
  }

  /// <summary>Turns a table of sizes into frame positions, refusing one that overruns the block.</summary>
  private static void _Place(
    MatroskaBlockHeader header, int[] sizes, ref int payload, ref int remaining, List<(int Offset, int Length)> frames) {
    foreach (var size in sizes) {
      if (size < 0 || size > remaining)
        throw new InvalidDataException(
          $"A laced block of track {header.TrackNumber} states a frame of {size} bytes where {remaining} are left.");

      frames.Add((payload, size));
      payload += size;
      remaining -= size;
    }
  }

  /// <summary>Reads a variable-length integer with its marker bit dropped, as a block's sizes are.</summary>
  private static int _ReadUnsignedVint(ReadOnlySpan<byte> data, int offset, out ulong value) {
    value = 0;
    if (offset >= data.Length)
      return 0;

    var first = data[offset];
    if (first == 0)
      return 0;

    var length = 1;
    var mask = 0x80;
    for (; (first & mask) == 0; mask >>= 1)
      ++length;

    if (length > EbmlScanner.MAX_SIZE_LENGTH || offset + length > data.Length)
      return 0;

    var result = (ulong)(first & (mask - 1));
    for (var i = 1; i < length; ++i)
      result = (result << 8) | data[offset + i];

    value = result;
    return length;
  }

  /// <summary>Reads a variable-length integer biased into a signed one, as EBML lacing stores its differences.</summary>
  private static int _ReadSignedVint(ReadOnlySpan<byte> data, int offset, out long value) {
    value = 0;

    var read = _ReadUnsignedVint(data, offset, out var raw);
    if (read == 0)
      return 0;

    // The bias is half the range the field can hold: seven bits per byte, so one byte spans -63..63
    // and two span -8191..8191.
    value = (long)raw - ((1L << ((7 * read) - 1)) - 1);
    return read;
  }
}
