using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.H263Video;

/// <summary>Writes complete H.263 coded pictures as a raw elementary stream.</summary>
public sealed class H263VideoWriter : IVideoContainerWriter<H263VideoWriter> {

  private readonly ElementaryStreamMuxer _muxer;

  private H263VideoWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata)
    => this._muxer = new(streams, metadata, "H.263 elementary stream",
      static stream => stream.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("H263")));

  public static string PrimaryExtension => ".263";
  public static string[] FileExtensions => [".263", ".h263"];

  public static H263VideoWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata)
    => new(streams, metadata);

  public void WritePacket(CodedPacket packet) {
    if (!H263VideoContainer.IsPictureStart(packet.Data.Span))
      throw new InvalidDataException("Each raw H.263 packet must begin with its byte-aligned 22-bit picture start code.");

    this._muxer.WritePacket(packet);
  }

  public byte[] Finish() => this._muxer.Finish();
}
