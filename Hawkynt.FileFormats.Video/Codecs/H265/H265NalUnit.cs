using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.H265;

/// <summary>The kinds of NAL unit — ITU-T H.265, Table 7-1.</summary>
/// <remarks>
/// HEVC gives the type six bits where H.264 gave it five, and spends the extra room on saying what a
/// picture is <em>for</em> rather than only what it contains. The low bit of every non-IRAP type says
/// whether the picture is a reference (odd) or may be dropped without consequence (even); the
/// sixteen types from <see cref="BlaWithLeadingPictures"/> up are the random access points, ordered so
/// that "is this a point decoding may begin at" is a range test.
/// </remarks>
internal enum H265NalUnitType {

  /// <summary>A trailing picture no later picture refers to.</summary>
  TrailingNonReference = 0,

  /// <summary>A trailing picture later pictures may refer to.</summary>
  TrailingReference = 1,

  TemporalSubLayerAccessNonReference = 2,

  TemporalSubLayerAccessReference = 3,

  StepwiseTemporalSubLayerAccessNonReference = 4,

  StepwiseTemporalSubLayerAccessReference = 5,

  /// <summary>A leading picture that is decodable when its random access point is entered at.</summary>
  RandomAccessDecodableLeadingNonReference = 6,

  RandomAccessDecodableLeadingReference = 7,

  /// <summary>A leading picture that is <em>not</em> decodable when its access point is entered at.</summary>
  RandomAccessSkippedLeadingNonReference = 8,

  RandomAccessSkippedLeadingReference = 9,

  /// <summary>A broken link access picture: an access point spliced in, whose leading pictures are lost.</summary>
  BlaWithLeadingPictures = 16,

  BlaWithRandomAccessDecodableLeading = 17,

  BlaWithNoLeadingPictures = 18,

  /// <summary>An instantaneous decoding refresh picture that may be followed by decodable leading pictures.</summary>
  IdrWithRandomAccessDecodableLeading = 19,

  /// <summary>An instantaneous decoding refresh picture with no leading pictures at all.</summary>
  IdrWithNoLeadingPictures = 20,

  /// <summary>A clean random access picture: an intra picture later pictures may still refer past.</summary>
  CleanRandomAccess = 21,

  ReservedIrap22 = 22,

  ReservedIrap23 = 23,

  VideoParameterSet = 32,

  SequenceParameterSet = 33,

  PictureParameterSet = 34,

  AccessUnitDelimiter = 35,

  EndOfSequence = 36,

  EndOfBitstream = 37,

  FillerData = 38,

  PrefixSupplementalEnhancementInformation = 39,

  SuffixSupplementalEnhancementInformation = 40,
}

/// <summary>
/// One NAL unit: its two header fields and its payload with the emulation prevention removed.
/// </summary>
/// <remarks>
/// The escape positions are kept because HEVC needs them and H.264 did not. A slice segment may be
/// cut into substreams — one per tile, or one per row of coding tree blocks when the entropy coder
/// is synchronised across rows — and the header states each substream's length in bytes counted
/// <em>before</em> unescaping (clause 7.4.7.1). Every other part of the decoder works on the
/// unescaped payload, so the offsets have to be translated, and the only place that knows where the
/// escapes were is the unescaper.
/// </remarks>
internal sealed class H265NalUnit {

  internal H265NalUnit(H265NalUnitType type, int layerId, int temporalId, byte[] payload, int[] escapeRemovals) {
    this.Type = type;
    this.LayerId = layerId;
    this.TemporalId = temporalId;
    this.Payload = payload;
    this._escapeRemovals = escapeRemovals;
  }

  /// <summary>
  /// The unescaped positions a byte was removed before: after the j-th entry's index, the escaped
  /// payload holds one byte more than the unescaped one.
  /// </summary>
  private readonly int[] _escapeRemovals;

  internal H265NalUnitType Type { get; }

  /// <summary><c>nuh_layer_id</c> — non-zero only in the scalable and multiview extensions.</summary>
  internal int LayerId { get; }

  /// <summary><c>nuh_temporal_id_plus1 − 1</c>: which temporal sub-layer this unit belongs to.</summary>
  internal int TemporalId { get; }

  /// <summary>The raw byte sequence payload, with every emulation prevention byte removed.</summary>
  internal byte[] Payload { get; }

  /// <summary>Whether this unit carries a coded slice segment (Table 7-1: types 0 to 31).</summary>
  internal bool IsSlice => (int)this.Type <= 31;

  /// <summary>
  /// Whether this is an intra random access point picture — a picture decoding may begin at
  /// (clause 3.73: types <see cref="BlaWithLeadingPictures"/> through <see cref="ReservedIrap23"/>).
  /// </summary>
  internal bool IsRandomAccessPoint => (int)this.Type is >= 16 and <= 23;

  /// <summary>Whether this is an instantaneous decoding refresh picture, which empties the buffer.</summary>
  internal bool IsInstantaneousRefresh
    => this.Type is H265NalUnitType.IdrWithRandomAccessDecodableLeading or H265NalUnitType.IdrWithNoLeadingPictures;

  /// <summary>Whether this is a broken link access picture, whose references are known to be missing.</summary>
  internal bool IsBrokenLinkAccess => (int)this.Type is >= 16 and <= 18;

  /// <summary>Whether a picture of this type is kept as a reference — the low bit, for the non-IRAP types.</summary>
  internal bool IsSubLayerReference => (int)this.Type > 15 || ((int)this.Type & 1) != 0;

  /// <summary>
  /// Where an offset counted in escaped bytes lands in the unescaped payload.
  /// </summary>
  /// <remarks>
  /// The j-th removed byte sits at escaped position <c>_escapeRemovals[j] + j</c>, because every
  /// removal before it has already pushed it along by one. So the answer is the escaped offset less
  /// the number of removals that occurred before it.
  /// </remarks>
  internal int UnescapedOffsetOf(int escapedOffset) {
    var removed = 0;
    while (removed < this._escapeRemovals.Length && this._escapeRemovals[removed] + removed < escapedOffset)
      ++removed;

    return escapedOffset - removed;
  }

  /// <summary>Where an offset into the unescaped payload sits in the escaped one.</summary>
  internal int EscapedOffsetOf(int unescapedOffset) {
    var removed = 0;
    while (removed < this._escapeRemovals.Length && this._escapeRemovals[removed] <= unescapedOffset)
      ++removed;

    return unescapedOffset + removed;
  }
}

/// <summary>
/// Cuts a coded stream into NAL units, in both the forms a container hands them over in.
/// </summary>
/// <remarks>
/// A byte stream — Annex B, which is what a transport stream, a program stream, a bare
/// <c>.265</c> file and a decoder fed over a socket all carry — separates units with start codes.
/// MP4, Matroska and the ISO base media family put each unit behind its length instead, with the
/// parameter sets out of band in an <c>HEVCDecoderConfigurationRecord</c>. Which form a stream is in
/// is decided once, from whether that record was present, and never guessed at per packet: a
/// three-byte length beginning <c>00 00 01</c> is an ordinary 256-byte NAL unit, and a guess would
/// read it as a start code.
/// </remarks>
internal static class H265NalReader {

  /// <summary>Walks the NAL units of an Annex B byte stream (clause B.2).</summary>
  internal static IReadOnlyList<H265NalUnit> SplitAnnexB(ReadOnlyMemory<byte> data) {
    var units = new List<H265NalUnit>();
    var span = data.Span;

    if (_NextStartCode(span, 0, out var start) < 0)
      return units;

    while (start >= 0) {
      var next = _NextStartCode(span, start, out var following);
      var end = next < 0 ? span.Length : next;

      // Back off the trailing_zero_8bits, which sit between one unit and the next start code.
      while (end > start && span[end - 1] == 0)
        --end;

      if (end > start)
        units.Add(Parse(span[start..end]));

      start = following;
    }

    return units;
  }

  /// <summary>
  /// Walks the NAL units of a length-prefixed sample, as MP4, Matroska and the ISO family carry them.
  /// </summary>
  /// <param name="lengthSize">The bytes each length occupies, from the configuration record: 1, 2 or 4.</param>
  internal static IReadOnlyList<H265NalUnit> SplitLengthPrefixed(ReadOnlyMemory<byte> data, int lengthSize) {
    var units = new List<H265NalUnit>();
    var span = data.Span;

    for (var at = 0; at + lengthSize <= span.Length;) {
      var length = 0;
      for (var i = 0; i < lengthSize; ++i)
        length = (length << 8) | span[at + i];

      at += lengthSize;

      if (length == 0)
        continue;

      if (at + length > span.Length)
        throw new InvalidDataException(
          $"An H.265 access unit states a NAL unit of {length} bytes at offset {at}, but only {span.Length - at} "
          + $"remain in the packet. The length prefix size ({lengthSize} byte(s)) taken from the "
          + "HEVCDecoderConfigurationRecord does not match how this stream is written.");

      units.Add(Parse(span.Slice(at, length)));
      at += length;
    }

    return units;
  }

  /// <summary>Whether a packet looks like the Annex B byte stream, judged by a start code at its front.</summary>
  internal static bool LooksLikeAnnexB(ReadOnlySpan<byte> data)
    => data.Length >= 5
       && data[0] == 0
       && data[1] == 0
       && (data[2] == 1 || (data[2] == 0 && data[3] == 1));

  /// <summary>Reads the two-byte NAL unit header and unescapes the rest (clauses 7.3.1.2 and 7.4.2.2).</summary>
  internal static H265NalUnit Parse(ReadOnlySpan<byte> unit) {
    if (unit.Length < 2)
      throw new InvalidDataException(
        $"An H.265 NAL unit is {unit.Length} byte(s) long, too short for its two-byte header (clause 7.3.1.2).");

    if ((unit[0] & 0x80) != 0)
      throw new InvalidDataException(
        $"An H.265 NAL unit header has its forbidden_zero_bit set (first byte 0x{unit[0]:X2}). Clause 7.4.2.2 "
        + "requires it to be zero, so these bytes are not a NAL unit — the packet boundaries are wrong or the data "
        + "is corrupt.");

    var type = (H265NalUnitType)((unit[0] >> 1) & 0x3F);
    var layerId = ((unit[0] & 1) << 5) | (unit[1] >> 3);
    var temporalIdPlus1 = unit[1] & 7;

    if (temporalIdPlus1 == 0)
      throw new InvalidDataException(
        "An H.265 NAL unit header states nuh_temporal_id_plus1 of zero, which clause 7.4.2.2 forbids. These bytes "
        + "are not a NAL unit header.");

    var payload = _Unescape(unit[2..], out var removals);
    return new(type, layerId, temporalIdPlus1 - 1, payload, removals);
  }

  /// <summary>
  /// Removes the emulation prevention bytes: every <c>03</c> that follows two zeroes (clause 7.3.1.1).
  /// </summary>
  /// <remarks>
  /// The escape is applied by the encoder whether or not the unit will ever travel in a byte stream,
  /// so it is removed here for both delivery forms. An MP4's NAL units are escaped exactly as a
  /// transport stream's are, and reading one without unescaping puts a stray byte into the middle of
  /// a slice.
  /// <para/>
  /// <paramref name="removals"/> answers where they were, in unescaped coordinates: an entry
  /// <c>r</c> means a byte was dropped immediately before unescaped position <c>r</c>. Nothing but
  /// the entry point offsets needs this, and they need it exactly.
  /// </remarks>
  private static byte[] _Unescape(ReadOnlySpan<byte> payload, out int[] removals) {
    var count = 0;
    for (var i = 0; i + 2 < payload.Length; ++i)
      if (payload[i] == 0 && payload[i + 1] == 0 && payload[i + 2] == 3) {
        ++count;
        i += 2;
      }

    if (count == 0) {
      removals = [];
      return payload.ToArray();
    }

    var rbsp = new byte[payload.Length - count];
    removals = new int[count];
    var target = 0;
    var removed = 0;

    for (var i = 0; i < payload.Length; ++i) {
      if (i + 2 < payload.Length && payload[i] == 0 && payload[i + 1] == 0 && payload[i + 2] == 3) {
        rbsp[target++] = 0;
        rbsp[target++] = 0;
        removals[removed++] = target;
        i += 2;
        continue;
      }

      rbsp[target++] = payload[i];
    }

    return rbsp;
  }

  /// <summary>Finds the next start code at or after <paramref name="from"/>, and where the unit after it begins.</summary>
  private static int _NextStartCode(ReadOnlySpan<byte> data, int from, out int payloadStart) {
    for (var at = from; at + 2 < data.Length; ++at) {
      if (data[at] != 0 || data[at + 1] != 0 || data[at + 2] != 1)
        continue;

      payloadStart = at + 3;
      return at;
    }

    payloadStart = -1;
    return -1;
  }
}
