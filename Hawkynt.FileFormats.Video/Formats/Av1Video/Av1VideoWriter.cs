using System.Collections.Generic;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Av1Video;

/// <summary>Writes AV1 temporal units as a low-overhead OBU elementary stream.</summary>
public sealed class Av1VideoWriter : IVideoContainerWriter<Av1VideoWriter> {

  private static readonly byte[] _TEMPORAL_DELIMITER = [0x12, 0x00];

  private readonly ElementaryStreamMuxer _muxer;

  private Av1VideoWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata)
    => this._muxer = new(streams, metadata, "AV1 low-overhead OBU stream",
      static stream => stream.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("av01")) && stream.CodecPrivateData.IsEmpty);

  public static string PrimaryExtension => ".obu";
  public static string[] FileExtensions => [".obu"];

  public static Av1VideoWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata)
    => new(streams, metadata);

  public void WritePacket(CodedPacket packet) {
    var alreadyDelimited = Av1VideoReader.ValidateTemporalUnit(packet.Data);
    if (alreadyDelimited) {
      this._muxer.WritePacket(packet);
      return;
    }

    var canonical = new byte[_TEMPORAL_DELIMITER.Length + packet.Data.Length];
    _TEMPORAL_DELIMITER.CopyTo(canonical, 0);
    packet.Data.Span.CopyTo(canonical.AsSpan(_TEMPORAL_DELIMITER.Length));
    this._muxer.WritePacket(packet with { Data = canonical });
  }

  public byte[] Finish() => this._muxer.Finish();
}
