using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.MpegVideo;

/// <summary>Writes coded MPEG-1 or MPEG-2 video packets as an elementary video stream.</summary>
public sealed class MpegVideoWriter : IVideoContainerWriter<MpegVideoWriter> {

  private readonly ElementaryStreamMuxer _muxer;

  private MpegVideoWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata)
    => this._muxer = new(streams, metadata, "MPEG elementary video stream",
      static stream => stream.CodecPrivateData.IsEmpty
        && (stream.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("MPG1"))
          || stream.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("MPG2"))));

  public static string PrimaryExtension => ".m1v";

  public static string[] FileExtensions =>
    [".m1v", ".m2v", ".mpv", ".mpeg1video", ".mpeg2video", ".m1v1", ".m2v1"];

  public static MpegVideoWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata)
    => new(streams, metadata);

  public void WritePacket(CodedPacket packet) {
    var trailer = packet.ContainerPrivateData.Span;
    if (trailer.IsEmpty) {
      this._muxer.WritePacket(packet);
      return;
    }

    if (trailer.Length < 4
        || trailer[^4] != 0x00 || trailer[^3] != 0x00 || trailer[^2] != 0x01 || trailer[^1] != MpegVideoReader.SequenceEndCode)
      throw new InvalidDataException(
        "MPEG elementary packet private data must end in the sequence-end start code 00 00 01 B7.");

    var bytes = new byte[checked(packet.Data.Length + trailer.Length)];
    packet.Data.Span.CopyTo(bytes);
    trailer.CopyTo(bytes.AsSpan(packet.Data.Length));
    this._muxer.WritePacket(packet with { Data = bytes, ContainerPrivateData = default });
  }

  public byte[] Finish() => this._muxer.Finish();
}
