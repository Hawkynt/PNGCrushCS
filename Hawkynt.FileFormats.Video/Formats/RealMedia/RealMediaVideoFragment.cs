using System;
using System.Buffers.Binary;

namespace FileFormat.RealMedia;

/// <summary>What one element of a video packet's payload turns out to be.</summary>
internal enum RealMediaFragmentKind {

  /// <summary>A piece of a frame, carrying where in the frame it goes.</summary>
  Piece,

  /// <summary>A whole frame occupying the rest of the packet.</summary>
  WholeFrame,

  /// <summary>A whole frame carrying its own length, so that more may follow it in the same packet.</summary>
  PackedFrame,
}

/// <summary>
/// One element of a video packet's payload: its sub-header read, and its bytes located.
/// </summary>
/// <param name="Kind">What this element is.</param>
/// <param name="FrameLength">The whole frame's length in bytes.</param>
/// <param name="Offset">Where this element's bytes go within the frame.</param>
/// <param name="DataOffset">Where this element's bytes begin, counted from the file's start.</param>
/// <param name="DataLength">How many bytes this element carries.</param>
/// <param name="End">Where the element ends, which is where the next one begins.</param>
internal readonly record struct RealMediaVideoFragment(
  RealMediaFragmentKind Kind,
  int FrameLength,
  int Offset,
  int DataOffset,
  int DataLength,
  int End);

/// <summary>
/// Reads the sub-header a RealMedia video payload puts in front of each of its elements.
/// </summary>
/// <remarks>
/// A RealMedia packet is capped at a size the writer chose, and a coded frame is not, so a video
/// payload is a sequence of elements each introduced by a small header of its own. The top two bits
/// of that header's first byte say which of four things the element is; the rest of the byte carries
/// nothing this reader needs.
/// <para/>
/// The four kinds and their fields were derived from the files themselves rather than from any
/// published description of the layout, and checked by taking every video frame out of nine
/// recordings — RealVideo 1, 2, 3 and 4, twenty-one thousand frames — and finding the same count, the
/// same byte lengths, the same timestamps and the same key-frame flags that ffmpeg's demuxer reports
/// for the same files. The two numbers each piece carries are the interesting part: for every piece
/// but the last they are the whole frame's length and this piece's offset within it, and for the last
/// they are the whole frame's length and this piece's <em>own</em> length — the offset being implied
/// by subtraction. A reader that took the second number as an offset in both cases assembles every
/// frame of every file wrongly by exactly the length of its final piece.
/// <para/>
/// The numbers themselves are written in a form that spends two bytes on values that fit and four on
/// values that do not, which is why they cannot simply be read at fixed offsets.
/// </remarks>
internal static class RealMediaVideoFragmentReader {

  /// <summary>The top two bits of the first byte, which say what the element is.</summary>
  private const int _KIND_SHIFT = 6;

  private const int _PIECE = 0;
  private const int _WHOLE_FRAME = 1;
  private const int _LAST_PIECE = 2;
  private const int _PACKED_FRAME = 3;

  /// <summary>The bit that says a stored number is the short form, and the bias carried with it.</summary>
  private const int _SHORT_FORM = 0x4000;

  /// <summary>
  /// Reads one element of a video payload.
  /// </summary>
  /// <param name="data">The file.</param>
  /// <param name="at">Where the element's sub-header begins.</param>
  /// <param name="end">Where the payload ends.</param>
  /// <param name="fragment">The element, when one could be read.</param>
  /// <returns><c>false</c> when the bytes for a whole element are not there.</returns>
  internal static bool TryRead(ReadOnlySpan<byte> data, int at, int end, out RealMediaVideoFragment fragment) {
    fragment = default;
    if (at + 2 > end)
      return false;

    var kind = data[at] >> _KIND_SHIFT;
    var cursor = at + 1;

    switch (kind) {
      // A whole frame filling the rest of the packet. Its length is the packet's rather than its own,
      // so a packet the file was cut short in the middle of loses it — there is no field saying how
      // long it was meant to be.
      case _WHOLE_FRAME: {
        ++cursor; // the sequence number, which says nothing about where the bytes are
        if (cursor > end)
          return false;

        fragment = new(RealMediaFragmentKind.WholeFrame, end - cursor, 0, cursor, end - cursor, end);
        return true;
      }

      // A whole frame carrying its own length, so that another element may follow it.
      case _PACKED_FRAME: {
        if (!_TryReadNumber(data, ref cursor, end, out var length)
            || !_TryReadNumber(data, ref cursor, end, out _)
            || cursor + 1 > end)
          return false;

        ++cursor; // the picture number
        if (length < 0 || cursor + length > end)
          return false;

        fragment = new(RealMediaFragmentKind.PackedFrame, length, 0, cursor, length, cursor + length);
        return true;
      }

      case _PIECE:
      case _LAST_PIECE: {
        ++cursor; // the sequence number
        if (cursor > end)
          return false;

        if (!_TryReadNumber(data, ref cursor, end, out var frameLength)
            || !_TryReadNumber(data, ref cursor, end, out var second)
            || cursor + 1 > end)
          return false;

        ++cursor; // the picture number
        if (frameLength <= 0 || second < 0)
          return false;

        var available = end - cursor;

        // For every piece but the last, the second number is where this piece goes and the piece runs
        // to the end of the packet. For the last it is the piece's own length, and where it goes
        // follows from the frame's length.
        var (offset, length) = kind == _LAST_PIECE
          ? (frameLength - second, second)
          : (second, available);

        if (offset < 0 || length < 0 || length > available || offset > frameLength - length)
          return false;

        fragment = new(RealMediaFragmentKind.Piece, frameLength, offset, cursor, length, cursor + length);
        return true;
      }

      default:
        return false;
    }
  }

  /// <summary>
  /// Reads one of the two numbers a piece's sub-header carries.
  /// </summary>
  /// <remarks>
  /// Two bytes when the value fits in the fourteen bits left after the form bit, and four when it does
  /// not — a frame longer than sixteen kilobytes, which the larger pictures reach. Reading the short
  /// form as though it were the long one swallows the two bytes after it, and every field from there
  /// on is somebody else's.
  /// </remarks>
  private static bool _TryReadNumber(ReadOnlySpan<byte> data, ref int cursor, int end, out int value) {
    value = 0;
    if (cursor + 2 > end)
      return false;

    var first = BinaryPrimitives.ReadUInt16BigEndian(data[cursor..]) & 0x7FFF;
    cursor += 2;

    if (first >= _SHORT_FORM) {
      value = first - _SHORT_FORM;
      return true;
    }

    if (cursor + 2 > end)
      return false;

    value = (first << 16) | BinaryPrimitives.ReadUInt16BigEndian(data[cursor..]);
    cursor += 2;
    return true;
  }
}
