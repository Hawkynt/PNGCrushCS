using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Ea;

/// <summary>Writes the Electronic Arts flat chunk stream exactly as video packets expose it.</summary>
public sealed class EaWriter : IVideoContainerWriter<EaWriter> {

  private readonly MemoryStream _output = new();
  private bool _finished;

  private EaWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) {
    ArgumentNullException.ThrowIfNull(streams);
    ArgumentNullException.ThrowIfNull(metadata);
    if (streams.Count != 1 || streams[0].Index != 0 || streams[0].Kind != MediaStreamKind.Video
        || !(streams[0].Codec.EqualsIgnoringCase(CodecTag.FromCharacters("cmv ")) || streams[0].Codec.EqualsIgnoringCase(CodecTag.FromCharacters("tgv "))))
      throw new NotSupportedException("EA multimedia muxing currently writes the CMV/TGV video stream exposed by the demuxer; audio chunks are not exposed as packets yet.");
  }

  public static string PrimaryExtension => ".wve";
  public static string[] FileExtensions => [".wve", ".cmv", ".tgv", ".uv", ".uv2"];
  public static EaWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams, metadata);

  public void WritePacket(CodedPacket packet) {
    if (this._finished) throw new InvalidOperationException("EA writer has already been finished.");
    if (packet.StreamIndex != 0) throw new ArgumentOutOfRangeException(nameof(packet));
    var data = packet.Data.Span;
    if (data.Length < 8)
      throw new InvalidDataException("EA packet must include its eight-byte chunk header.");
    var size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4, 4));
    if (size != data.Length)
      throw new InvalidDataException($"EA chunk header states {size} bytes including itself, packet carries {data.Length}.");
    this._output.Write(data);
  }

  public byte[] Finish() {
    if (this._finished) throw new InvalidOperationException("EA writer has already been finished.");
    this._finished = true;
    return this._output.ToArray();
  }
}
