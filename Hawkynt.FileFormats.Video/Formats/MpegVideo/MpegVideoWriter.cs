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

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".m1v";

  /// <summary>Gets the file extensions supported by this format.</summary>
  public static string[] FileExtensions =>
    [".m1v", ".m2v", ".mpv", ".mpeg1video", ".mpeg2video", ".m1v1", ".m2v1"];

  /// <summary>Creates a writer for the specified stream descriptions and metadata.</summary>
  public static MpegVideoWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata)
    => new(streams, metadata);

  /// <summary>Writes the specified coded packet to the container.</summary>
  public void WritePacket(CodedPacket packet) => this._muxer.WritePacket(packet);

  /// <summary>Finishes writing the container and returns its encoded bytes.</summary>
  public byte[] Finish() => this._muxer.Finish();
}
