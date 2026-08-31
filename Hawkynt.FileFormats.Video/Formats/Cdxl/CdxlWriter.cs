using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Cdxl;

/// <summary>Writes CDXL chunks by pairing each demux-shaped video packet with its following PCM packet.</summary>
public sealed class CdxlWriter : IVideoContainerWriter<CdxlWriter> {

  private readonly bool _hasAudio;
  private readonly MemoryStream _output = new();
  private ReadOnlyMemory<byte>? _pendingVideo;
  private bool _finished;

  private CdxlWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) {
    ArgumentNullException.ThrowIfNull(streams);
    ArgumentNullException.ThrowIfNull(metadata);
    if (streams.Count is < 1 or > 2 || streams[0].Index != 0 || streams[0].Kind != MediaStreamKind.Video
        || !streams[0].Codec.EqualsIgnoringCase(CodecTag.FromCharacters("CDXL")))
      throw new NotSupportedException("CDXL needs CDXL video at stream 0 and optionally its raw PCM view at stream 1.");
    if (streams.Count == 2 && (streams[1].Index != 1 || streams[1].Kind != MediaStreamKind.Audio))
      throw new NotSupportedException("CDXL's optional second stream is audio at index 1.");
    this._hasAudio = streams.Count == 2;
  }

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".cdxl";
  /// <summary>Gets the file extensions supported by this format.</summary>
  public static string[] FileExtensions => [".cdxl"];
  /// <summary>Creates a writer for the specified stream descriptions and metadata.</summary>
  public static CdxlWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams, metadata);

  /// <summary>Writes the specified coded packet to the container.</summary>
  public void WritePacket(CodedPacket packet) {
    if (this._finished) throw new InvalidOperationException("CDXL writer has already been finished.");
    if (packet.StreamIndex == 0) {
      if (this._pendingVideo != null)
        throw new InvalidDataException("A second CDXL video packet arrived before the previous frame's audio packet.");
      if (packet.Data.Length < CdxlChunkReader.HeaderLength || !CdxlChunkReader.LooksPlausible(packet.Data.Span))
        throw new InvalidDataException("CDXL video packet must contain its complete 32-byte frame header, palette and pixels.");
      if (!this._hasAudio)
        this._WriteFrame(packet.Data, ReadOnlyMemory<byte>.Empty);
      else
        this._pendingVideo = packet.Data;
      return;
    }

    if (packet.StreamIndex != 1 || !this._hasAudio)
      throw new ArgumentOutOfRangeException(nameof(packet));
    if (this._pendingVideo is not { } video)
      throw new InvalidDataException("CDXL audio packet arrived without a preceding video packet for the same chunk.");
    this._WriteFrame(video, packet.Data);
    this._pendingVideo = null;
  }

  /// <summary>Finishes writing the container and returns its encoded bytes.</summary>
  public byte[] Finish() {
    if (this._finished) throw new InvalidOperationException("CDXL writer has already been finished.");
    if (this._pendingVideo != null)
      throw new InvalidDataException("CDXL file ends with a video frame whose audio packet is missing.");
    this._finished = true;
    return this._output.ToArray();
  }

  private void _WriteFrame(ReadOnlyMemory<byte> video, ReadOnlyMemory<byte> audio) {
    if (audio.Length > ushort.MaxValue)
      throw new NotSupportedException("CDXL sound size exceeds its 16-bit header field.");
    var bytes = video.ToArray();
    var total = checked(bytes.Length + audio.Length);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(2, 4), checked((uint)total));
    BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(22, 2), checked((ushort)audio.Length));
    this._output.Write(bytes);
    this._output.Write(audio.Span);
  }
}
