using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.RoqVideo;

/// <summary>Writes RoQ video chunks exactly as the video demuxer exposes them.</summary>
public sealed class RoqWriter : IVideoContainerWriter<RoqWriter> {

  private readonly MemoryStream _output = new();
  private bool _finished;

  private RoqWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) {
    ArgumentNullException.ThrowIfNull(streams);
    ArgumentNullException.ThrowIfNull(metadata);
    if (streams.Count != 1 || streams[0].Index != 0 || streams[0].Kind != MediaStreamKind.Video
        || !streams[0].Codec.EqualsIgnoringCase(CodecTag.FromCharacters("RoQV")))
      throw new NotSupportedException(
        "This RoQ muxer writes the video logical stream. RoQ audio demux currently discards each sound chunk's DPCM seed argument, so accepting an audio stream would change decoded sound rather than remux it.");
    this._output.Write(RoqReader.Signature);
  }

  public static string PrimaryExtension => ".roq";
  public static string[] FileExtensions => [".roq"];
  public static RoqWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams, metadata);

  public void WritePacket(CodedPacket packet) {
    if (this._finished) throw new InvalidOperationException("RoQ writer has already been finished.");
    if (packet.StreamIndex != 0) throw new ArgumentOutOfRangeException(nameof(packet));
    var data = packet.Data.Span;
    if (data.Length < 8)
      throw new InvalidDataException("A RoQ video packet must include its eight-byte codec chunk header.");
    var size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(2, 4));
    if (size != data.Length - 8)
      throw new InvalidDataException($"RoQ packet header states {size} payload bytes but the packet carries {data.Length - 8}.");
    this._output.Write(data);
  }

  public byte[] Finish() {
    if (this._finished) throw new InvalidOperationException("RoQ writer has already been finished.");
    this._finished = true;
    return this._output.ToArray();
  }
}
