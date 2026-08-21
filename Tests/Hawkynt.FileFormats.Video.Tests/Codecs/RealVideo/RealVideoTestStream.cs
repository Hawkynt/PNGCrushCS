using System;
using FileFormat.Codecs.H263.Tests;
using FileFormat.Core;

namespace FileFormat.Codecs.RealVideo.Tests;

/// <summary>
/// Writes RealVideo 1 pictures a bit at a time, over the H.263 macroblock layer they share.
/// </summary>
/// <remarks>
/// Only the picture header is written here. Everything below it is H.263's, so the macroblocks come
/// from <see cref="H263TestStream"/> — writing them again would be a second transcription of the same
/// tables, and two transcriptions of one table go wrong together.
/// <para/>
/// It exists for the paths ffmpeg's own RealVideo 1 encoder cannot reach. That encoder refuses to
/// emit more than one slice — "Multiple slices are not supported by this codec" — so a picture cut
/// into runs, which is the shape every recording in the wild has and the shape the offsets on a packet
/// exist for, cannot be produced with it at all. Nor can a header stating a quantiser of zero, a
/// PB-frame, or a run that runs off the end of the picture; each of those is a refusal, and by
/// definition no valid stream produces one.
/// </remarks>
internal static class RealVideoTestStream {

  /// <summary>The private data a stream states to name the revision this decoder implements.</summary>
  internal static byte[] Revision0 => [0, 0, 0, 8, 0x10, 0, 0, 0];

  /// <summary>Private data naming a revision this decoder refuses.</summary>
  internal static byte[] Revision(int minor) => [0, 0, 0, 8, 0x10, 0, (byte)(minor << 4), 0];

  /// <summary>
  /// Opens a RealVideo 1 picture header and leaves the stream at the macroblock layer.
  /// </summary>
  /// <param name="isIntra">Whether the picture is coded without reference to another.</param>
  /// <param name="quantiser">The step size, 1 to 31, or zero to write the value that is refused.</param>
  /// <param name="position">The macroblock the run begins at and how many it carries, or <c>null</c>
  /// to leave the fields out — which a picture sent in one run may do, and then the run is the whole
  /// picture.</param>
  /// <param name="isPbFrame">Whether to set the bit a PB-frame is signalled by.</param>
  internal static H263TestStream Picture(
    bool isIntra, int quantiser, (int Column, int Row, int Count)? position = null, bool isPbFrame = false) {
    var stream = new H263TestStream();
    stream.Bits(1, 1);                          // the marker, set in every header of every stream
    stream.Bits(isIntra ? 0 : 1, 1);            // picture type
    stream.Bits(isPbFrame ? 1 : 0, 1);
    stream.Bits(quantiser, 5);

    if (position == null) {
      // Something non-zero where the position would be, which is what a stream that leaves the fields
      // out looks like — and which this decoder refuses rather than guessing at.
      stream.Bits(0xFFF, 12);
      return stream;
    }

    var (column, row, count) = position.Value;
    stream.Bits(column, 6);
    stream.Bits(row, 6);
    stream.Bits(count, 12);
    stream.Bits(0, 3);                          // the three bits nothing here gives a meaning to
    return stream;
  }

  /// <summary>A stream description naming a RealVideo codec at a size.</summary>
  internal static MediaStreamInfo Stream(
    string fourCharacterCode, int width = 176, int height = 144, byte[]? codecPrivateData = null)
    => new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters(fourCharacterCode),
      Width = width,
      Height = height,
      TimeBase = new(1, 1000),
      CodecPrivateData = codecPrivateData ?? Revision0,
    };

  /// <summary>Joins several runs into the one packet a container hands over, with their offsets.</summary>
  internal static CodedPacket Packet(params byte[][] runs) {
    ArgumentNullException.ThrowIfNull(runs);

    var total = 0;
    var offsets = new int[runs.Length];
    for (var i = 0; i < runs.Length; ++i) {
      offsets[i] = total;
      total += runs[i].Length;
    }

    var joined = new byte[total];
    for (var i = 0; i < runs.Length; ++i)
      runs[i].CopyTo(joined, offsets[i]);

    return new(0, joined, 0, IsKeyFrame: true, FragmentOffsets: offsets);
  }
}
