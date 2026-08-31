using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.H265Video;

/// <summary>Writes coded H.265 access units as an Annex B byte stream.</summary>
public sealed class H265VideoWriter : IVideoContainerWriter<H265VideoWriter> {

  private readonly ElementaryStreamMuxer _muxer;

  private H265VideoWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata)
    => this._muxer = new(streams, metadata, "H.265 Annex B",
      static stream => stream.Codec == CodecTag.FromCharacters("hvc1") && stream.CodecPrivateData.IsEmpty);

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".265";

  /// <summary>Gets the file extensions supported by this format.</summary>
  public static string[] FileExtensions => [".265", ".h265", ".hevc", ".x265"];

  /// <summary>Creates a writer for the specified stream descriptions and metadata.</summary>
  public static H265VideoWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata)
    => new(streams, metadata);

  /// <summary>Writes the specified coded packet to the container.</summary>
  public void WritePacket(CodedPacket packet) {
    var span = packet.Data.Span;
    var hasStartCode = span.Length >= 5
      && span[0] == 0 && span[1] == 0
      && (span[2] == 1 || span.Length >= 6 && span[2] == 0 && span[3] == 1);

    if (!hasStartCode)
      throw new InvalidDataException(
        "H.265 Annex B packets must contain start codes. Length-prefixed MP4/QuickTime packets need conversion before writing a raw byte stream.");

    this._muxer.WritePacket(packet);
  }

  /// <summary>Finishes writing the container and returns its encoded bytes.</summary>
  public byte[] Finish() => this._muxer.Finish();
}
