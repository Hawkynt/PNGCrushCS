using System;
using System.Buffers.Binary;

namespace FileFormat.Codecs.RealVideo;

/// <summary>Which of the four RealVideo bitstreams a stream is coded with.</summary>
internal enum RealVideoGeneration {

  /// <summary>Something the leading byte of the version word does not name.</summary>
  Unknown = 0,

  /// <summary>RealVideo 1 and 1.3, which a container names <c>RV10</c> or <c>RV13</c>.</summary>
  RealVideo10 = 1,

  /// <summary>RealVideo 2, which a container names <c>RV20</c>.</summary>
  RealVideo20 = 2,

  /// <summary>RealVideo 3, which a container names <c>RV30</c>.</summary>
  RealVideo30 = 3,

  /// <summary>RealVideo 4, which a container names <c>RV40</c>.</summary>
  RealVideo40 = 4,
}

/// <summary>
/// What a RealVideo stream's codec-private data says about how its pictures are coded.
/// </summary>
/// <remarks>
/// A RealMedia file describes a video stream with a fixed part the container reads — the picture size,
/// the depth, the frame rate — and a remainder that belongs to the codec. That remainder's second
/// four bytes are a version word, and it is the only place the bitstream's own version appears: the
/// four-character code says <c>RV10</c> for two bitstreams that differ, and a decoder that went by the
/// code alone would read one of them with the other's rules.
/// <para/>
/// The word's leading byte names the generation and matches the code, which is what makes it safe to
/// use — a stream whose code and version word disagree is one thing claiming to be another, and is
/// refused rather than decoded as either.
/// </remarks>
/// <param name="Generation">Which of the four bitstreams this is.</param>
/// <param name="Version">The whole version word, for a message that has to name it.</param>
/// <param name="Minor">The nibble below the generation, which names a revision of that generation's
/// bitstream.</param>
internal readonly record struct RealVideoBitstreamVersion(
  RealVideoGeneration Generation, uint Version, int Minor) {

  /// <summary>Where the version word sits in the codec-private data.</summary>
  private const int _VERSION_OFFSET = 4;

  /// <summary>
  /// Reads the version word out of a stream's codec-private data.
  /// </summary>
  /// <remarks>
  /// A stream whose private data is too short to hold the word gets the generation its four-character
  /// code names and the revision zero that every such stream has turned out to be. That is not much of
  /// a guess — the code and the word's leading byte carry the same fact — but it is a guess about the
  /// revision, and a stream that decoded wrongly because of it fails in its first picture rather than
  /// producing something plausible.
  /// </remarks>
  internal static RealVideoBitstreamVersion Read(ReadOnlySpan<byte> codecPrivateData, RealVideoGeneration fromTag) {
    if (codecPrivateData.Length < _VERSION_OFFSET + 4)
      return new(fromTag, 0, 0);

    var version = BinaryPrimitives.ReadUInt32BigEndian(codecPrivateData[_VERSION_OFFSET..]);
    var generation = (RealVideoGeneration)(version >> 28);

    // The word and the code have to agree. One saying RV20 in a stream the container calls RV10 is a
    // file whose two statements about itself contradict each other, and picking either would be this
    // decoder deciding which of them to believe.
    if (generation != fromTag)
      return new(RealVideoGeneration.Unknown, version, 0);

    return new(generation, version, (int)((version >> 12) & 0xF));
  }

  /// <summary>
  /// The revision of the RealVideo 1 bitstream whose macroblock layer is implemented here.
  /// </summary>
  /// <remarks>
  /// Revision zero, which is what a version word of 0x10000000 states and what ffmpeg's own RealVideo 1
  /// encoder writes. Its picture header is followed straight away by the macroblock position, and its
  /// macroblock layer is ITU-T H.263's exactly — every picture of every stream of it measured here
  /// reconstructs to ffmpeg's own planes sample for sample.
  /// <para/>
  /// The recordings on the sample servers state 0x10001000 and 0x10003001 instead, and those are a
  /// different bitstream below the picture header rather than the same one shifted: no offset into
  /// the picture decodes even three of their macroblocks with the H.263 tables, so the difference is
  /// not the header's length. What it is has not been worked out, and until it has, a stream stating
  /// one of them is refused by name rather than decoded into something that looks like a picture.
  /// </remarks>
  internal const int IMPLEMENTED_MINOR = 0;
}
