using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Codecs.QuickTimeRle;

/// <summary>
/// Finds the colour table inside the sample description a container carried across as codec private
/// data.
/// </summary>
/// <remarks>
/// The fixed fields of a visual sample entry — the picture size, the depth — are read by whichever
/// container the entry came out of, because they are at the same places in every one of them
/// whatever the codec is. What follows the depth is not: a colour table is there only for a codec
/// whose samples are indices, and only that codec knows to look for one. So the container states the
/// depth and this states the table, which is the same seam that keeps a demuxer from having to know
/// what an Animation stream is.
/// <para/>
/// The entry arrives whole, box header and all, because that is what a sample description is; the
/// header is four bytes of length and four of the codec's code, or sixteen when the length is the
/// escape value and a sixty-four bit length follows.
/// </remarks>
internal static class QuickTimeRleSampleDescription {

  /// <summary>Where the depth sits, counted from the first byte of the entry's body.</summary>
  private const int _DEPTH_AT = 74;

  /// <summary>Where the colour table identifier sits, immediately after the depth.</summary>
  private const int _COLOUR_TABLE_ID_AT = 76;

  /// <summary>Where a table, when there is one, begins.</summary>
  private const int _COLOUR_TABLE_AT = 78;

  /// <summary>The identifier a description states when it carries no table of its own.</summary>
  private const ushort _NO_COLOUR_TABLE = 0xFFFF;

  /// <summary>The length field's escape value, which means a sixty-four bit length follows it.</summary>
  private const uint _EXTENDED_LENGTH = 1;

  /// <summary>
  /// The colour table the description carries, or an empty span when it carries none.
  /// </summary>
  /// <remarks>
  /// Empty for a description that is too short to reach the field as well as for one that says there
  /// is no table. Both mean the same thing to a caller — there is nothing here to draw indices
  /// through — and a direct-colour stream reaches neither, since it never asks.
  /// </remarks>
  internal static ReadOnlySpan<byte> ColourTable(ReadOnlySpan<byte> sampleDescription) {
    var body = _Body(sampleDescription);
    if (body.Length < _COLOUR_TABLE_AT)
      return default;

    if (BinaryPrimitives.ReadUInt16BigEndian(body.Slice(_COLOUR_TABLE_ID_AT, 2)) == _NO_COLOUR_TABLE)
      return default;

    return body[_COLOUR_TABLE_AT..];
  }

  /// <summary>
  /// The depth the description states, or zero when it is too short to state one.
  /// </summary>
  /// <remarks>
  /// A second reading of a field the container has already read, and used only where the container
  /// left it at zero — a container that carries sample descriptions but describes streams without a
  /// depth field of its own. Where both speak they agree, because they are reading the same two bytes.
  /// </remarks>
  internal static int Depth(ReadOnlySpan<byte> sampleDescription) {
    var body = _Body(sampleDescription);
    return body.Length < _DEPTH_AT + 2 ? 0 : BinaryPrimitives.ReadUInt16BigEndian(body.Slice(_DEPTH_AT, 2));
  }

  /// <summary>The entry with its box header taken off.</summary>
  private static ReadOnlySpan<byte> _Body(ReadOnlySpan<byte> sampleDescription) {
    if (sampleDescription.Length < 8)
      return default;

    var header = BinaryPrimitives.ReadUInt32BigEndian(sampleDescription) == _EXTENDED_LENGTH ? 16 : 8;
    return sampleDescription.Length < header ? default : sampleDescription[header..];
  }

  /// <summary>Refuses a description that is present but cannot be a visual sample entry.</summary>
  internal static void RefuseUnreadable(ReadOnlySpan<byte> sampleDescription, int streamIndex) {
    if (sampleDescription.IsEmpty || _Body(sampleDescription).Length >= _DEPTH_AT + 2)
      return;

    throw new InvalidDataException(
      $"Video stream {streamIndex} carries {sampleDescription.Length} bytes of sample description, which is too short for the visual sample entry an Animation stream is described by.");
  }
}
