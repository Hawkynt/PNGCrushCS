using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Ea;

/// <summary>
/// Writes Electronic Arts' self-delimiting video and audio-family chunks in packet order.
/// </summary>
/// <remarks>
/// A packet is allowed to contain several complete chunks from the same logical stream. That matters
/// for codecs such as CMV, where a palette/geometry <c>MVIh</c> state chunk may need to sit immediately
/// before the <c>MVIf</c> picture produced by the same encoder call. Demuxed packets containing one
/// chunk remain byte-for-byte replayable.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class EaWriter : IVideoContainerWriter<EaWriter> {

  private const int _HEADER_SEARCH = 5;
  private static readonly string[] _VideoCodecs = ["cmv ", "tgv ", "mad ", "tgq ", "tqi "];

  private readonly IReadOnlyList<MediaStreamInfo> _streams;
  private readonly MemoryStream _output = new();
  private bool _finished;
  private bool _preambleWritten;
  private int _chunks;

  private EaWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) {
    ArgumentNullException.ThrowIfNull(streams);
    ArgumentNullException.ThrowIfNull(metadata);
    if (streams.Count is < 1 or > 2)
      throw new NotSupportedException("EA multimedia muxing supports one video stream, one audio-family stream, or both.");

    var videoSeen = false;
    var audioSeen = false;
    for (var i = 0; i < streams.Count; ++i) {
      var stream = streams[i] ?? throw new ArgumentException($"EA stream {i} is null.", nameof(streams));
      if (stream.Index != i)
        throw new ArgumentException($"EA streams must be indexed densely; position {i} has index {stream.Index}.", nameof(streams));

      if (stream.Kind == MediaStreamKind.Video) {
        if (videoSeen || !Array.Exists(_VideoCodecs, tag => stream.Codec.EqualsIgnoringCase(CodecTag.FromCharacters(tag))))
          throw new NotSupportedException("EA video must be a single CMV, TGV, MAD, TGQ or TQI logical stream.");
        videoSeen = true;
      } else if (stream.Kind == MediaStreamKind.Audio) {
        if (audioSeen || !stream.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("EAAU")))
          throw new NotSupportedException("EA audio must be the EAAU chunk-protocol stream exposed by EaReader.");
        audioSeen = true;
      } else
        throw new NotSupportedException("EA muxing supports only the video and documented audio-family chunk streams.");
    }

    this._streams = streams;
  }

  public static string PrimaryExtension => ".wve";
  public static string[] FileExtensions => [".wve", ".cmv", ".tgv", ".uv", ".uv2", ".mad", ".tgq", ".tqi"];
  public static EaWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams, metadata);

  public void WritePacket(CodedPacket packet) {
    if (this._finished)
      throw new InvalidOperationException("EA writer has already been finished.");
    if ((uint)packet.StreamIndex >= (uint)this._streams.Count)
      throw new ArgumentOutOfRangeException(nameof(packet), packet.StreamIndex, "Packet names no declared EA stream.");

    var data = packet.Data.Span;
    if (data.IsEmpty)
      throw new InvalidDataException("EA packet must contain at least one complete chunk.");

    var stream = this._streams[packet.StreamIndex];

    // TGQ and TQI films open with a sound header, which FFmpeg's demuxer needs to recognise them; a
    // film written without sound gets one that declares none, and the frame rate.
    var tqi = stream.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("tqi "));
    if (!this._preambleWritten && this._output.Length == 0 && stream.Kind == MediaStreamKind.Video
        && (tqi || stream.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("tgq ")))
        && !_HasAudio(this._streams))
      this._WritePreamble(stream);
    this._preambleWritten = true;

    // FFmpeg's demuxer hands TQI pictures over without their chunk header, and so does the reader here.
    if (tqi) {
      this._WriteUInt32(EaChunkType.pIQT);
      this._WriteUInt32(checked((uint)data.Length + 8));
      this._output.Write(data);
      ++this._chunks;
      return;
    }

    for (var at = 0; at < data.Length;) {
      var remaining = data.Length - at;
      if (remaining < 8)
        throw new InvalidDataException($"EA packet ends with {remaining} byte(s), short of another eight-byte chunk header.");

      var chunk = data[at..];
      var fourCc = BinaryPrimitives.ReadUInt32LittleEndian(chunk);
      var size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
      if (size < 8)
        throw new InvalidDataException($"EA chunk at packet byte {at} states {size} bytes including itself, shorter than its header.");
      if (size > (uint)remaining)
        throw new InvalidDataException(
          $"EA chunk at packet byte {at} states {size} bytes including itself, but only {remaining} remain in the packet.");

      this._ValidateChunkKind(stream, fourCc);
      at += checked((int)size);
      ++this._chunks;
    }

    this._output.Write(data);
  }

  private void _ValidateChunkKind(MediaStreamInfo stream, uint fourCc) {
    if (stream.Kind == MediaStreamKind.Audio) {
      if (!EaChunkType.IsAudio(fourCc))
        throw new InvalidDataException("An EAAU packet must carry only documented EA sound-family chunk identifiers.");
      return;
    }

    if (stream.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("cmv "))) {
      if (!EaChunkType.IsCmv(fourCc))
        throw new InvalidDataException("A CMV packet must carry only MVIh/MVIf/MVIe chunks.");
      return;
    }

    if (stream.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("mad "))) {
      if (!EaChunkType.IsMad(fourCc))
        throw new InvalidDataException("A MAD packet must carry only MADk/MADm/MADe chunks.");
      return;
    }

    if (stream.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("tgq "))) {
      if (!EaChunkType.IsTgq(fourCc))
        throw new InvalidDataException("A TGQ packet must carry only TGQs/pQGT chunks.");
      return;
    }

    if (!EaChunkType.IsTgv(fourCc))
      throw new InvalidDataException("A TGV packet must carry only kVGT/fVGT chunks.");
  }

  private static bool _HasAudio(IReadOnlyList<MediaStreamInfo> streams) {
    foreach (var stream in streams)
      if (stream.Kind == MediaStreamKind.Audio)
        return true;
    return false;
  }

  private void _WriteUInt32(uint value) {
    Span<byte> bytes = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
    this._output.Write(bytes);
  }

  /// <summary>An SCHl header of no sound: the PT patch tag, the frame rate as element 0x1B, and the end byte.</summary>
  private void _WritePreamble(MediaStreamInfo video) {
    var rate = video.FrameRate.Numerator > 0 && video.FrameRate.Denominator > 0
      ? (int)Math.Clamp(Math.Round((double)video.FrameRate.Numerator / video.FrameRate.Denominator), 1, 255)
      : 15;
    byte[] payload = [(byte)'P', (byte)'T', 0, 0, 0x1B, 1, (byte)rate, 0xFF];
    this._WriteUInt32(EaChunkType.SCHl);
    this._WriteUInt32(checked((uint)payload.Length + 8));
    this._output.Write(payload);
    ++this._chunks;
  }

  public byte[] Finish() {
    if (this._finished)
      throw new InvalidOperationException("EA writer has already been finished.");
    this._finished = true;
    if (this._output.Length == 0)
      throw new InvalidDataException("An EA multimedia file needs at least one chunk.");

    // FFmpeg's demuxer reads up to five chunks looking for both a sound and a picture header, so a
    // silent film shorter than that would send it past the end; end chunks, which it steps over,
    // make up the difference.
    if (!_HasAudio(this._streams))
      for (; this._chunks < _HEADER_SEARCH; ++this._chunks) {
        this._WriteUInt32(EaChunkType.SCEl);
        this._WriteUInt32(8);
      }

    return this._output.ToArray();
  }
}
