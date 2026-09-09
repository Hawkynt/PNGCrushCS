using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.RoqVideo;

/// <summary>Writes RoQ video chunks verbatim and sound chunks with their preserved predictor arguments.</summary>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class RoqWriter : IVideoContainerWriter<RoqWriter> {

  private readonly IReadOnlyList<MediaStreamInfo> _streams;
  private readonly MemoryStream _output = new();
  private bool _finished;

  private RoqWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) {
    ArgumentNullException.ThrowIfNull(streams);
    ArgumentNullException.ThrowIfNull(metadata);
    if (streams.Count is < 1 or > 2 || streams[0].Index != 0 || streams[0].Kind != MediaStreamKind.Video
        || !streams[0].Codec.EqualsIgnoringCase(CodecTag.FromCharacters("RoQV")))
      throw new NotSupportedException("RoQ needs RoQV video at stream zero and optionally one RoQ sound stream at index one.");

    if (streams.Count == 2) {
      var audio = streams[1];
      if (audio.Index != 1 || audio.Kind != MediaStreamKind.Audio
          || !(audio.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("RoQM"))
               || audio.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("RoQS"))))
        throw new NotSupportedException("RoQ's optional second stream must be RoQM mono or RoQS stereo sound.");
    }

    this._streams = streams;
    this._output.Write(RoqReader.Signature);
  }

  public static string PrimaryExtension => ".roq";
  public static string[] FileExtensions => [".roq"];
  public static RoqWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams, metadata);

  public void WritePacket(CodedPacket packet) {
    if (this._finished)
      throw new InvalidOperationException("RoQ writer has already been finished.");
    if ((uint)packet.StreamIndex >= (uint)this._streams.Count)
      throw new ArgumentOutOfRangeException(nameof(packet));

    if (packet.StreamIndex == 0) {
      _CheckVideoChunks(packet.Data.Span);
      this._output.Write(packet.Data.Span);
      return;
    }

    if (packet.ContainerPrivateData.Length != 2)
      throw new NotSupportedException(
        "A RoQ sound packet needs the original two-byte chunk argument in ContainerPrivateData; it is the DPCM predictor seed and cannot be invented.");

    var audio = this._streams[1];
    var id = audio.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("RoQS"))
      ? RoqChunkType.SOUND_STEREO
      : RoqChunkType.SOUND_MONO;
    ContainerWriterTools.WriteUInt16LittleEndian(this._output, id);
    ContainerWriterTools.WriteUInt32LittleEndian(this._output, checked((uint)packet.Data.Length));
    this._output.Write(packet.ContainerPrivateData.Span);
    this._output.Write(packet.Data.Span);
  }

  /// <summary>
  /// Checks that a video packet is a whole number of RoQ chunks, each stating its own length truly.
  /// </summary>
  /// <remarks>
  /// One packet, one chunk is what the demuxer hands out, and a remux writes those back unchanged. An
  /// encoder cannot work that way: a picture is a <c>QUAD_VQ</c> chunk plus the <c>QUAD_CODEBOOK</c>
  /// chunk it needs and, at the start of a film, an <c>INFO</c> chunk, and
  /// <see cref="FileFormat.Core.IVideoPacketEncoder.TryEncode"/> hands back one packet per picture. So a
  /// packet is a run of chunks here rather than exactly one, and the run is walked rather than the first
  /// header trusted for the whole of it — a packet whose last chunk overruns is a file that cannot be
  /// read back, and is refused here rather than written.
  /// </remarks>
  private static void _CheckVideoChunks(ReadOnlySpan<byte> data) {
    if (data.Length < 8)
      throw new InvalidDataException("A RoQ video packet must include its eight-byte codec chunk header.");

    var at = 0;
    while (at < data.Length) {
      if (at + 8 > data.Length)
        throw new InvalidDataException(
          $"A RoQ chunk header would start {data.Length - at} bytes from the end of a packet whose chunk "
          + "headers are eight bytes each.");

      var size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(at + 2, 4));
      if (size > int.MaxValue || at + 8 + (long)size > data.Length)
        throw new InvalidDataException(
          $"A RoQ chunk at byte {at} of a packet states {size} payload bytes, which runs past the packet's "
          + $"{data.Length - at - 8} remaining.");

      at += 8 + (int)size;
    }
  }

  public byte[] Finish() {
    if (this._finished)
      throw new InvalidOperationException("RoQ writer has already been finished.");
    this._finished = true;
    return this._output.ToArray();
  }
}
