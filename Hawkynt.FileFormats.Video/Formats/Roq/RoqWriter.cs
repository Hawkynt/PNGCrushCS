using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.RoqVideo;

/// <summary>Writes RoQ video chunks verbatim and optional RoQ DPCM sound.</summary>
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
    _WriteSignature(this._output, streams[0]);
  }

  public static string PrimaryExtension => ".roq";
  public static string[] FileExtensions => [".roq"];
  public static RoqWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams, metadata);

  private static void _WriteSignature(Stream output, MediaStreamInfo video) {
    var privateData = video.CodecPrivateData.Span;
    if (privateData.Length > 0 && privateData[0] == 2) {
      ContainerWriterTools.WriteUInt16LittleEndian(output, RoqChunkType.SIGNATURE);
      ContainerWriterTools.WriteUInt32LittleEndian(output, 0);
      ContainerWriterTools.WriteUInt16LittleEndian(output, 0);
      return;
    }

    var rate = video.FrameRate;
    var fps = 30;
    if (rate.IsKnown) {
      if (rate.Numerator <= 0 || rate.Denominator <= 0 || rate.Numerator % rate.Denominator != 0)
        throw new NotSupportedException($"RoQ's header stores an integer frame rate; {rate.Numerator}/{rate.Denominator} cannot be represented exactly.");
      var integral = rate.Numerator / rate.Denominator;
      if (integral is <= 0 or > ushort.MaxValue)
        throw new NotSupportedException($"RoQ's header stores its frame rate in sixteen bits; {integral} is out of range.");
      fps = (int)integral;
    }

    output.Write(RoqReader.CreateSignature(fps));
  }

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
      throw new NotSupportedException("A RoQ sound packet needs its original two-byte DPCM predictor argument in ContainerPrivateData.");

    var audio = this._streams[1];
    var id = audio.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("RoQS")) ? RoqChunkType.SOUND_STEREO : RoqChunkType.SOUND_MONO;
    ContainerWriterTools.WriteUInt16LittleEndian(this._output, id);
    ContainerWriterTools.WriteUInt32LittleEndian(this._output, checked((uint)packet.Data.Length));
    this._output.Write(packet.ContainerPrivateData.Span);
    this._output.Write(packet.Data.Span);
  }

  private static void _CheckVideoChunks(ReadOnlySpan<byte> data) {
    if (data.Length < 8)
      throw new InvalidDataException("A RoQ video packet must include its eight-byte codec chunk header.");

    for (var at = 0; at < data.Length;) {
      if (at + 8 > data.Length)
        throw new InvalidDataException($"A RoQ chunk header starts {data.Length - at} bytes from the end of a video packet.");
      var size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(at + 2, 4));
      if (size > int.MaxValue || at + 8 + (long)size > data.Length)
        throw new InvalidDataException($"A RoQ chunk at byte {at} states {size} bytes past the end of its packet.");
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
