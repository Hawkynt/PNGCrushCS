using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Bfi;

/// <summary>Writes a BFI header followed by complete IVAS frame chunks.</summary>
public sealed class BfiWriter : IVideoContainerWriter<BfiWriter> {

  private readonly IReadOnlyList<MediaStreamInfo> _streams;
  private readonly List<ReadOnlyMemory<byte>> _frames = [];
  private bool _finished;

  private BfiWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) {
    ArgumentNullException.ThrowIfNull(streams);
    ArgumentNullException.ThrowIfNull(metadata);
    if (streams.Count is < 1 or > 2 || streams[0].Index != 0 || streams[0].Kind != MediaStreamKind.Video
        || !streams[0].Codec.EqualsIgnoringCase(CodecTag.FromCharacters("BFIV")))
      throw new NotSupportedException("BFI needs BFIV video stream 0 and optionally its derived PCM view at stream 1.");
    if (streams[0].CodecPrivateData.Length != 768)
      throw new NotSupportedException("BFI video needs its 256xRGB palette in CodecPrivateData.");
    if (streams.Count == 2 && (streams[1].Index != 1 || streams[1].Kind != MediaStreamKind.Audio))
      throw new NotSupportedException("BFI's optional second stream is audio at index 1.");
    this._streams = streams;
  }

  public static string PrimaryExtension => ".bfi";
  public static string[] FileExtensions => [".bfi"];
  public static BfiWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams, metadata);

  public void WritePacket(CodedPacket packet) {
    if (this._finished) throw new InvalidOperationException("BFI writer has already been finished.");
    if ((uint)packet.StreamIndex >= (uint)this._streams.Count) throw new ArgumentOutOfRangeException(nameof(packet));

    // The BFI demuxer's video packet is deliberately the complete IVAS chunk, including the same
    // interleaved audio bytes it additionally exposes as stream 1 for callers interested in sound.
    // Writing stream 1 again would duplicate those bytes, so only the authoritative whole-frame
    // packet is container data here.
    if (packet.StreamIndex == 1)
      return;

    var data = packet.Data.Span;
    if (data.Length < 8 || !data[..4].SequenceEqual("IVAS"u8))
      throw new InvalidDataException("BFI video packet must be a complete IVAS chunk.");
    var size = BinaryPrimitives.ReadUInt32LittleEndian(data[4..8]);
    if (size != data.Length)
      throw new InvalidDataException($"BFI IVAS chunk states {size} bytes and packet carries {data.Length}.");
    this._frames.Add(packet.Data);
  }

  public byte[] Finish() {
    if (this._finished) throw new InvalidOperationException("BFI writer has already been finished.");
    this._finished = true;

    var video = this._streams[0];
    var header = new byte[BfiChunkReader.HeaderLength];
    "BF&I"u8.CopyTo(header);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), BfiChunkReader.HeaderLength);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), checked((uint)this._frames.Count));
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(44), checked((uint)video.Width));
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(48), checked((uint)video.Height));
    video.CodecPrivateData.Span.CopyTo(header.AsSpan(60, 768));

    if (this._streams.Count == 2) {
      var audio = this._streams[1];
      var sampleRate = audio.SampleRate > 0
        ? audio.SampleRate
        : audio.TimeBase.IsKnown && audio.TimeBase.Numerator == 1 ? checked((int)audio.TimeBase.Denominator) : 11025;
      var channels = audio.Channels > 0 ? audio.Channels
        : audio.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("BFI2")) ? 2 : 1;
      BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(828), checked((uint)sampleRate));
      BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(832), checked((uint)channels));
    }

    using var output = new MemoryStream();
    output.Write(header);
    foreach (var frame in this._frames)
      output.Write(frame.Span);
    return output.ToArray();
  }
}
