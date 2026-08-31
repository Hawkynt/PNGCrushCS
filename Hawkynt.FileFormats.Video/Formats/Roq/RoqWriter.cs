using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.RoqVideo;

/// <summary>Writes RoQ video chunks verbatim and sound chunks with their preserved predictor arguments.</summary>
public sealed class RoqWriter : IVideoContainerWriter<RoqWriter> {

  private readonly IReadOnlyList<MediaStreamInfo> _streams;
  private readonly MemoryStream _output = new();
  private bool _finished;

  private RoqWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) {
    ArgumentNullException.ThrowIfNull(streams);
    ArgumentNullException.ThrowIfNull(metadata);
    if (streams.Count is < 1 or > 2 || streams[0].Index != 0 || streams[0].Kind != MediaStreamKind.Video
        || !streams[0].Codec.EqualsIgnoringCase(CodecTag.FromCharacters("RoQV")))
      throw new NotSupportedException("RoQ needs RoQV video at stream zero and optionally one RoQ sound stream at index one.");

    if (streams.Count == 2) {
      var audio = streams[1];
      if (audio.Index != 1 || audio.Kind != MediaStreamKind.Audio
          || !(audio.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("RoQM"))
               || audio.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("RoQS"))))
        throw new NotSupportedException("RoQ's optional second stream must be RoQM mono or RoQS stereo sound.");
    }

    this._streams = streams;
    this._output.Write(RoqReader.Signature);
  }

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".roq";
  /// <summary>Gets the file extensions supported by this format.</summary>
  public static string[] FileExtensions => [".roq"];
  /// <summary>Creates a writer for the specified stream descriptions and metadata.</summary>
  public static RoqWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams, metadata);

  /// <summary>Writes the specified coded packet to the container.</summary>
  public void WritePacket(CodedPacket packet) {
    if (this._finished)
      throw new InvalidOperationException("RoQ writer has already been finished.");
    if ((uint)packet.StreamIndex >= (uint)this._streams.Count)
      throw new ArgumentOutOfRangeException(nameof(packet));

    if (packet.StreamIndex == 0) {
      var data = packet.Data.Span;
      if (data.Length < 8)
        throw new InvalidDataException("A RoQ video packet must include its eight-byte codec chunk header.");
      var size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(2, 4));
      if (size != data.Length - 8)
        throw new InvalidDataException($"RoQ packet header states {size} payload bytes but the packet carries {data.Length - 8}.");
      this._output.Write(data);
      return;
    }

    if (packet.ContainerPrivateData.Length != 2)
      throw new NotSupportedException(
        "A RoQ sound packet needs the original two-byte chunk argument in ContainerPrivateData; it is the DPCM predictor seed and cannot be invented.");

    var audio = this._streams[1];
    var id = audio.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("RoQS"))
      ? RoqChunkType.SOUND_STEREO
      : RoqChunkType.SOUND_MONO;
    ContainerWriterTools.WriteUInt16LittleEndian(this._output, id);
    ContainerWriterTools.WriteUInt32LittleEndian(this._output, checked((uint)packet.Data.Length));
    this._output.Write(packet.ContainerPrivateData.Span);
    this._output.Write(packet.Data.Span);
  }

  /// <summary>Finishes writing the container and returns its encoded bytes.</summary>
  public byte[] Finish() {
    if (this._finished)
      throw new InvalidOperationException("RoQ writer has already been finished.");
    this._finished = true;
    return this._output.ToArray();
  }
}
