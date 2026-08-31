using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MpegPs;

/// <summary>
/// An MPEG program stream — <c>.mpg</c>, <c>.mpeg</c>, <c>.vob</c>, <c>.m2p</c> — taken apart into the
/// elementary streams it multiplexes and the coded packets they are made of.
/// </summary>
/// <remarks>
/// This container knows where every packet is and nothing about what is in any of them. It does not
/// decode, it never reports a picture, and it does not refuse a file for carrying a codec nothing here
/// reads: a VOB full of MPEG-2 video and AC-3 is a perfectly good VOB, and copying its packets into
/// another container needs no decoder. Refusal by code happens later, when a decoder is asked for.
/// <para/>
/// What makes this container unlike the other two here is that its packets are not its packets. An AVI
/// stores one frame per chunk and an ISO base media file describes each sample in a table, so in both
/// the container states where a coded picture begins and ends. A program stream states no such thing:
/// it chops the elementary stream into PES packets sized to fill 2048-byte packs, a picture routinely
/// spans two of them and a single PES packet routinely holds seven. The bytes a decoder needs are the
/// elementary stream with all of that removed, so the payloads are stitched back together and cut
/// again where the elementary stream itself says a picture starts.
/// <para/>
/// That cut is made on start codes and nothing else — <c>00 00 01</c> followed by a picture, a
/// sequence header or a group header. Those three bytes are the one pattern the systems layer
/// guarantees cannot occur inside coded data, which is what makes finding them the container's job
/// rather than a codec's. Sound is left at PES packet boundaries for the mirror-image reason: an MPEG
/// audio frame is found by reading a bitrate and a sampling rate out of a table, which is the codec's
/// knowledge and not the container's.
/// <para/>
/// Measured against <c>ffprobe -fflags +nofillin</c> on six files ffmpeg muxed — MPEG-1 and MPEG-2
/// program streams, with and without B-pictures, with MPEG audio and with AC-3 — the video packets
/// agree in count, in order, in size and in every timestamp the file states. <c>+nofillin</c> is the
/// comparison that means something: without it libavformat also fills in timestamps the container
/// never carried, by interpolating from the frame rate, and a demuxer that matched those numbers would
/// be reporting as read what was in fact inferred.
/// </remarks>
[FormatMagicBytes([0x00, 0x00, 0x01, 0xBA])]
[FormatMimeType("video/mpeg", "video/x-mpeg", "video/dvd", "video/mpeg-system")]
public sealed class MpegProgramStreamContainer : IVideoContainerReader<MpegProgramStreamContainer> {
  /// <summary>Initializes a new instance of this type.</summary>
  public MpegProgramStreamContainer() { }

  private const byte _PICTURE_START_CODE = 0x00;
  private const byte _SEQUENCE_HEADER_CODE = 0xB3;
  private const byte _GROUP_START_CODE = 0xB8;

  /// <summary>The whole file, which every packet that lies inside one PES payload is a window onto.</summary>
  public required ReadOnlyMemory<byte> File { get; init; }

  /// <summary>1 for an ISO/IEC 11172-1 file, 2 for an ISO/IEC 13818-1 one, as the first pack said.</summary>
  public int SystemsVersion { get; init; }

  /// <summary>Every elementary stream the file carries, in the order its packets first appear.</summary>
  /// <remarks>
  /// First appearance and not stream id order, because that is the order a program stream declares
  /// anything in — it has no header listing its streams — and it is the order ffprobe numbers them in
  /// for the same file.
  /// </remarks>
  internal IReadOnlyList<MpegPsStream> ElementaryStreams { get; init; } = [];

  // -------- Format identity --------

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".mpg";

  /// <summary>
  /// Every name the one format goes under.
  /// </summary>
  /// <remarks>
  /// <c>.vob</c> and <c>.m2p</c> are the same container as <c>.mpg</c> with a later systems standard
  /// and stricter rules about what may be in it — a DVD's VOB is a 13818-1 program stream whose packs
  /// are exactly one sector long. Nothing about taking it apart differs, so nothing here branches on
  /// the name.
  /// <para/>
  /// <c>.ps</c> is deliberately not here even though it is a name a program stream goes under. It is
  /// PostScript's name far more often, and the image library already claims it; taking it as well
  /// would put this container in front of every PostScript file for the sake of the rare one that is
  /// not, and win nothing — a real program stream is recognised by its bytes whatever it is called.
  /// </remarks>
  public static string[] FileExtensions => [".mpg", ".mpeg", ".vob", ".m2p", ".m2ps"];

  /// <summary>A file that begins with a pack header.</summary>
  /// <remarks>
  /// A program stream is a chain of packs and ends with an end code, so the first four bytes of one
  /// are always the pack start code. Nothing weaker will do: the elementary streams inside are full of
  /// <c>00 00 01</c> prefixes of their own, and a raw MPEG video file begins with one — claiming any
  /// start code would claim every <c>.m1v</c> and <c>.m2v</c> in existence for a container they are
  /// not in.
  /// </remarks>
  public static bool? MatchesSignature(ReadOnlySpan<byte> header) => StartsWithPack(header) ? true : null;

  internal static bool StartsWithPack(ReadOnlySpan<byte> data)
    => data.Length >= 4 && data[0] == 0x00 && data[1] == 0x00 && data[2] == 0x01 && data[3] == MpegPsScanner.PACK_START;

  // -------- Demux --------

  /// <summary>Reads an instance from the specified byte span.</summary>
  public static MpegProgramStreamContainer FromSpan(ReadOnlySpan<byte> data) => MpegProgramStreamReader.FromSpan(data);

  /// <summary>Opens a program stream over the caller's array, keeping it rather than copying it.</summary>
  public static MpegProgramStreamContainer FromBytes(byte[] data) => MpegProgramStreamReader.FromBytes(data);

  /// <summary>Reads an instance from the specified file.</summary>
  public static MpegProgramStreamContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MPEG program stream file not found.", file.FullName);

    return MpegProgramStreamReader.FromBytes(System.IO.File.ReadAllBytes(file.FullName));
  }

  /// <summary>Every elementary stream the file carries — sound and subpictures as well as pictures.</summary>
  public static IReadOnlyList<MediaStreamInfo> Streams(MpegProgramStreamContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var result = new MediaStreamInfo[container.ElementaryStreams.Count];
    for (var i = 0; i < result.Length; ++i)
      result[i] = container.ElementaryStreams[i].Info;

    return result;
  }

  /// <summary>
  /// What the file says about itself, which is the streams it carries and nothing else.
  /// </summary>
  /// <remarks>
  /// A program stream has no place to put a title, an author or a date, and it does not state its own
  /// duration either — the figure ffprobe reports for one is arrived at by reading to the end and
  /// subtracting the first timestamp from the last, which is a measurement of the file and not a claim
  /// the file makes. Reporting it here as <see cref="VideoMetadata.Duration"/>, which every other
  /// container fills from a header field, would put a measured number where the model promises a
  /// declared one.
  /// </remarks>
  public static VideoMetadata Metadata(MpegProgramStreamContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var streams = new MediaStreamMetadata[container.ElementaryStreams.Count];
    for (var i = 0; i < streams.Length; ++i) {
      var info = container.ElementaryStreams[i].Info;
      streams[i] = new(info.Index, info.Kind, info.Codec);
    }

    return new() { Streams = streams };
  }

  /// <summary>Walks every packet of every stream, in the order the file completes them.</summary>
  /// <remarks>
  /// Lazy and re-runnable. The walk holds one access unit per video stream and never a list of them,
  /// so a film enumerated for its first frame costs one frame however long it is.
  /// </remarks>
  public static IEnumerable<CodedPacket> ReadPackets(MpegProgramStreamContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return _Walk(container, null);
  }

  /// <summary>Walks the packets of one stream, skipping the others rather than filtering them out.</summary>
  /// <remarks>
  /// Skipping is free here — each element states its own length, so passing over another stream's
  /// packet costs one addition — and a filtered walk hands back exactly the same packets as the full
  /// one, because every timestamp comes from a PES header rather than from a running count. That is
  /// what makes overriding this safe, where the AVI reader has to keep counting the streams it is not
  /// reporting.
  /// </remarks>
  public static IEnumerable<CodedPacket> ReadPackets(MpegProgramStreamContainer container, int streamIndex) {
    ArgumentNullException.ThrowIfNull(container);

    if ((uint)streamIndex >= (uint)container.ElementaryStreams.Count)
      return [];

    return _Walk(container, streamIndex);
  }

  // ------------------------------------------------------------------------------------------
  // The walk
  // ------------------------------------------------------------------------------------------

  /// <summary>
  /// What has been gathered of one video stream's current access unit.
  /// </summary>
  /// <remarks>
  /// A class rather than a struct because it is mutated in place through a walk that holds one per
  /// stream, and because the ranges it collects are a list that is cleared and refilled rather than a
  /// value that is copied per packet.
  /// </remarks>
  private sealed class VideoAccessUnit {

    /// <summary>The file ranges this access unit is made of, in order.</summary>
    /// <remarks>
    /// More than one exactly when the picture spans a PES boundary, which for the reference files is
    /// most of them. One range is handed out as a window onto the file; several have to be joined, and
    /// that copy is the only one this reader makes.
    /// </remarks>
    internal readonly List<(int Offset, int Length)> Ranges = [];

    internal long? PresentationTimestamp;
    internal long? DecodeTimestamp;
    internal bool IsOpen;
    internal bool SawPicture;
    internal bool StartsSequence;

    /// <summary>
    /// How many bytes at the end of the previous payload of this stream could be the beginning of a
    /// start code, and where they end.
    /// </summary>
    /// <remarks>
    /// A start code is four bytes and a PES packet may end in the middle of one. Scanning each payload
    /// on its own would miss that boundary and glue two pictures into one packet, so up to three bytes
    /// are carried across and the join is tested explicitly.
    /// </remarks>
    internal int Carry;

    internal int CarryEnd;

    /// <summary>The timestamps of the previous PES packet, where no access unit commenced in it to
    /// take them.</summary>
    /// <remarks>
    /// Which happens exactly when a picture starts within the last three bytes of a packet: the
    /// picture commences in that packet, so its timestamps are the ones the packet stated, even though
    /// the code that says so is only complete in the next one.
    /// </remarks>
    internal long? CarriedPresentationTimestamp;

    internal long? CarriedDecodeTimestamp;
  }

  private static IEnumerable<CodedPacket> _Walk(MpegProgramStreamContainer container, int? onlyStream) {
    var streams = container.ElementaryStreams;
    var units = new VideoAccessUnit?[streams.Count];
    var byId = new Dictionary<int, int>(streams.Count);
    for (var i = 0; i < streams.Count; ++i) {
      byId[streams[i].StreamId << 8 | (streams[i].SubstreamId ?? 0)] = i;
      if (streams[i].Info.Kind == MediaStreamKind.Video)
        units[i] = new();
    }

    var starts = new List<int>();

    foreach (var element in MpegPsScanner.Walk(container.File)) {
      if (!MpegPsScanner.IsMedia(element.StreamId))
        continue;

      var substream = element.StreamId == MpegPsScanner.PRIVATE_STREAM_1 && element.PayloadLength > 0
        ? container.File.Span[element.PayloadOffset]
        : (byte)0;

      if (!byId.TryGetValue(element.StreamId << 8 | substream, out var index))
        continue;
      if (onlyStream != null && index != onlyStream)
        continue;

      if (units[index] is not { } unit) {
        yield return _NonVideoPacket(container, streams[index], element);
        continue;
      }

      var from = element.PayloadOffset;
      var to = from + element.PayloadLength;

      // A start code that begins an access unit and began in the previous payload of this stream, so
      // that only its last bytes are here. Handled before the payload is scanned, because the place it
      // says a picture starts is a few bytes back inside a range that has already been recorded.
      var junction = _JunctionCode(container.File, unit, from, to);
      if (junction is _PICTURE_START_CODE or _SEQUENCE_HEADER_CODE or _GROUP_START_CODE) {
        if (unit.IsOpen && unit.SawPicture) {
          _Shorten(unit.Ranges, unit.Carry);
          yield return _Packet(container.File, index, unit);
          _Reset(unit);
        }

        if (!unit.IsOpen) {
          unit.IsOpen = true;
          unit.Ranges.Add((unit.CarryEnd - unit.Carry, unit.Carry));
          unit.PresentationTimestamp = unit.CarriedPresentationTimestamp;
          unit.DecodeTimestamp = unit.CarriedDecodeTimestamp;
        }

        _Note(unit, (byte)junction);
      }

      var claimed = false;
      var start = from;
      _CollectUnitStarts(container.File, from, to, starts);

      foreach (var at in starts) {
        if (unit.IsOpen && unit.SawPicture) {
          unit.Ranges.Add((start, at - start));
          yield return _Packet(container.File, index, unit);
          _Reset(unit);
        }

        if (!unit.IsOpen) {
          unit.IsOpen = true;
          start = at;

          // A PES header's timestamps belong to the first access unit that commences in that packet,
          // and to no other. The ones after it in the same packet carry none, which is exactly what
          // ffprobe reports for them once it is told not to fill in what the file does not say.
          if (!claimed) {
            unit.PresentationTimestamp = element.PresentationTimestamp;
            unit.DecodeTimestamp = element.DecodeTimestamp;
            claimed = true;
          }
        }

        _Note(unit, container.File.Span[at + 3]);
      }

      if (unit.IsOpen && to > start)
        unit.Ranges.Add((start, to - start));

      unit.Carry = _TrailingStartCodePrefix(container.File, from, to);
      unit.CarryEnd = to;
      unit.CarriedPresentationTimestamp = claimed ? null : element.PresentationTimestamp;
      unit.CarriedDecodeTimestamp = claimed ? null : element.DecodeTimestamp;
    }

    // Whatever was still being gathered when the file ran out. An access unit that got as far as a
    // picture is a picture and is handed over; one that is only a sequence header or a group header
    // with nothing behind it is not a packet and is dropped rather than reported as one.
    for (var i = 0; i < units.Length; ++i) {
      if (onlyStream != null && i != onlyStream)
        continue;
      if (units[i] is { IsOpen: true, SawPicture: true } unit)
        yield return _Packet(container.File, i, unit);
    }
  }

  private static void _Note(VideoAccessUnit unit, byte code) {
    if (code == _PICTURE_START_CODE)
      unit.SawPicture = true;
    else if (code == _SEQUENCE_HEADER_CODE)
      unit.StartsSequence = true;
  }

  private static void _Reset(VideoAccessUnit unit) {
    unit.Ranges.Clear();
    unit.IsOpen = false;
    unit.SawPicture = false;
    unit.StartsSequence = false;
    unit.PresentationTimestamp = null;
    unit.DecodeTimestamp = null;
  }

  private static void _Shorten(List<(int Offset, int Length)> ranges, int by) {
    if (ranges.Count == 0)
      return;

    var last = ranges[^1];
    ranges[^1] = (last.Offset, last.Length - by);
  }

  /// <summary>Turns the ranges gathered for one access unit into a packet.</summary>
  private static CodedPacket _Packet(ReadOnlyMemory<byte> file, int index, VideoAccessUnit unit) {
    var data = unit.Ranges.Count == 1
      ? file.Slice(unit.Ranges[0].Offset, unit.Ranges[0].Length)
      : _Join(file, unit.Ranges);

    // Decoding may begin at an access unit that carries a sequence header, and at no other: an I
    // picture on its own says nothing about the size of the picture or the shape of its quantiser
    // tables, and a decoder started there has nothing to build a frame in. ffprobe flags the same
    // packets on every reference file — it flags I pictures, and ffmpeg writes a sequence header in
    // front of each of them — but where the two could differ this is the one a caller can act on.
    return new(index, data, unit.PresentationTimestamp, unit.DecodeTimestamp, IsKeyFrame: unit.StartsSequence);
  }

  private static ReadOnlyMemory<byte> _Join(ReadOnlyMemory<byte> file, List<(int Offset, int Length)> ranges) {
    var total = 0;
    foreach (var range in ranges)
      total += range.Length;

    var joined = new byte[total];
    var at = 0;
    foreach (var range in ranges) {
      file.Span.Slice(range.Offset, range.Length).CopyTo(joined.AsSpan(at));
      at += range.Length;
    }

    return joined;
  }

  /// <summary>One packet of a stream this reader does not cut into access units.</summary>
  /// <remarks>
  /// The PES payload as it stands, with the private stream's own header taken off the front where
  /// there is one. Sound is left whole on purpose: an MPEG audio frame or an AC-3 frame is found by
  /// reading a sampling rate and a bitrate out of the codec's tables, which is the codec's work, and a
  /// container that did it would be deciding what its packets mean. ffprobe does split them, using its
  /// audio parsers, so its packet list for a stream of sound is finer than this one — that difference
  /// is the demux/decode line and not a disagreement about the file.
  /// </remarks>
  private static CodedPacket _NonVideoPacket(MpegProgramStreamContainer container, MpegPsStream stream, MpegPsElement element) {
    var skip = stream.PrivateHeaderLength;
    if (skip == MpegProgramStreamReader.UNKNOWN_HEADER_LENGTH)
      throw new NotSupportedException(
        $"Stream {stream.Info.Index} is substream 0x{stream.SubstreamId:X2} of private stream 1, whose packets begin "
        + "with a header this reader does not know the length of. Handing the payload over would put an unknown "
        + "number of container bytes at the front of every packet.");

    if (skip > element.PayloadLength)
      throw new InvalidDataException(
        $"The packet for stream {stream.Info.Index} at offset {element.Position} is {element.PayloadLength} bytes long, "
        + $"which is less than the {skip}-byte private stream header it is required to begin with.");

    return new(
      stream.Info.Index,
      container.File.Slice(element.PayloadOffset + skip, element.PayloadLength - skip),
      element.PresentationTimestamp,
      element.DecodeTimestamp,
      // Every frame of MPEG audio, AC-3, DTS and linear PCM stands on its own, so any packet of one is
      // a place playback may start.
      IsKeyFrame: stream.Info.Kind == MediaStreamKind.Audio);
  }

  // ------------------------------------------------------------------------------------------
  // Start codes
  // ------------------------------------------------------------------------------------------

  /// <summary>
  /// Collects the offsets of every start code in a payload that begins an access unit.
  /// </summary>
  /// <remarks>
  /// Three of the codes end a picture and the rest do not. A picture start code obviously begins one;
  /// a sequence header and a group header begin one too, because both are written in front of the
  /// picture they apply to and cutting after them would put the header of a frame into the packet
  /// before it. Everything else — slices, extensions, user data — is inside a picture and is passed
  /// over.
  /// <para/>
  /// The cut is made once a picture start code has been seen. ffmpeg's own parser waits instead for
  /// the first slice, which on any conforming stream is the same moment one step later — a picture
  /// always has at least one slice — and the two agreed on every packet of every file measured. The
  /// difference shows only on a stream of picture headers with no slices behind them, which is not a
  /// stream.
  /// </remarks>
  private static void _CollectUnitStarts(ReadOnlyMemory<byte> file, int from, int to, List<int> into) {
    into.Clear();

    var span = file.Span;
    ReadOnlySpan<byte> prefix = [0x00, 0x00, 0x01];
    var at = from;

    while (at + 3 < to) {
      var found = span[at..to].IndexOf(prefix);
      if (found < 0)
        return;

      var start = at + found;
      if (start + 3 >= to)
        return;

      if (span[start + 3] is _PICTURE_START_CODE or _SEQUENCE_HEADER_CODE or _GROUP_START_CODE)
        into.Add(start);

      // Three and not four: the bytes 00 00 01 00 00 01 hold two start codes, the second beginning at
      // the fourth byte of the first.
      at = start + 3;
    }
  }

  /// <summary>
  /// The code byte of a start code that straddles the join between the previous payload of a stream
  /// and this one, or <c>-1</c> when none does.
  /// </summary>
  /// <remarks>
  /// Three joins are possible, one per number of prefix bytes that fell on the far side, and at most
  /// one of them can hold at a time: they disagree about what the bytes on this side have to be.
  /// <para/>
  /// Not a <see cref="byte"/>, because a picture start code's own code byte is zero and a method that
  /// said "none" with zero could never report one. Getting that wrong reads as a picture boundary
  /// missed at every join it falls on, and the two pictures either side of it handed over as one
  /// packet — which is exactly what a 352x288 stream, whose pictures are several PES packets each,
  /// showed against ffprobe before it was fixed.
  /// </remarks>
  private static int _JunctionCode(ReadOnlyMemory<byte> file, VideoAccessUnit unit, int from, int to) {
    if (unit.Carry <= 0)
      return -1;

    var span = file.Span;
    var tail = unit.CarryEnd - unit.Carry;
    var need = 4 - unit.Carry;
    if (from + need > to)
      return -1;

    // The whole prefix is on the far side and only the code byte is here.
    if (unit.Carry == 3)
      return span[from];

    // Two bytes of zero there and the 01 here, or one byte of zero there and 00 01 here.
    if (unit.Carry == 2)
      return span[tail] == 0x00 && span[tail + 1] == 0x00 && span[from] == 0x01 ? span[from + 1] : -1;

    return span[tail] == 0x00 && span[from] == 0x00 && span[from + 1] == 0x01 ? span[from + 2] : -1;
  }

  /// <summary>
  /// How many bytes at the end of a payload could be the beginning of a start code.
  /// </summary>
  /// <remarks>
  /// Never more than the payload holds. A start code split across three payloads would need one of
  /// them to be shorter than three bytes, which no writer produces — a PES packet exists to fill a
  /// pack — and is not tracked.
  /// </remarks>
  private static int _TrailingStartCodePrefix(ReadOnlyMemory<byte> file, int from, int to) {
    var span = file.Span;
    var available = to - from;

    if (available >= 3 && span[to - 3] == 0x00 && span[to - 2] == 0x00 && span[to - 1] == 0x01)
      return 3;
    if (available >= 2 && span[to - 2] == 0x00 && span[to - 1] == 0x00)
      return 2;
    if (available >= 1 && span[to - 1] == 0x00)
      return 1;

    return 0;
  }
}
