using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.H264;

/// <summary>The kinds of NAL unit this decoder acts on, by the codes of ITU-T H.264, Table 7-1.</summary>
internal enum H264NalUnitType {
  Unspecified = 0,
  NonIdrSlice = 1,
  SlicePartitionA = 2,
  SlicePartitionB = 3,
  SlicePartitionC = 4,
  IdrSlice = 5,
  SupplementalEnhancementInformation = 6,
  SequenceParameterSet = 7,
  PictureParameterSet = 8,
  AccessUnitDelimiter = 9,
  EndOfSequence = 10,
  EndOfStream = 11,
  FillerData = 12,
  SequenceParameterSetExtension = 13,
  PrefixNalUnit = 14,
  SubsetSequenceParameterSet = 15,
  AuxiliarySlice = 19,
  SliceExtension = 20,
  DepthOrThreeDimensionalSliceExtension = 21,
}

/// <summary>
/// One NAL unit: its two header fields and its payload with the emulation prevention taken out.
/// </summary>
/// <param name="RefIdc">
/// <c>nal_ref_idc</c> — zero for a unit no later picture refers to, which for a slice is what says
/// the picture it belongs to is not a reference and carries no reference picture marking.
/// </param>
/// <param name="Type">
/// <c>nal_unit_type</c>, which is what the unit is: a parameter set, a slice, or something to step
/// over.
/// </param>
/// <param name="Payload">The RBSP: the unit's bytes after the header, unescaped.</param>
internal readonly record struct H264NalUnit(int RefIdc, H264NalUnitType Type, byte[] Payload) {

  /// <summary>Whether this slice belongs to an IDR picture, which resets every reference (clause 7.4.1.2.4).</summary>
  internal bool IsIdr => this.Type == H264NalUnitType.IdrSlice;
}

/// <summary>
/// Cuts a stream of bytes into NAL units, in either of the two ways containers deliver them.
/// </summary>
/// <remarks>
/// H.264 is carried two ways and a decoder that reads only one of them reads only half the files.
/// A transport stream, a program stream and a bare <c>.264</c> carry the byte stream format of Annex
/// B, where units are separated by a three or four byte start code and any byte inside a unit that
/// would look like one has had a <c>03</c> stuffed into it. MP4, Matroska and FLV carry each unit
/// with its length in front instead, because a container that already knows where everything is has
/// no need to scan for it — the number of bytes in that length is in the
/// <c>AVCDecoderConfigurationRecord</c>.
/// <para/>
/// Which of the two a packet is in is not stated anywhere in the packet, so
/// <see cref="H264VideoDecoder"/> decides it once from the stream description and this type is told;
/// guessing per packet would eventually guess wrong on a length prefix that happens to begin with
/// two zero bytes.
/// </remarks>
internal static class H264NalReader {

  /// <summary>
  /// Walks the NAL units of an Annex B byte stream (H.264, Annex B).
  /// </summary>
  /// <remarks>
  /// Leading zero bytes before a start code and trailing zero bytes after a unit are both allowed and
  /// both belong to neither unit, so a unit is taken as everything between one start code and the
  /// next with the trailing zeroes trimmed off. Trimming matters: those bytes would otherwise move
  /// the <c>rbsp_stop_one_bit</c> and make <c>more_rbsp_data()</c> answer that a slice has another
  /// macroblock in it when it does not.
  /// </remarks>
  internal static IEnumerable<H264NalUnit> SplitAnnexB(ReadOnlyMemory<byte> data) {
    var units = new List<H264NalUnit>();
    var span = data.Span;
    var at = _NextStartCode(span, 0, out var start);
    if (at < 0)
      return units;

    while (start >= 0) {
      var next = _NextStartCode(span, start, out var following);
      var end = next < 0 ? span.Length : next;

      // Back off the trailing_zero_8bits, which sit between one unit and the next start code.
      while (end > start && span[end - 1] == 0)
        --end;

      if (end > start)
        units.Add(_Parse(span[start..end]));

      start = following;
    }

    return units;
  }

  /// <summary>
  /// Walks the NAL units of a length-prefixed sample, as MP4, Matroska and FLV carry them.
  /// </summary>
  /// <param name="lengthSize">
  /// The bytes each length occupies, from the configuration record: 1, 2 or 4.
  /// </param>
  internal static IEnumerable<H264NalUnit> SplitLengthPrefixed(ReadOnlyMemory<byte> data, int lengthSize) {
    var units = new List<H264NalUnit>();
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
          $"An H.264 access unit states a NAL unit of {length} bytes at offset {at}, but only "
          + $"{span.Length - at} remain in the packet. The length prefix size ({lengthSize} byte(s)) taken from the "
          + "AVCDecoderConfigurationRecord does not match how this stream is written.");

      units.Add(_Parse(span.Slice(at, length)));
      at += length;
    }

    return units;
  }

  /// <summary>
  /// Whether a packet looks like the Annex B byte stream, judged by a start code at its front.
  /// </summary>
  /// <remarks>
  /// Asked only where the container says nothing — a bare elementary stream, or an MP4 whose sample
  /// entry carried no configuration record. Where there is a configuration record the length prefix
  /// size comes from it and this is not consulted, because a three-byte length beginning
  /// <c>00 00 01</c> is a perfectly ordinary 256-byte NAL unit and would be read here as a start code.
  /// </remarks>
  internal static bool LooksLikeAnnexB(ReadOnlySpan<byte> data)
    => data.Length >= 4
       && data[0] == 0
       && data[1] == 0
       && (data[2] == 1 || (data[2] == 0 && data[3] == 1));

  /// <summary>Reads the two header fields and unescapes the rest (clauses 7.3.1 and 7.4.1).</summary>
  private static H264NalUnit _Parse(ReadOnlySpan<byte> unit) {
    var header = unit[0];
    if ((header & 0x80) != 0)
      throw new InvalidDataException(
        $"An H.264 NAL unit header has its forbidden_zero_bit set (first byte 0x{header:X2}). H.264, clause 7.4.1 "
        + "requires it to be zero, so these bytes are not a NAL unit — the packet boundaries are wrong or the data "
        + "is corrupt.");

    var type = (H264NalUnitType)(header & 0x1F);

    // Types 14, 20 and 21 carry three more header bytes before their payload (clause 7.3.1) and are
    // the scalable and multiview extensions. Refused where they are used rather than skipped over
    // silently, but the header has to be stepped past for the payload offset to mean anything.
    var headerBytes = type is H264NalUnitType.PrefixNalUnit or H264NalUnitType.SliceExtension
      or H264NalUnitType.DepthOrThreeDimensionalSliceExtension ? 4 : 1;

    if (unit.Length < headerBytes)
      throw new InvalidDataException(
        $"An H.264 NAL unit of type {(int)type} is {unit.Length} byte(s) long, too short for its {headerBytes}-byte header.");

    return new((header >> 5) & 3, type, _Unescape(unit[headerBytes..]));
  }

  /// <summary>
  /// Removes the emulation prevention bytes: every <c>03</c> that follows two zeroes (clause 7.3.1).
  /// </summary>
  /// <remarks>
  /// The escape exists so that no start code can occur inside a unit, and it is applied by the
  /// encoder to the payload whether or not the unit will ever be carried in a byte stream. So it is
  /// removed here for both delivery forms and not only for Annex B — an MP4's NAL units are escaped
  /// exactly as a transport stream's are, and reading one without unescaping puts a stray byte into
  /// the middle of a slice.
  /// </remarks>
  private static byte[] _Unescape(ReadOnlySpan<byte> payload) {
    // Nothing to remove is the common case for small units, and finding that out costs one pass
    // either way. The count is what decides the size of the array, so it is done first regardless.
    var removals = 0;
    for (var i = 0; i + 2 < payload.Length; ++i)
      if (payload[i] == 0 && payload[i + 1] == 0 && payload[i + 2] == 3) {
        ++removals;
        i += 2;
      }

    if (removals == 0)
      return payload.ToArray();

    var rbsp = new byte[payload.Length - removals];
    var target = 0;
    for (var i = 0; i < payload.Length; ++i) {
      if (i + 2 < payload.Length && payload[i] == 0 && payload[i + 1] == 0 && payload[i + 2] == 3) {
        rbsp[target++] = 0;
        rbsp[target++] = 0;
        i += 2;
        continue;
      }

      rbsp[target++] = payload[i];
    }

    return rbsp;
  }

  /// <summary>
  /// Finds the next start code at or after <paramref name="from"/>, answering where the unit after it
  /// begins.
  /// </summary>
  /// <returns>Where the start code itself begins, or -1 when there is none left.</returns>
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
