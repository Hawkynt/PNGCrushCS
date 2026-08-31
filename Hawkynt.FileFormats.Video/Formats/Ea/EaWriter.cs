using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Ea;

/// <summary>
/// Replays Electronic Arts' self-delimiting video and audio-family chunks without parsing nested codec
/// patch headers. Both logical streams already expose complete eight-byte-header-plus-payload chunks.
/// </summary>
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

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".wve";
  /// <summary>Gets the file extensions supported by this format.</summary>
  public static string[] FileExtensions => [".wve", ".cmv", ".tgv", ".uv", ".uv2"];
  /// <summary>Creates a writer for the specified stream descriptions and metadata.</summary>
  public static EaWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams, metadata);

  /// <summary>Writes the specified coded packet to the container.</summary>
  public void WritePacket(CodedPacket packet) {
    if (this._finished)
      throw new InvalidOperationException("EA writer has already been finished.");
    if ((uint)packet.StreamIndex >= (uint)this._streams.Count)
      throw new ArgumentOutOfRangeException(nameof(packet), packet.StreamIndex, "Packet names no declared EA stream.");

    var data = packet.Data.Span;
    if (data.Length < 8)
      throw new InvalidDataException("EA packet must include its eight-byte chunk header.");
    var size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4, 4));
    if (size != data.Length)
      throw new InvalidDataException($"EA chunk header states {size} bytes including itself, packet carries {data.Length}.");

    var fourCc = BinaryPrimitives.ReadUInt32LittleEndian(data);
    var stream = this._streams[packet.StreamIndex];
    if (stream.Kind == MediaStreamKind.Audio) {
      if (!EaChunkType.IsAudio(fourCc))
        throw new InvalidDataException("An EAAU packet must carry one of the documented EA sound-family chunk identifiers.");
    } else if (stream.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("cmv "))) {
      if (!EaChunkType.IsCmv(fourCc))
        throw new InvalidDataException("A CMV packet must carry an MVIh/MVIf/MVIe chunk.");
    } else if (!EaChunkType.IsTgv(fourCc))
      throw new InvalidDataException("A TGV packet must carry a kVGT/fVGT chunk.");

    this._output.Write(data);
  }

  /// <summary>Finishes writing the container and returns its encoded bytes.</summary>
  public byte[] Finish() {
    if (this._finished)
      throw new InvalidOperationException("EA writer has already been finished.");
    this._finished = true;
    if (this._output.Length == 0)
      throw new InvalidDataException("An EA multimedia file needs at least one chunk.");
    return this._output.ToArray();
  }
}
