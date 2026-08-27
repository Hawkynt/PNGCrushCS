using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Mjpeg;

/// <summary>Writes complete JPEG frames one after another as a raw Motion JPEG stream.</summary>
public sealed class MjpegWriter : IVideoContainerWriter<MjpegWriter> {

  private readonly ElementaryStreamMuxer _muxer;

  private MjpegWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata)
    => this._muxer = new(streams, metadata, "Motion JPEG stream",
      static stream => stream.Codec == CodecTag.FromCharacters("MJPG"));

  public static string PrimaryExtension => ".mjpg";

  public static string[] FileExtensions => [".mjpg", ".mjpeg"];

  public static MjpegWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata)
    => new(streams, metadata);

  public void WritePacket(CodedPacket packet) {
    var span = packet.Data.Span;
    if (span.Length < 4 || span[0] != 0xFF || span[1] != 0xD8 || span[^2] != 0xFF || span[^1] != 0xD9)
      throw new InvalidDataException("A Motion JPEG packet must be one complete JPEG, from SOI (FF D8) through EOI (FF D9).");

    this._muxer.WritePacket(packet);
  }

  public byte[] Finish() => this._muxer.Finish();
}
