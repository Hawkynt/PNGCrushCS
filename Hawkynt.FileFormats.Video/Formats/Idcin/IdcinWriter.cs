using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Idcin;

/// <summary>Writes Quake II id Cinematic files from complete video commands and optional raw PCM packets.</summary>
public sealed class IdcinWriter : IVideoContainerWriter<IdcinWriter> {

  private const int _HUFFMAN_TABLE_LENGTH = 64 * 1024;
  private readonly IReadOnlyList<MediaStreamInfo> _streams;
  private readonly MemoryStream _output = new();
  private readonly int _audioPacketBytes;
  private bool _expectVideo = true;
  private bool _finished;

  private IdcinWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) {
    ArgumentNullException.ThrowIfNull(streams);
    ArgumentNullException.ThrowIfNull(metadata);
    if (streams.Count is < 1 or > 2 || streams[0].Index != 0 || streams[0].Kind != MediaStreamKind.Video
        || !streams[0].Codec.EqualsIgnoringCase(CodecTag.FromCharacters("IDCV")))
      throw new NotSupportedException("id Cinematic needs IDCV video at stream 0 and optionally its raw PCM audio at stream 1.");
    var video = streams[0];
    if (video.Width is <= 0 or > 1024 || video.Height is <= 0 or > 1024)
      throw new NotSupportedException("id Cinematic dimensions must fit the format's plausible 1..1024 range.");
    if (video.CodecPrivateData.Length != _HUFFMAN_TABLE_LENGTH)
      throw new NotSupportedException("id Cinematic needs its exact 64 KiB Huffman histogram table in the video stream's CodecPrivateData.");

    var sampleRate = 0;
    var bytesPerSample = 0;
    var channels = 0;
    if (streams.Count == 2) {
      var audio = streams[1];
      if (audio.Index != 1 || audio.Kind != MediaStreamKind.Audio)
        throw new NotSupportedException("id Cinematic's optional second stream is audio at index 1.");
      (bytesPerSample, channels) = _AudioShape(audio.Codec);
      sampleRate = audio.SampleRate > 0
        ? audio.SampleRate
        : audio.TimeBase.IsKnown && audio.TimeBase.Numerator == 1 ? checked((int)audio.TimeBase.Denominator) : 0;
      if (sampleRate is < 4000 or > 96000 || sampleRate % 14 != 0)
        throw new NotSupportedException("id Cinematic audio sample rate must be 4..96 kHz and divide the fixed 14 fps cadence exactly.");
      this._audioPacketBytes = sampleRate / 14 * bytesPerSample * channels;
    }

    this._streams = streams;
    ContainerWriterTools.WriteUInt32LittleEndian(this._output, checked((uint)video.Width));
    ContainerWriterTools.WriteUInt32LittleEndian(this._output, checked((uint)video.Height));
    ContainerWriterTools.WriteUInt32LittleEndian(this._output, checked((uint)sampleRate));
    ContainerWriterTools.WriteUInt32LittleEndian(this._output, checked((uint)bytesPerSample));
    ContainerWriterTools.WriteUInt32LittleEndian(this._output, checked((uint)channels));
    this._output.Write(video.CodecPrivateData.Span);
  }

  public static string PrimaryExtension => ".cin";
  public static string[] FileExtensions => [".cin"];
  public static IdcinWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams, metadata);

  public void WritePacket(CodedPacket packet) {
    if (this._finished) throw new InvalidOperationException("id Cinematic writer has already been finished.");
    if ((uint)packet.StreamIndex >= (uint)this._streams.Count) throw new ArgumentOutOfRangeException(nameof(packet));

    if (packet.StreamIndex == 0) {
      if (!this._expectVideo)
        throw new InvalidDataException("id Cinematic with audio alternates one video command and one fixed PCM block; two video packets arrived consecutively.");
      if (packet.Data.Length < 12)
        throw new InvalidDataException("An id Cinematic video packet must include its command, Huffman count and decode count fields.");
      this._output.Write(packet.Data.Span);
      this._expectVideo = this._streams.Count == 1;
    } else {
      if (this._expectVideo)
        throw new InvalidDataException("id Cinematic audio must follow its corresponding video frame.");
      if (packet.Data.Length != this._audioPacketBytes)
        throw new InvalidDataException($"id Cinematic audio packet is {packet.Data.Length} bytes; this header requires exactly {this._audioPacketBytes} per video frame.");
      this._output.Write(packet.Data.Span);
      this._expectVideo = true;
    }
  }

  public byte[] Finish() {
    if (this._finished) throw new InvalidOperationException("id Cinematic writer has already been finished.");
    if (!this._expectVideo)
      throw new InvalidDataException("id Cinematic file ends after a video frame whose required PCM block was never supplied.");
    this._finished = true;
    ContainerWriterTools.WriteUInt32LittleEndian(this._output, 2);
    return this._output.ToArray();
  }

  private static (int BytesPerSample, int Channels) _AudioShape(CodecTag codec) {
    if (codec.EqualsIgnoringCase(CodecTag.FromCharacters("ICBM"))) return (1, 1);
    if (codec.EqualsIgnoringCase(CodecTag.FromCharacters("ICBS"))) return (1, 2);
    if (codec.EqualsIgnoringCase(CodecTag.FromCharacters("ICWM"))) return (2, 1);
    if (codec.EqualsIgnoringCase(CodecTag.FromCharacters("ICWS"))) return (2, 2);
    throw new NotSupportedException($"'{codec}' is not one of id Cinematic's four raw PCM layouts.");
  }
}
