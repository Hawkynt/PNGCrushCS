using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.H265Video;

/// <summary>
/// Cuts an H.265 Annex B byte stream into one packet per access unit.
/// </summary>
/// <remarks>
/// The structure is the start code: any number of zero bytes, then <c>00 00 01</c>, then two bytes
/// whose middle six bits say what the unit is (H.265, clauses 7.3.1 and B.1). The pattern can occur
/// inside coded data, which is why the encoder stuffs a <c>03</c> byte to break it up; that stuffing
/// is undone by the decoder and not here, because a packet is handed on as the bytes it was written
/// as — and because the slice header's entry point offsets are counted in the bytes as written.
/// <para/>
/// <b>Where an access unit begins</b> is the one question this cannot answer from the start codes
/// alone, and it is the question a demuxer exists to answer. A picture may be coded as several slice
/// segments, so a slice following a slice is sometimes a new picture and sometimes more of the
/// current one. The distinction is <c>first_slice_segment_in_pic_flag</c>, which is the very first
/// bit of a slice segment header — one bit at a fixed position, needing no parameter set and no state
/// (clause 7.3.6.1).
/// <para/>
/// That one bit is the whole of what is read here, and the line is drawn there deliberately. This
/// reader never learns the picture size, the frame rate, the slice type or the profile; every one of
/// those is in a parameter set that <see cref="FileFormat.Codecs.H265VideoDecoder"/> parses for
/// itself, and a demuxer that parsed them too would be a second place for the same field to be read,
/// with two chances to disagree.
/// </remarks>
public static class H265VideoReader {

  /// <summary>NAL unit types 0 to 31 carry coded slice segments — the video coding layer (Table 7-1).</summary>
  private const int _LAST_SLICE_TYPE = 31;

  /// <summary>The first type that introduces a picture rather than being part of one.</summary>
  private const int _VIDEO_PARAMETER_SET_TYPE = 32;

  /// <summary>Types 16 to 23 are the intra random access points, which decoding may begin at.</summary>
  private const int _FIRST_RANDOM_ACCESS_TYPE = 16;

  private const int _LAST_RANDOM_ACCESS_TYPE = 23;

  public static H265VideoContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("H.265 video file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static H265VideoContainer FromStream(Stream stream) {
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

  public static H265VideoContainer FromBytes(byte[] data) {
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
  public static H265VideoContainer FromSpan(ReadOnlySpan<byte> data) {
    _RefuseWithoutStartCode(data);

    return new() { Data = data.ToArray() };
  }

  /// <summary>
  /// Whether these bytes open with a start code introducing a plausible HEVC NAL unit.
  /// </summary>
  /// <remarks>
  /// Every MPEG elementary stream opens with <c>00 00 01</c>, so the prefix alone claims H.264,
  /// MPEG-1 and MPEG-4 part 2 as well. Three things tell HEVC apart, and all three are needed. The
  /// unit type must be one a stream may be entered at — a parameter set, an access unit delimiter or
  /// a random access point. The layer must be zero, which is what rules out an H.264 sequence
  /// parameter set: its <c>27</c> reads as a valid HEVC refresh picture, but the profile byte that
  /// follows reads as a layer forty. And the temporal sub-layer field may not be zero, which clause
  /// 7.4.2.2 forbids.
  /// </remarks>
  internal static bool LooksLikeByteStream(ReadOnlySpan<byte> header) {
    if (header.Length < 6 || header[0] != 0 || header[1] != 0)
      return false;

    var at = header[2] == 1 ? 3 : header[2] == 0 && header[3] == 1 ? 4 : -1;
    if (at < 0 || at + 1 >= header.Length)
      return false;

    var first = header[at];
    if ((first & 0x80) != 0)
      return false;

    var type = (first >> 1) & 0x3F;
    if (type is not (>= _FIRST_RANDOM_ACCESS_TYPE and <= _LAST_RANDOM_ACCESS_TYPE) and not (>= 32 and <= 35) and not 39)
      return false;

    var second = header[at + 1];
    var layerId = ((first & 1) << 5) | (second >> 3);
    return layerId == 0 && (second & 7) != 0;
  }

  private static void _RefuseWithoutStartCode(ReadOnlySpan<byte> data) {
    if (!LooksLikeByteStream(data))
      throw new InvalidDataException(
        "Data does not begin with an H.265 Annex B start code introducing a parameter set, an access unit "
        + "delimiter, supplemental enhancement information or a random access point picture. A byte stream may only "
        + "be entered at one of those.");
  }

  /// <summary>
  /// Walks the access units of the stream, one packet each.
  /// </summary>
  /// <remarks>
  /// A packet runs from the first NAL unit belonging to a picture through the last byte of that
  /// picture's final slice segment. The parameter sets, supplemental information and access unit
  /// delimiter that precede a picture are part of <em>its</em> packet rather than of the one before,
  /// because they describe what follows them — a decoder handed a picture without the sequence
  /// parameter set that introduced it has no picture size to decode it at.
  /// <para/>
  /// So the boundary is not "at every slice". It is at the first of the run of non-slice units that
  /// leads up to a slice, which is why the position of that run is remembered as the walk passes it
  /// rather than searched for backwards afterwards.
  /// </remarks>
  internal static IEnumerable<CodedPacket> Split(ReadOnlyMemory<byte> data) {
    var packetStart = 0;
    var pendingBoundary = -1;
    var sawSlice = false;

    // Whether a random access point has been read into the packet being accumulated, and into the
    // run of units that will open the next.
    var openable = false;
    var nextOpenable = false;
    var ordinal = 0L;

    foreach (var (position, payload, type) in NalUnits(data)) {
      if (type <= _LAST_SLICE_TYPE) {
        // A segment that does not begin a picture is more of the picture already open, and cannot
        // start a packet of its own.
        if (sawSlice && _StartsAPicture(data, payload)) {
          var boundary = pendingBoundary >= 0 ? pendingBoundary : position;
          yield return _Packet(data, packetStart, boundary, ordinal, openable);
          ++ordinal;
          packetStart = boundary;
          openable = nextOpenable;
          nextOpenable = false;
        }

        sawSlice = true;
        pendingBoundary = -1;
        openable |= type is >= _FIRST_RANDOM_ACCESS_TYPE and <= _LAST_RANDOM_ACCESS_TYPE;
        continue;
      }

      // A unit that introduces whatever comes after it. It opens the next packet if a slice has
      // already been read into this one, and that is only known once the next slice is reached.
      if (sawSlice) {
        if (pendingBoundary < 0)
          pendingBoundary = position;

        nextOpenable |= type == _VIDEO_PARAMETER_SET_TYPE;
      } else
        openable |= type == _VIDEO_PARAMETER_SET_TYPE;
    }

    if (sawSlice)
      yield return _Packet(data, packetStart, data.Length, ordinal, openable);
  }

  private static CodedPacket _Packet(ReadOnlyMemory<byte> data, int from, int to, long ordinal, bool refreshes)
    => new(0, data[from..to], PresentationTimestamp: null, DecodeTimestamp: ordinal, IsKeyFrame: refreshes);

  /// <summary>
  /// Whether a slice segment's <c>first_slice_segment_in_pic_flag</c> is set — clause 7.3.6.1.
  /// </summary>
  /// <remarks>
  /// The very first bit of the header, so it is the top bit of the byte after the two-byte NAL unit
  /// header. The emulation prevention byte cannot reach that far: it is only inserted where two zero
  /// bytes already stand, and there have been at most one here.
  /// </remarks>
  private static bool _StartsAPicture(ReadOnlyMemory<byte> data, int payload)
    => payload < data.Length && (data.Span[payload] & 0x80) != 0;

  /// <summary>
  /// Walks every NAL unit: the offset of the start code, the offset of its payload, and its type.
  /// </summary>
  /// <remarks>
  /// The payload offset is past the two-byte NAL unit header, which is one byte more than H.264's —
  /// HEVC spends the extra byte on the layer and the temporal sub-layer, which is what makes its
  /// scalable extensions a header field rather than a separate unit type.
  /// </remarks>
  internal static IEnumerable<(int Position, int Payload, int Type)> NalUnits(ReadOnlyMemory<byte> data) {
    for (var i = 0; i + 4 < data.Length; ++i) {
      if (!_IsStartCodePrefix(data, i))
        continue;

      yield return (i, i + 5, (_At(data, i + 3) >> 1) & 0x3F);
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
