using System.Collections.Generic;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.MpegVideo;

/// <summary>Writes coded MPEG-1 or MPEG-2 video packets as an elementary video stream.</summary>
public sealed class MpegVideoWriter : IVideoContainerWriter<MpegVideoWriter> {

  private readonly ElementaryStreamMuxer _muxer;

  private MpegVideoWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata)
    => this._muxer = new(streams, metadata, "MPEG elementary video stream",
      static stream => stream.Codec == CodecTag.FromCharacters("MPG1") || stream.Codec == CodecTag.FromCharacters("MPG2"));

  public static string PrimaryExtension => ".m1v";

  public static string[] FileExtensions =>
    [".m1v", ".m2v", ".mpv", ".mpeg1video", ".mpeg2video", ".m1v1", ".m2v1"];

  public static MpegVideoWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata)
    => new(streams, metadata);

  public void WritePacket(CodedPacket packet) => this._muxer.WritePacket(packet);

  public byte[] Finish() => this._muxer.Finish();
}
