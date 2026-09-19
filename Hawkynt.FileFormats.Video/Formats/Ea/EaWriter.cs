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
public sealed class EaWriter : IVideoContainerWriter<EaWriter> {

  private readonly IReadOnlyList<MediaStreamInfo> _streams;
  private readonly MemoryStream _output = new();
  private bool _finished;

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
        if (videoSeen || !(stream.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("cmv ")) || stream.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("tgv "))))
          throw new NotSupportedException("EA video must be a single CMV or TGV logical stream.");
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
  public static string[] FileExtensions => [".wve", ".cmv", ".tgv", ".uv", ".uv2"];
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

    if (!EaChunkType.IsTgv(fourCc))
      throw new InvalidDataException("A TGV packet must carry only kVGT/fVGT chunks.");
  }

  public byte[] Finish() {
    if (this._finished)
      throw new InvalidOperationException("EA writer has already been finished.");
    this._finished = true;
    if (this._output.Length == 0)
      throw new InvalidDataException("An EA multimedia file needs at least one chunk.");
    return this._output.ToArray();
  }
}
