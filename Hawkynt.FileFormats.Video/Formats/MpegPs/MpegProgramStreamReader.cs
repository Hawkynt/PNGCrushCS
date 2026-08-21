using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MpegPs;

/// <summary>
/// Takes an MPEG program stream apart: which elementary streams it carries and where their packets
/// are.
/// </summary>
/// <remarks>
/// One reader for ISO/IEC 11172-1 and ISO/IEC 13818-1 because they are the same design twice, and the
/// places they differ are places this reader has to look anyway: the pack header, whose two layouts
/// share a start code and nothing else, and the PES header, which 13818-1 rebuilt around an explicit
/// length where 11172-1 had a run of stuffing and a code byte. Which of the two a file is, is read
/// from the first pack header and from nowhere else — there is no version field in a program stream.
/// <para/>
/// Opening walks the whole chain of packs and packets once, reading no payload. It has to: a program
/// stream carries no index, no table of streams and no duration, so the only way to say which streams
/// a file holds is to look at every packet's stream id. The walk skips each element by its own stated
/// length, so what it costs is one hop per packet — a few thousand for a film — and not a pass over
/// the bytes.
/// </remarks>
public static class MpegProgramStreamReader {

  /// <summary>DVD subpicture — the bitmap subtitles a disc overlays on the picture.</summary>
  private const byte _FIRST_SUBPICTURE = 0x20;
  private const byte _LAST_SUBPICTURE = 0x3F;
  private const byte _FIRST_AC3 = 0x80;
  private const byte _LAST_AC3 = 0x87;
  private const byte _FIRST_DTS = 0x88;
  private const byte _LAST_DTS = 0x8F;
  private const byte _FIRST_LPCM = 0xA0;
  private const byte _LAST_LPCM = 0xA7;

  /// <summary>
  /// The substream id alone, which is all a subpicture packet carries in front of its payload.
  /// </summary>
  private const int _SUBPICTURE_HEADER_LENGTH = 1;

  /// <summary>
  /// The substream id, a count of frame headers in the packet, and a sixteen-bit pointer to the first
  /// of them.
  /// </summary>
  /// <remarks>
  /// Measured on a VOB ffmpeg muxed with <c>-c:a ac3</c>: every private packet begins
  /// <c>80 05 00 01</c> and the AC-3 sync word <c>0B 77</c> follows immediately after those four
  /// bytes. Handing the four across as if they were part of the stream would put two bytes of the
  /// container into every packet a decoder is given.
  /// </remarks>
  private const int _AC3_HEADER_LENGTH = 4;

  /// <summary>The four bytes above and then three more describing the sample format.</summary>
  private const int _LPCM_HEADER_LENGTH = 7;

  /// <summary>A substream whose header width is not known, so no packet of it can be handed over.</summary>
  internal const int UNKNOWN_HEADER_LENGTH = -1;

  public static MpegProgramStreamContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MPEG program stream file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MpegProgramStreamContainer FromStream(Stream stream) {
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

  public static MpegProgramStreamContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    return _Parse(data);
  }

  /// <summary>
  /// Reads a program stream out of a span, which copies it once.
  /// </summary>
  /// <remarks>
  /// The container outlives this call — its packets are windows onto the file and are walked long
  /// afterwards — and a span promises nothing about how long the memory behind it stays valid.
  /// Callers already holding an array should use <see cref="FromBytes"/>, which keeps theirs.
  /// </remarks>
  public static MpegProgramStreamContainer FromSpan(ReadOnlySpan<byte> data) => _Parse(data.ToArray());

  private static MpegProgramStreamContainer _Parse(byte[] data) {
    if (!MpegProgramStreamContainer.StartsWithPack(data))
      throw new InvalidDataException(
        "This is not an MPEG program stream: it does not begin with the 00 00 01 BA of a pack header.");

    var file = new ReadOnlyMemory<byte>(data);

    var systemsVersion = 0;
    var streamTypes = new Dictionary<byte, byte>();
    var streams = new List<MpegPsStream>();
    var byId = new Dictionary<int, int>();

    foreach (var element in MpegPsScanner.Walk(file)) {
      if (element.StreamId == MpegPsScanner.PACK_START) {
        // The first pack decides the whole file. A stream that changed layout half way through is not
        // a thing any writer produces, and taking a later pack's word for it would mean the streams a
        // caller was already told about had been described under the other standard.
        if (systemsVersion == 0)
          systemsVersion = element.SystemsVersion;

        continue;
      }

      if (element.StreamId == MpegPsScanner.PROGRAM_STREAM_MAP) {
        _ReadStreamMap(file, element, streamTypes);
        continue;
      }

      // A system header states P-STD buffer bounds for the streams it lists. It is walked past rather
      // than read: its list is what the writer intended to put in the file, and the streams reported
      // here are the ones whose packets are actually in it. ffmpeg's demuxer takes the same view, and
      // the two agree on every file this was measured against.
      if (!MpegPsScanner.IsMedia(element.StreamId))
        continue;

      var substream = _SubstreamOf(file, element);
      var key = element.StreamId << 8 | (substream ?? 0);
      if (byId.ContainsKey(key))
        continue;

      byId.Add(key, streams.Count);
      streams.Add(_Describe(streams.Count, element.StreamId, substream, streamTypes, systemsVersion));
    }

    return new() {
      File = file,
      SystemsVersion = systemsVersion,
      ElementaryStreams = streams.ToArray(),
    };
  }

  /// <summary>The substream a private packet belongs to, which is the first byte of its payload.</summary>
  /// <remarks>
  /// Several streams share the stream id <c>0xBD</c> in a VOB — two languages of AC-3 and a handful of
  /// subpicture tracks is ordinary — and nothing but this byte tells them apart. Reporting them as one
  /// stream would interleave four films' worth of sound into a single track.
  /// </remarks>
  private static byte? _SubstreamOf(ReadOnlyMemory<byte> file, MpegPsElement element) {
    if (element.StreamId != MpegPsScanner.PRIVATE_STREAM_1)
      return null;

    if (element.PayloadLength < 1)
      throw new InvalidDataException(
        $"The private stream packet at offset {element.Position} carries no payload, so nothing says which "
        + "of the streams sharing stream id 0xBD it belongs to.");

    return file.Span[element.PayloadOffset];
  }

  /// <summary>Describes one elementary stream from its ids and whatever the file said about it.</summary>
  private static MpegPsStream _Describe(
    int index, byte streamId, byte? substreamId, IReadOnlyDictionary<byte, byte> streamTypes, int systemsVersion) {
    var declared = streamTypes.TryGetValue(streamId, out var type) ? type : (byte)0;

    if (MpegPsScanner.IsVideo(streamId))
      return new(streamId, null, 0, _Info(index, MediaStreamKind.Video, _VideoCodec(declared, systemsVersion)));

    if (MpegPsScanner.IsAudio(streamId))
      return new(streamId, null, 0, _Info(index, MediaStreamKind.Audio, _AudioCodec(declared)));

    var (kind, codec, headerLength) = substreamId switch {
      >= _FIRST_SUBPICTURE and <= _LAST_SUBPICTURE => (MediaStreamKind.Subtitle, CodecTag.FromCharacters("subp"), _SUBPICTURE_HEADER_LENGTH),
      >= _FIRST_AC3 and <= _LAST_AC3 => (MediaStreamKind.Audio, CodecTag.FromCharacters("ac-3"), _AC3_HEADER_LENGTH),
      >= _FIRST_DTS and <= _LAST_DTS => (MediaStreamKind.Audio, CodecTag.FromCharacters("dts "), _AC3_HEADER_LENGTH),
      >= _FIRST_LPCM and <= _LAST_LPCM => (MediaStreamKind.Audio, CodecTag.FromCharacters("lpcm"), _LPCM_HEADER_LENGTH),
      // Something is there and it is a stream; what its packets begin with is not known, so they are
      // refused when asked for rather than handed over with an unknown number of container bytes on
      // the front. The stream is still reported, because leaving it out would renumber the others.
      _ => (MediaStreamKind.Unknown, CodecTag.None, UNKNOWN_HEADER_LENGTH),
    };

    return new(streamId, substreamId, headerLength, _Info(index, kind, codec));
  }

  /// <summary>
  /// Which of the two MPEG video standards a video stream is coded with.
  /// </summary>
  /// <remarks>
  /// A program stream that carries a program stream map states it outright, and that is taken. One
  /// that does not — which is every file ffmpeg's own muxers write — states only which systems
  /// standard it is, and the two travel together: an ISO/IEC 11172-1 stream may only carry ISO/IEC
  /// 11172-2 video, so a 12-byte pack header settles it, and a 13818-1 stream in practice carries
  /// 13818-2. ffprobe reports <c>mpeg1video</c> and <c>mpeg2video</c> for exactly that split on all
  /// six files this was measured against.
  /// <para/>
  /// The remaining case is a 13818-1 stream carrying 11172-2 pictures, which is legal and which this
  /// would call <c>mpg2</c>. It costs nothing: 13818-2 defines 11172-2 as a subset it decodes, so a
  /// decoder that takes <c>mpg2</c> takes those pictures too. The reverse cannot arise.
  /// </remarks>
  private static CodecTag _VideoCodec(byte declaredType, int systemsVersion)
    => declaredType switch {
      0x01 => CodecTag.FromCharacters("mpg1"),
      0x02 => CodecTag.FromCharacters("mpg2"),
      0x10 => CodecTag.FromCharacters("mp4v"),
      0x1B => CodecTag.FromCharacters("avc1"),
      0x24 => CodecTag.FromCharacters("hvc1"),
      0x33 => CodecTag.FromCharacters("vvc1"),
      _ => CodecTag.FromCharacters(systemsVersion == 1 ? "mpg1" : "mpg2"),
    };

  private static CodecTag _AudioCodec(byte declaredType)
    => declaredType switch {
      0x0F or 0x11 => CodecTag.FromCharacters("aac "),
      0x81 => CodecTag.FromCharacters("ac-3"),
      0x82 => CodecTag.FromCharacters("dts "),
      // Stream ids 0xC0 to 0xDF are defined as MPEG audio and nothing else, so the fallback is not a
      // guess: the layer is in the frames and is the decoder's to read.
      _ => CodecTag.FromCharacters("mpga"),
    };

  private static MediaStreamInfo _Info(int index, MediaStreamKind kind, CodecTag codec)
    => new() {
      Index = index,
      Kind = kind,
      Codec = codec,
      // Every timestamp in a program stream is counted in ticks of the 90 kHz system clock, whatever
      // the stream and whatever the codec. ffprobe reports 1/90000 as the time base of every stream of
      // every file measured here.
      TimeBase = new(1, MpegPsScanner.SYSTEM_CLOCK_HZ),
      // No width, no height, no frame rate, no frame count and no codec private data — a program
      // stream states none of them. They are in the elementary stream, which for MPEG video means the
      // sequence header that the first packet of the stream begins with. Reading it here to fill these
      // in would be this container decoding, and a caller would be handed a size no header of the
      // container ever claimed.
    };

  /// <summary>
  /// Reads a program stream map, which is the one place a program stream names its codecs.
  /// </summary>
  /// <remarks>
  /// Rare — no ffmpeg muxer writes one, and every reference file here was read without it — but it is
  /// the only container-level statement of what a stream is coded with, so where it is present it
  /// outranks the inference from the pack header. A map that is cut short is read as far as it goes
  /// and no further rather than refused: it describes streams, and the streams themselves are still
  /// all there in the packets.
  /// </remarks>
  private static void _ReadStreamMap(ReadOnlyMemory<byte> file, MpegPsElement map, IDictionary<byte, byte> into) {
    var span = file.Span.Slice(map.PayloadOffset, map.PayloadLength);

    // A version byte, a marker byte, then the descriptors that apply to the programme as a whole.
    const int _PREFIX = 4;
    if (span.Length < _PREFIX)
      return;

    var programInfoLength = (span[2] << 8) | span[3];
    var at = _PREFIX + programInfoLength;
    if (at + 2 > span.Length)
      return;

    var mapLength = (span[at] << 8) | span[at + 1];
    at += 2;

    var end = Math.Min(at + mapLength, span.Length);
    while (at + 4 <= end) {
      var type = span[at];
      var streamId = span[at + 1];
      var infoLength = (span[at + 2] << 8) | span[at + 3];
      into[streamId] = type;
      at += 4 + infoLength;
    }
  }
}
