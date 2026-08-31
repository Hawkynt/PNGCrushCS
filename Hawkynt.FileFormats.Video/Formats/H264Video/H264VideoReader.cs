using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.H264Video;

/// <summary>
/// Cuts an H.264 Annex B byte stream into one packet per access unit.
/// </summary>
/// <remarks>
/// The structure is the start code: any number of zero bytes, then <c>00 00 01</c>, then one byte
/// whose low five bits say what the unit is (H.264, clauses 7.3.1 and B.1). Unlike MPEG-1 the pattern
/// <em>can</em> occur inside coded data, which is why the encoder stuffs a <c>03</c> byte to break it
/// up; that stuffing is undone by the decoder and not here, because a packet is handed on as the
/// bytes it was written as.
/// <para/>
/// <b>Where an access unit begins</b> is the one question this cannot answer from the start codes
/// alone, and it is the question a demuxer exists to answer. A picture may be coded as several slices,
/// so a slice following a slice is sometimes a new picture and sometimes more of the current one, and
/// nothing in the NAL unit header distinguishes the two. The distinction is in
/// <c>first_mb_in_slice</c>, which is the very first syntax element of a slice header — one
/// Exp-Golomb code at a fixed position, needing no parameter set and no state (clause 7.3.3). A slice
/// that starts at macroblock zero starts a picture (clause 7.4.1.2.4).
/// <para/>
/// That one field is the whole of what is read here, and the line is drawn there deliberately. This
/// reader never learns the picture size, the frame rate, the slice type or the profile; every one of
/// those is in a parameter set that <see cref="FileFormat.Codecs.H264VideoDecoder"/> parses for
/// itself, and a demuxer that parsed them too would be a second place for the same field to be read,
/// with two chances to disagree.
/// </remarks>
public static class H264VideoReader {

  /// <summary>NAL unit types 1 to 5 carry coded slices — the video coding layer (H.264, Table 7-1).</summary>
  private const int _FIRST_SLICE_TYPE = 1;

  private const int _LAST_SLICE_TYPE = 5;

  /// <summary>An instantaneous decoding refresh slice: a picture decodable with nothing before it.</summary>
  private const int _IDR_SLICE_TYPE = 5;

  private const int _SEQUENCE_PARAMETER_SET_TYPE = 7;

  /// <summary>Reads an instance from the specified file.</summary>
  public static H264VideoContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("H.264 video file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  /// <summary>Reads an instance from the specified stream.</summary>
  public static H264VideoContainer FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    return FromBytes(buffer.ToArray());
  }

  /// <summary>Reads an instance from the specified byte array.</summary>
  public static H264VideoContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    _RefuseWithoutStartCode(data);
    return new() { Data = data };
  }

  /// <summary>
  /// Opens a stream from a span, which copies it once.
  /// </summary>
  /// <remarks>
  /// The container outlives this call and its packets are windows onto the bytes, which a span makes
  /// no promise about. Callers holding an array should use <see cref="FromBytes"/>.
  /// </remarks>
  public static H264VideoContainer FromSpan(ReadOnlySpan<byte> data) {
    _RefuseWithoutStartCode(data);

    return new() { Data = data.ToArray() };
  }

  /// <summary>Whether these bytes open with a start code introducing a plausible NAL unit.</summary>
  internal static bool LooksLikeByteStream(ReadOnlySpan<byte> header) {
    if (header.Length < 5 || header[0] != 0 || header[1] != 0)
      return false;

    var at = header[2] == 1 ? 3 : header[2] == 0 && header[3] == 1 ? 4 : -1;
    if (at < 0 || at >= header.Length)
      return false;

    var octet = header[at];
    if ((octet & 0x80) != 0)
      return false;

    // A stream may only be entered where the parameter sets or a refresh picture are, so the units a
    // file plausibly opens with are a parameter set, an access unit delimiter, supplemental
    // information or an IDR slice. This is also what tells an H.264 byte stream from every other MPEG
    // elementary stream, which share the three-byte prefix and differ in the byte after it: MPEG-1's
    // sequence header is B3 and MPEG-4 part 2's visual object sequence is B0, both of which set the
    // forbidden bit this has already refused.
    return (octet & 0x1F) is 5 or 6 or 7 or 8 or 9;
  }

  private static void _RefuseWithoutStartCode(ReadOnlySpan<byte> data) {
    if (!LooksLikeByteStream(data))
      throw new InvalidDataException(
        "Data does not begin with an H.264 Annex B start code introducing a parameter set, an access unit "
        + "delimiter, supplemental enhancement information or an IDR slice. A byte stream may only be entered at "
        + "one of those.");
  }

  /// <summary>
  /// Walks the access units of the stream, one packet each.
  /// </summary>
  /// <remarks>
  /// A packet runs from the first NAL unit belonging to a picture through the last byte of that
  /// picture's final slice. The parameter sets, supplemental information and access unit delimiter
  /// that precede a picture are part of <em>its</em> packet rather than of the one before, because
  /// they describe what follows them — a decoder handed a picture without the sequence parameter set
  /// that introduced it has no picture size to decode it at.
  /// <para/>
  /// So the boundary is not "at every slice". It is at the first of the run of non-slice units that
  /// leads up to a slice, which is why the position of that run is remembered as the walk passes it
  /// rather than searched for backwards afterwards. That is the same shape as the MPEG-1 elementary
  /// stream reader beside this one, for the same reason.
  /// </remarks>
  internal static IEnumerable<CodedPacket> Split(ReadOnlyMemory<byte> data) {
    var packetStart = 0;
    var pendingBoundary = -1;
    var sawSlice = false;

    // Whether a sequence parameter set and an IDR slice have been read into the packet being
    // accumulated, and into the run of units that will open the next.
    var openable = false;
    var nextOpenable = false;
    var ordinal = 0L;

    foreach (var (position, payload, type) in NalUnits(data)) {
      if (type is >= _FIRST_SLICE_TYPE and <= _LAST_SLICE_TYPE) {
        // A slice that does not begin at macroblock zero is more of the picture already open, and
        // cannot start a packet of its own.
        if (sawSlice && _StartsAtFirstMacroblock(data, payload)) {
          var boundary = pendingBoundary >= 0 ? pendingBoundary : position;
          yield return _Packet(data, packetStart, boundary, ordinal, openable);
          ++ordinal;
          packetStart = boundary;
          openable = nextOpenable;
          nextOpenable = false;
        }

        sawSlice = true;
        pendingBoundary = -1;
        openable |= type == _IDR_SLICE_TYPE;
        continue;
      }

      // A unit that introduces whatever comes after it. It opens the next packet if a slice has
      // already been read into this one, and that is only known once the next slice is reached.
      if (sawSlice) {
        if (pendingBoundary < 0)
          pendingBoundary = position;

        nextOpenable |= type == _SEQUENCE_PARAMETER_SET_TYPE;
      } else
        openable |= type == _SEQUENCE_PARAMETER_SET_TYPE;
    }

    if (sawSlice)
      yield return _Packet(data, packetStart, data.Length, ordinal, openable);
  }

  /// <summary>
  /// One packet, flagged as a point decoding may begin at when it carries an IDR picture.
  /// </summary>
  /// <remarks>
  /// An IDR picture and not merely an I picture. An I slice can be coded in a stream whose later
  /// pictures still refer past it, so entering there leaves the decoded picture buffer holding
  /// nothing the following pictures expect; an IDR is defined as the point where that cannot happen
  /// (clause 7.4.1.2.4).
  /// </remarks>
  private static CodedPacket _Packet(ReadOnlyMemory<byte> data, int from, int to, long ordinal, bool refreshes)
    => new(0, data[from..to], PresentationTimestamp: null, DecodeTimestamp: ordinal, IsKeyFrame: refreshes);

  /// <summary>
  /// Whether a slice's <c>first_mb_in_slice</c> is zero, which makes it the first slice of a picture.
  /// </summary>
  /// <remarks>
  /// The field is an unsigned Exp-Golomb code at the very start of the slice header, so reading it is
  /// counting leading zero bits and taking as many again (clause 9.1). Only its being zero matters
  /// here, and a code is zero exactly when its first bit is one — so this does not even have to
  /// decode it.
  /// <para/>
  /// The emulation prevention byte cannot reach this far: it is only inserted where two zero bytes
  /// already stand, and the first payload byte of a slice whose <c>first_mb_in_slice</c> is zero has
  /// its top bit set. A slice whose first byte is zero has a first macroblock far from zero, which
  /// this answers correctly without unescaping anything.
  /// </remarks>
  private static bool _StartsAtFirstMacroblock(ReadOnlyMemory<byte> data, int payload)
    => payload < data.Length && (data.Span[payload] & 0x80) != 0;

  /// <summary>
  /// Walks every NAL unit: the offset of the start code, the offset of the byte after it, and the
  /// unit's type.
  /// </summary>
  /// <remarks>
  /// Any number of zero bytes may precede the three-byte prefix, and a code found at
  /// <c>00 00 00 01</c> must report the position of the <em>last</em> two zeroes. Reporting the
  /// earlier one would put the stuffing into the next packet instead of the previous one, which
  /// changes nothing about the decode but does change where the packets are cut — and packets cut
  /// somewhere other than where they were written are hard to compare against another demuxer's.
  /// </remarks>
  internal static IEnumerable<(int Position, int Payload, int Type)> NalUnits(ReadOnlyMemory<byte> data) {
    for (var i = 0; i + 3 < data.Length; ++i) {
      if (!_IsStartCodePrefix(data, i))
        continue;

      yield return (i, i + 4, _At(data, i + 3) & 0x1F);
      i += 2;
    }
  }

  // Both of these exist because a span cannot be a local of an iterator method.
  private static bool _IsStartCodePrefix(ReadOnlyMemory<byte> data, int offset) {
    var span = data.Span;
    return span[offset] == 0x00 && span[offset + 1] == 0x00 && span[offset + 2] == 0x01;
  }

  private static byte _At(ReadOnlyMemory<byte> data, int offset) => data.Span[offset];
}
