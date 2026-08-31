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

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".263";
  /// <summary>Gets the file extensions supported by this format.</summary>
  public static string[] FileExtensions => [".263", ".h263"];

  /// <summary>Creates a writer for the specified stream descriptions and metadata.</summary>
  public static H263VideoWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata)
    => new(streams, metadata);

  /// <summary>Writes the specified coded packet to the container.</summary>
  public void WritePacket(CodedPacket packet) {
    if (!H263VideoContainer.IsPictureStart(packet.Data.Span))
      throw new InvalidDataException("Each raw H.263 packet must begin with its byte-aligned 22-bit picture start code.");

    this._muxer.WritePacket(packet);
  }

  /// <summary>Finishes writing the container and returns its encoded bytes.</summary>
  public byte[] Finish() => this._muxer.Finish();
}
