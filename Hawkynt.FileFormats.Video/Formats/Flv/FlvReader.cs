using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Flv;

/// <summary>
/// Takes a Flash Video file apart: which streams its tags turn out to belong to, what its
/// <c>onMetaData</c> says about it, and where its packets are.
/// </summary>
/// <remarks>
/// FLV has no header describing its streams. There is a nine-byte file header saying whether sound
/// and pictures are present and nothing else — no codec, no size, no count — so the only way to
/// describe a stream is to find the first tag belonging to it. That is why this walks the tag chain
/// when the file is opened, reading headers and stepping over payloads: eleven bytes per tag,
/// whatever the length of the film, and no way round it short of reporting streams the file never
/// declared.
/// <para/>
/// Which stream is which comes out of that walk too. ffprobe numbers an FLV's streams in the order
/// their first tag appears rather than in the order the header's flags list them — a file muxed here
/// with sound and pictures has its audio tag first and is reported as stream 0 audio, stream 1 video,
/// which is the opposite of the flag order. This walk numbers them the same way.
/// <para/>
/// Nothing decodes here and nothing is decoded to describe a stream. The width and height reported
/// are the ones <c>onMetaData</c> states; they are not read out of a picture, and a file without an
/// <c>onMetaData</c> reports none rather than a size taken from a frame.
/// </remarks>
public static class FlvReader {

  /// <summary>The file header: <c>FLV</c>, a version, a flags byte and the offset of the first tag.</summary>
  private const int _HEADER_SIZE = 9;

  /// <summary>The only version the format has ever had.</summary>
  private const byte _VERSION = 1;

  private const byte _HAS_VIDEO = 0x01;
  private const byte _HAS_AUDIO = 0x04;

  /// <summary>The bit of a video tag's first byte that says the header is the extended, code-named one.</summary>
  private const byte _EXTENDED_VIDEO_HEADER = 0x80;

  /// <summary>The video codec id whose packets carry an AVC packet type and a composition time.</summary>
  private const int _AVC_CODEC = 7;

  /// <summary>The sound format id whose packets carry an AAC packet type.</summary>
  private const int _AAC_FORMAT = 10;

  /// <summary>An AVC or AAC packet carrying the decoder's configuration rather than a frame.</summary>
  private const byte _SEQUENCE_HEADER = 0;

  /// <summary>An AVC packet saying the sequence has ended, which is a marker and not a frame.</summary>
  private const byte _END_OF_SEQUENCE = 2;

  /// <summary>The bytes an AVC video tag spends before its payload: the codec byte, the packet type and a 24-bit composition time.</summary>
  private const int _AVC_PREFIX = 5;

  /// <summary>The bytes an AAC audio tag spends before its payload: the sound format byte and the packet type.</summary>
  private const int _AAC_PREFIX = 2;

  /// <summary>The frame type of a picture that may be decoded without anything before it.</summary>
  private const int _KEY_FRAME = 1;

  /// <summary>The frame type that is a command to a player rather than a picture.</summary>
  private const int _INFO_OR_COMMAND_FRAME = 5;

  /// <summary>
  /// The seconds one FLV timestamp stands for.
  /// </summary>
  /// <remarks>
  /// Fixed by the format at a millisecond, which is why every stream of every FLV has the same time
  /// base and why ffprobe reports <c>1/1000</c> for both streams of a file with sound.
  /// </remarks>
  private static readonly Rational _TIME_BASE = new(1, 1000);

  /// <summary>Reads an instance from the specified file.</summary>
  public static FlvContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("FLV file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  /// <summary>Reads an instance from the specified stream.</summary>
  public static FlvContainer FromStream(Stream stream) {
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
  public static FlvContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    return _Parse(data);
  }

  /// <summary>
  /// Reads an FLV out of a span.
  /// </summary>
  /// <remarks>
  /// The bytes are copied once here, and only here. A container has to outlive the call that built
  /// it — its packets are windows onto the file and are walked long afterwards — and a span promises
  /// nothing about how long the memory behind it stays valid. Callers that already hold an array
  /// should use <see cref="FromBytes"/>, which keeps theirs.
  /// </remarks>
  public static FlvContainer FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _HEADER_SIZE)
      throw new InvalidDataException("Data is too small to be a valid FLV file.");

    return _Parse(data.ToArray());
  }

  private static FlvContainer _Parse(byte[] data) {
    if (data.Length < _HEADER_SIZE)
      throw new InvalidDataException("Data is too small to be a valid FLV file.");

    if (data[0] != (byte)'F' || data[1] != (byte)'L' || data[2] != (byte)'V')
      throw new InvalidDataException("Invalid FLV signature.");

    if (data[3] != _VERSION)
      throw new InvalidDataException($"FLV version {data[3]} is not a version of this format; only version {_VERSION} was ever defined.");

    // The offset of the first tag is stated rather than fixed, so a writer may put something of its
    // own between the header and the body. Every file measured here states nine, which is the header
    // and nothing after it.
    var declaredOffset = BinaryPrimitives.ReadUInt32BigEndian(new ReadOnlySpan<byte>(data, 5, 4));
    if (declaredOffset < _HEADER_SIZE || declaredOffset > (uint)data.Length)
      throw new InvalidDataException(
        $"The header states the first tag at offset {declaredOffset}, which is outside a file of {data.Length} bytes.");

    var file = new ReadOnlyMemory<byte>(data);
    var firstTag = (int)declaredOffset;
    var (streams, metadata, audio, video) = _Describe(file, firstTag);

    return new() {
      File = file,
      FirstTagOffset = firstTag,
      StreamInfos = streams,
      FileMetadata = metadata,
      AudioStream = audio,
      VideoStream = video,
    };
  }

  // ------------------------------------------------------------------------------------------
  // Describing the streams
  // ------------------------------------------------------------------------------------------

  /// <summary>What the walk has learned about one stream so far.</summary>
  private sealed class _Draft {

    public int Index;
    public MediaStreamKind Kind;
    public int Code = -1;
    public ReadOnlyMemory<byte> Configuration;
  }

  /// <summary>
  /// Walks the whole tag chain, learning which streams exist and how they are coded.
  /// </summary>
  /// <remarks>
  /// The whole chain and not the first few tags, because stopping early is only safe when the file's
  /// own flags are right about what it holds — and a stream that turned up after the walk had stopped
  /// would have packets belonging to a stream nobody declared. The cost is eleven bytes read per tag
  /// with the payloads stepped over, which for a two-hour recording is a few million bytes touched
  /// out of a few billion, and the file is already in memory by the time this runs.
  /// <para/>
  /// The header's flags say whether sound and pictures are present, and they are deliberately not
  /// used: they are what the writer intended, where the tags are what is there. A file whose flags
  /// claim sound it does not carry would otherwise be reported with an audio stream of no packets and
  /// no codec, and one whose flags forgot to mention its pictures would have every video tag belong
  /// to a stream nobody declared.
  /// </remarks>
  private static (MediaStreamInfo[] Streams, VideoMetadata Metadata, int Audio, int Video) _Describe(
    ReadOnlyMemory<byte> file, int firstTag) {
    var order = new List<_Draft>();
    _Draft? audio = null, video = null;
    Amf0Value? announced = null;

    foreach (var tag in FlvTagScanner.Walk(file, firstTag)) {
      // A filtered tag's payload begins with a filter header rather than with the byte naming the
      // codec, so nothing about the stream can be read out of it. It is not refused here — a file may
      // carry one filtered tag among plain ones — but the packet walk refuses by name on reaching it.
      if (tag.Filtered)
        continue;

      switch (tag.Type) {
        case FlvTagType.Script:
          announced ??= _ReadScript(tag);
          break;

        case FlvTagType.Video:
          _NoteVideo(tag, order, ref video);
          break;

        case FlvTagType.Audio:
          _NoteAudio(tag, order, ref audio);
          break;

        // A tag type the format does not define. Its length is stated like any other's, so the walk
        // steps over it; inventing a stream for it would put a stream in the list that no packet ever
        // belongs to.
        default:
          break;
      }
    }

    var streams = new MediaStreamInfo[order.Count];
    for (var i = 0; i < order.Count; ++i)
      streams[i] = order[i].Kind == MediaStreamKind.Video ? _VideoStream(order[i], announced) : _AudioStream(order[i]);

    return (streams, _Metadata(announced, streams), audio?.Index ?? -1, video?.Index ?? -1);
  }

  private static void _NoteVideo(FlvTag tag, List<_Draft> order, ref _Draft? video) {
    var payload = tag.Data.Span;
    if (payload.IsEmpty)
      return;

    if ((payload[0] & _EXTENDED_VIDEO_HEADER) != 0)
      throw new NotSupportedException(
        $"The video tag at offset {tag.Offset} uses the extended header, which names its codec by a four-character code and lays its payload out differently. "
        + "This reader reads the original header only.");

    if (video == null) {
      video = new() { Index = order.Count, Kind = MediaStreamKind.Video };
      order.Add(video);
    }

    if (video.Code < 0)
      video.Code = payload[0] & 0x0F;

    // The AVC configuration record is codec-private data and not a frame: it describes how the
    // packets after it are coded and holds no picture. ffprobe reports it as this stream's extradata
    // and does not count it among the packets, which is why it is taken here rather than handed out
    // by the walk.
    if (video.Code == _AVC_CODEC
        && video.Configuration.IsEmpty
        && payload.Length > _AVC_PREFIX
        && payload[1] == _SEQUENCE_HEADER)
      video.Configuration = tag.Data[_AVC_PREFIX..];
  }

  private static void _NoteAudio(FlvTag tag, List<_Draft> order, ref _Draft? audio) {
    var payload = tag.Data.Span;
    if (payload.IsEmpty)
      return;

    if (audio == null) {
      audio = new() { Index = order.Count, Kind = MediaStreamKind.Audio };
      order.Add(audio);
    }

    if (audio.Code < 0)
      audio.Code = payload[0] >> 4;

    // The AAC audio specific config, for the same reason the AVC one is taken: ffprobe reports it as
    // extradata and counts the packets from the tag after it.
    if (audio.Code == _AAC_FORMAT
        && audio.Configuration.IsEmpty
        && payload.Length > _AAC_PREFIX
        && payload[1] == _SEQUENCE_HEADER)
      audio.Configuration = tag.Data[_AAC_PREFIX..];
  }

  private static MediaStreamInfo _VideoStream(_Draft draft, Amf0Value? metadata) {
    var width = _Whole(metadata?["width"]);
    var height = _Whole(metadata?["height"]);

    return new() {
      Index = draft.Index,
      Kind = MediaStreamKind.Video,
      Codec = _VideoCodec(draft.Code),
      // The container's own numbering, kept beside the code so a refusal can name both. FLV numbers
      // its codecs where every other container here writes four characters, and the number is the
      // only thing that was actually in the file.
      Handler = draft.Code < 0 ? CodecTag.None : new((uint)draft.Code),
      TimeBase = _TIME_BASE,
      FrameRate = _Ratio(metadata?["framerate"] ?? metadata?["videoframerate"]),
      Width = width,
      Height = height,
      CodecPrivateData = draft.Configuration,
    };
  }

  private static MediaStreamInfo _AudioStream(_Draft draft)
    => new() {
      Index = draft.Index,
      Kind = MediaStreamKind.Audio,
      Codec = _AudioCodec(draft.Code),
      Handler = draft.Code < 0 ? CodecTag.None : new((uint)draft.Code),
      TimeBase = _TIME_BASE,
      CodecPrivateData = draft.Configuration,
    };

  // ------------------------------------------------------------------------------------------
  // Packets
  // ------------------------------------------------------------------------------------------

  /// <summary>Walks the packets of a container, optionally of one stream only.</summary>
  internal static IEnumerable<CodedPacket> Walk(FlvContainer container, int? onlyStream) {
    foreach (var tag in FlvTagScanner.Walk(container.File, container.FirstTagOffset)) {
      if (tag.Type == FlvTagType.Script)
        continue;

      // A payload behind a filter header is not the codec's bytes: it is the filter's, and what the
      // filter did to them is stated in a header this reader does not read. Handing it out as a
      // packet would hand out ciphertext as though it were a frame.
      if (tag.Filtered)
        throw new NotSupportedException(
          $"The tag at offset {tag.Offset} is marked as filtered, so its payload is preceded by a filter header and is not the coded bytes. This reader reads unfiltered tags only.");

      var index = tag.Type switch {
        FlvTagType.Video => container.VideoStream,
        FlvTagType.Audio => container.AudioStream,
        _ => -1,
      };

      if (index < 0 || (onlyStream != null && index != onlyStream))
        continue;

      if (_TryPacket(tag, index, out var packet))
        yield return packet;
    }
  }

  private static bool _TryPacket(FlvTag tag, int index, out CodedPacket packet) {
    packet = default;

    var payload = tag.Data.Span;
    if (payload.IsEmpty)
      return false;

    var start = 1;
    var composition = 0;
    var isKeyFrame = true;

    if (tag.Type == FlvTagType.Video) {
      if ((payload[0] & _EXTENDED_VIDEO_HEADER) != 0)
        throw new NotSupportedException(
          $"The video tag at offset {tag.Offset} uses the extended header, which names its codec by a four-character code and lays its payload out differently. "
          + "This reader reads the original header only.");

      var frameType = payload[0] >> 4;

      // Frame type 5 is a command to the player — the specification's "video info/command frame" —
      // and carries no picture at all. It is not a packet of the stream.
      if (frameType == _INFO_OR_COMMAND_FRAME)
        return false;

      // Only the specification's key frame. ffprobe flags K on exactly the tags whose frame type is
      // one and on none of the others, which for the AVC file measured here is its first packet alone.
      isKeyFrame = frameType == _KEY_FRAME;

      if ((payload[0] & 0x0F) == _AVC_CODEC) {
        if (payload.Length < _AVC_PREFIX)
          throw new InvalidDataException(
            $"The AVC video tag at offset {tag.Offset} holds {payload.Length} bytes, which is fewer than the packet type and composition time that precede its payload.");

        // Packet type 0 is the configuration record, taken when the file was opened; packet type 2
        // says the sequence has ended and carries nothing. Neither is a frame, and ffprobe counts
        // neither among the packets.
        if (payload[1] is _SEQUENCE_HEADER or _END_OF_SEQUENCE)
          return false;

        composition = _Signed24(payload, 2);
        start = _AVC_PREFIX;
      }
    } else if ((payload[0] >> 4) == _AAC_FORMAT) {
      if (payload.Length < _AAC_PREFIX)
        throw new InvalidDataException(
          $"The AAC audio tag at offset {tag.Offset} holds {payload.Length} bytes, which is fewer than the packet type that precedes its payload.");

      if (payload[1] == _SEQUENCE_HEADER)
        return false;

      start = _AAC_PREFIX;
    }

    // A tag holding its header and nothing else carries no coded bytes. ffprobe does not invent a
    // packet for one, and neither does the AVI reader for a zero-length chunk.
    if (tag.Data.Length <= start)
      return false;

    // The composition time is how far the picture's presentation is ahead of its decoding, and it is
    // signed: a stream with bidirectional prediction has pictures due before the one being decoded.
    // The decode timestamp is the tag's own, whatever the codec.
    packet = new(index, tag.Data[start..], tag.Timestamp + composition, tag.Timestamp, IsKeyFrame: isKeyFrame);
    return true;
  }

  /// <summary>Reads a 24-bit two's-complement number, which is how a composition time is written.</summary>
  private static int _Signed24(ReadOnlySpan<byte> data, int at) {
    var value = (data[at] << 16) | (data[at + 1] << 8) | data[at + 2];
    return (value & 0x800000) != 0 ? value - 0x1000000 : value;
  }

  // ------------------------------------------------------------------------------------------
  // Metadata
  // ------------------------------------------------------------------------------------------

  /// <summary>Reads a script tag, which is the name of a message followed by its payload.</summary>
  /// <remarks>
  /// Only <c>onMetaData</c> is kept. The others a writer may emit — <c>onCuePoint</c>,
  /// <c>onTextData</c>, whatever a server invented — are messages to a player at a moment in the
  /// film rather than statements about the file, and there is nowhere in
  /// <see cref="VideoMetadata"/> for a thing that happens at a time.
  /// </remarks>
  private static Amf0Value? _ReadScript(FlvTag tag) {
    var data = tag.Data.Span;
    var at = 0;
    if (!Amf0Reader.TryReadValue(data, ref at, out var name) || name.Kind != Amf0Kind.String || name.Text != "onMetaData")
      return null;

    return Amf0Reader.TryReadValue(data, ref at, out var body) && body.Properties != null ? body : null;
  }

  /// <summary>Turns what <c>onMetaData</c> announced into the container-independent model.</summary>
  private static VideoMetadata _Metadata(Amf0Value? announced, MediaStreamInfo[] streams) {
    var streamMetadata = new MediaStreamMetadata[streams.Length];
    for (var i = 0; i < streams.Length; ++i)
      streamMetadata[i] = new(streams[i].Index, streams[i].Kind, streams[i].Codec, streams[i].Language, streams[i].Name);

    if (announced?.Properties == null)
      return new() { Streams = streamMetadata };

    string? title = null, artist = null, album = null, encodedBy = null;
    DateTimeOffset? created = null;
    var texts = new List<TextMetadataEntry>();

    foreach (var property in announced.Properties)
      switch (property.Name) {
        case "title": title ??= property.Value.Text; break;
        case "artist": artist ??= property.Value.Text; break;
        case "album": album ??= property.Value.Text; break;

        // Two names for the same thing. ffmpeg writes 'encoder'; the Flash-era tools wrote
        // 'metadatacreator', and files carrying both name the same program twice.
        case "encoder":
        case "metadatacreator": encodedBy ??= property.Value.Text; break;

        case "comment": _Text(texts, "Comment", property.Value); break;
        case "copyright": _Text(texts, "Copyright", property.Value); break;

        case "creationdate":
        case "datecreated":
          created ??= _Instant(property.Value);
          if (created == null)
            _Text(texts, "Creation Time", property.Value);
          break;

        // The rest of what a writer states about the film is measurements — the sizes, the rates,
        // the codec numbers, the keyframe index — and they are read where they belong rather than
        // repeated here as text. Anything else that is text is somebody's annotation and is kept
        // under the name it was written with.
        case "duration":
        case "width":
        case "height":
        case "framerate":
        case "videoframerate":
        case "videocodecid":
        case "videodatarate":
        case "audiocodecid":
        case "audiodatarate":
        case "audiosamplerate":
        case "audiosamplesize":
        case "audiodelay":
        case "stereo":
        case "filesize":
        case "lasttimestamp":
        case "lastkeyframetimestamp":
        case "hasVideo":
        case "hasAudio":
        case "hasMetadata":
        case "hasKeyframes":
        case "hasCuePoints":
        case "canSeekToEnd":
        case "keyframes":
        case "cuePoints":
          break;

        default: _Text(texts, property.Name, property.Value); break;
      }

    // The file's own claim, in seconds, and not a total of the tags. A recording that was cut off
    // keeps whatever duration was written into its header before it stopped — and ffmpeg writes this
    // one by seeking back to the script tag after the last packet, so a file that never got that far
    // states zero.
    var duration = announced["duration"];
    var seconds = duration is { Kind: Amf0Kind.Number } ? duration.Number : 0d;

    return new() {
      Title = title,
      Artist = artist,
      Album = album,
      EncodedBy = encodedBy,
      CreationTime = created,
      Duration = seconds > 0d ? TimeSpan.FromTicks((long)(seconds * TimeSpan.TicksPerSecond)) : null,
      Streams = streamMetadata,
      TextEntries = texts,
    };
  }

  private static void _Text(List<TextMetadataEntry> texts, string keyword, Amf0Value value) {
    if (value.Kind == Amf0Kind.String && !string.IsNullOrWhiteSpace(value.Text))
      texts.Add(new(keyword, value.Text!.Trim()));
  }

  /// <summary>Reads a creation date, which a writer may state as an AMF0 date or as text.</summary>
  private static DateTimeOffset? _Instant(Amf0Value value) {
    if (value.Kind == Amf0Kind.Date)
      return DateTimeOffset.FromUnixTimeMilliseconds((long)value.Number);

    if (value.Kind != Amf0Kind.String || value.Text == null)
      return null;

    return DateTimeOffset.TryParse(value.Text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
      ? parsed
      : null;
  }

  /// <summary>A stated number as a whole one, or zero where nothing usable was stated.</summary>
  private static int _Whole(Amf0Value? value)
    => value is { Kind: Amf0Kind.Number, Number: > 0d and <= int.MaxValue } ? (int)value.Number : 0;

  /// <summary>
  /// Turns a stated rate into the exact ratio the file meant by it.
  /// </summary>
  /// <remarks>
  /// AMF0 has one number and it is a double, so a rate arrives here already rounded — which is the
  /// whole reason <see cref="Rational"/> exists. A whole number is exact and is reported as itself
  /// over one. Anything else is reported as the file states it, to three decimal places: an FLV
  /// announcing 29.97 is announcing 2997/100 and not 30000/1001, and turning the one into the other
  /// would be reporting a rate the file never claimed.
  /// </remarks>
  private static Rational _Ratio(Amf0Value? value) {
    if (value is not { Kind: Amf0Kind.Number } || value.Number <= 0d || value.Number > 1_000_000d)
      return Rational.Unknown;

    var whole = (long)Math.Round(value.Number);
    if (Math.Abs(value.Number - whole) < 1e-9)
      return new(whole, 1);

    var numerator = (long)Math.Round(value.Number * 1000d);
    var divisor = _GreatestCommonDivisor(numerator, 1000);
    return new(numerator / divisor, 1000 / divisor);
  }

  private static long _GreatestCommonDivisor(long a, long b) {
    while (b != 0)
      (a, b) = (b, a % b);

    return a == 0 ? 1 : a;
  }

  // ------------------------------------------------------------------------------------------
  // Codec codes
  // ------------------------------------------------------------------------------------------

  /// <summary>
  /// The four-character code the world names an FLV video codec by.
  /// </summary>
  /// <remarks>
  /// FLV is the only container here that numbers its codecs instead of naming them, and a number is
  /// no use to a decoder that was written against the other containers: an AVI of Sorenson Spark says
  /// <c>FLV1</c> and an FLV of it says 2, and a decoder should not have to know both. So the number is
  /// translated into the code the same stream carries everywhere else, and the number itself is kept
  /// in <see cref="MediaStreamInfo.Handler"/> so nothing is lost.
  /// <para/>
  /// A code this table does not know is reported as the number, which is what the file actually held.
  /// </remarks>
  private static CodecTag _VideoCodec(int code)
    => code switch {
      1 => CodecTag.FromCharacters("MJPG"),
      2 => CodecTag.FromCharacters("FLV1"),
      3 => CodecTag.FromCharacters("FSV1"),
      4 => CodecTag.FromCharacters("VP6F"),
      5 => CodecTag.FromCharacters("VP6A"),
      6 => CodecTag.FromCharacters("FSV2"),
      7 => CodecTag.FromCharacters("H264"),
      < 0 => CodecTag.None,
      _ => new((uint)code),
    };

  /// <summary>
  /// The four-character code the world names an FLV sound format by, where there is one.
  /// </summary>
  /// <remarks>
  /// Fewer of these than of the video codes, because fewer of them have a code anybody agrees on. The
  /// ones here are the QuickTime sample entry types, which is the vocabulary the ISO base media
  /// reader beside this one already hands out. The rest — Flash's own ADPCM, Nellymoser, Speex,
  /// platform-endian PCM — have no such code, and inventing one would put a name in the model that no
  /// other reader would ever produce; the number is reported instead and stays readable in
  /// <see cref="MediaStreamInfo.Handler"/> either way.
  /// </remarks>
  private static CodecTag _AudioCodec(int code)
    => code switch {
      2 or 14 => CodecTag.FromCharacters(".mp3"),
      3 => CodecTag.FromCharacters("sowt"),
      7 => CodecTag.FromCharacters("alaw"),
      8 => CodecTag.FromCharacters("ulaw"),
      10 => CodecTag.FromCharacters("mp4a"),
      < 0 => CodecTag.None,
      _ => new((uint)code),
    };
}
