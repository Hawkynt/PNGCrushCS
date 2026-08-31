using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Flv;

/// <summary>Writes original-header FLV 1 tags for video and AAC audio streams.</summary>
public sealed class FlvWriter : IVideoContainerWriter<FlvWriter> {

  private readonly IReadOnlyList<MediaStreamInfo> _streams;
  private readonly int[] _formats;
  private readonly MemoryStream _output = new();
  private bool _finished;

  private FlvWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) {
    ArgumentNullException.ThrowIfNull(streams);
    ArgumentNullException.ThrowIfNull(metadata);
    if (streams.Count == 0 || streams.Count > 2)
      throw new NotSupportedException("FLV carries at most one audio and one video stream.");

    var videoSeen = false;
    var audioSeen = false;
    this._formats = new int[streams.Count];
    for (var i = 0; i < streams.Count; ++i) {
      var stream = streams[i] ?? throw new ArgumentException($"Stream {i} is null.", nameof(streams));
      if (stream.Index != i)
        throw new ArgumentException($"FLV streams must be indexed densely from zero; position {i} has index {stream.Index}.", nameof(streams));

      switch (stream.Kind) {
        case MediaStreamKind.Video when !videoSeen:
          videoSeen = true;
          this._formats[i] = _VideoFormat(stream);
          if (this._formats[i] == 7 && stream.CodecPrivateData.IsEmpty)
            throw new NotSupportedException("FLV AVC needs an AVCDecoderConfigurationRecord in CodecPrivateData.");
          break;
        case MediaStreamKind.Audio when !audioSeen:
          audioSeen = true;
          this._formats[i] = _AudioFormat(stream);
          if (this._formats[i] != 10)
            throw new NotSupportedException("This FLV writer currently emits AAC audio; other FLV audio mappings need sample-rate/channel fields the stream model does not preserve.");
          if (stream.CodecPrivateData.IsEmpty)
            throw new NotSupportedException("FLV AAC needs an AudioSpecificConfig in CodecPrivateData.");
          break;
        case MediaStreamKind.Video:
          throw new NotSupportedException("FLV has only one video tag stream.");
        case MediaStreamKind.Audio:
          throw new NotSupportedException("FLV has only one audio tag stream.");
        default:
          throw new NotSupportedException($"FLV cannot carry {stream.Kind} through its original audio/video tag headers.");
      }
    }

    this._streams = streams;

    ContainerWriterTools.WriteAscii(this._output, "FLV");
    this._output.WriteByte(1);
    this._output.WriteByte((byte)((videoSeen ? 1 : 0) | (audioSeen ? 4 : 0)));
    ContainerWriterTools.WriteUInt32BigEndian(this._output, 9);
    ContainerWriterTools.WriteUInt32BigEndian(this._output, 0);

    // Configuration tags are the first declaration an FLV has of these streams. Emit them in stream
    // order so reading the file back gives the same stream indices the caller supplied.
    for (var i = 0; i < streams.Count; ++i) {
      var stream = streams[i];
      if (stream.Kind == MediaStreamKind.Video && this._formats[i] == 7)
        this._WriteTag(9, 0, _AvcBody(stream, null, sequenceHeader: true));
      else if (stream.Kind == MediaStreamKind.Audio && this._formats[i] == 10)
        this._WriteTag(8, 0, _AacBody(stream, null, sequenceHeader: true));
    }
  }

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".flv";
  /// <summary>Gets the file extensions supported by this format.</summary>
  public static string[] FileExtensions => [".flv", ".f4v"];

  /// <summary>Creates a writer for the specified stream descriptions and metadata.</summary>
  public static FlvWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams, metadata);

  /// <summary>Writes the specified coded packet to the container.</summary>
  public void WritePacket(CodedPacket packet) {
    if (this._finished)
      throw new InvalidOperationException("FLV writer has already been finished.");
    if ((uint)packet.StreamIndex >= (uint)this._streams.Count)
      throw new ArgumentOutOfRangeException(nameof(packet), packet.StreamIndex, "Packet names no declared FLV stream.");

    var stream = this._streams[packet.StreamIndex];
    var dts = packet.DecodeTimestamp ?? packet.PresentationTimestamp ?? 0;
    var timestamp = ContainerWriterTools.Rescale(dts, stream.TimeBase, 1000);
    if (timestamp is < 0 or > uint.MaxValue)
      throw new NotSupportedException($"FLV timestamp {timestamp} ms is outside its unsigned 32-bit timestamp field.");

    byte[] body;
    if (stream.Kind == MediaStreamKind.Video) {
      var format = this._formats[packet.StreamIndex];
      if (format == 7) {
        var pts = packet.PresentationTimestamp ?? dts;
        var composition = ContainerWriterTools.Rescale(pts - dts, stream.TimeBase, 1000);
        if (composition is < -0x800000 or > 0x7FFFFF)
          throw new NotSupportedException($"FLV AVC composition offset {composition} ms does not fit its signed 24-bit field.");
        body = _AvcBody(stream, packet, sequenceHeader: false, checked((int)composition));
      } else {
        var prefix = (byte)(((packet.IsKeyFrame ? 1 : 2) << 4) | format);
        body = new byte[packet.Data.Length + 1];
        body[0] = prefix;
        packet.Data.CopyTo(body.AsMemory(1));
      }
      this._WriteTag(9, checked((uint)timestamp), body);
    } else {
      body = _AacBody(stream, packet, sequenceHeader: false);
      this._WriteTag(8, checked((uint)timestamp), body);
    }
  }

  /// <summary>Finishes writing the container and returns its encoded bytes.</summary>
  public byte[] Finish() {
    if (this._finished)
      throw new InvalidOperationException("FLV writer has already been finished.");
    this._finished = true;
    return this._output.ToArray();
  }

  private void _WriteTag(byte type, uint timestamp, ReadOnlySpan<byte> body) {
    if (body.Length > 0xFFFFFF)
      throw new NotSupportedException("One FLV tag may carry at most 16,777,215 bytes.");

    this._output.WriteByte(type);
    ContainerWriterTools.WriteUInt24BigEndian(this._output, body.Length);
    ContainerWriterTools.WriteUInt24BigEndian(this._output, (int)(timestamp & 0xFFFFFF));
    this._output.WriteByte((byte)(timestamp >> 24));
    ContainerWriterTools.WriteUInt24BigEndian(this._output, 0);
    this._output.Write(body);
    ContainerWriterTools.WriteUInt32BigEndian(this._output, checked((uint)(body.Length + 11)));
  }

  private static byte[] _AvcBody(MediaStreamInfo stream, CodedPacket? packet, bool sequenceHeader, int composition = 0) {
    var data = sequenceHeader ? stream.CodecPrivateData : packet!.Value.Data;
    var result = new byte[data.Length + 5];
    result[0] = (byte)(((sequenceHeader || packet!.Value.IsKeyFrame ? 1 : 2) << 4) | 7);
    result[1] = sequenceHeader ? (byte)0 : (byte)1;
    var encoded = composition & 0xFFFFFF;
    result[2] = (byte)(encoded >> 16);
    result[3] = (byte)(encoded >> 8);
    result[4] = (byte)encoded;
    data.CopyTo(result.AsMemory(5));
    return result;
  }

  private static byte[] _AacBody(MediaStreamInfo stream, CodedPacket? packet, bool sequenceHeader) {
    var data = sequenceHeader ? stream.CodecPrivateData : packet!.Value.Data;
    var result = new byte[data.Length + 2];
    // AAC ignores the legacy SoundRate/SoundSize/SoundType bits, but Adobe writers conventionally set
    // them to 44 kHz, 16-bit, stereo. SoundFormat=10 is the only part that identifies the codec.
    result[0] = 0xAF;
    result[1] = sequenceHeader ? (byte)0 : (byte)1;
    data.CopyTo(result.AsMemory(2));
    return result;
  }

  private static int _VideoFormat(MediaStreamInfo stream) {
    if (stream.Handler.Value is > 0 and <= 15)
      return (int)stream.Handler.Value;

    var code = stream.Codec;
    if (code.EqualsIgnoringCase(CodecTag.FromCharacters("MJPG"))) return 1;
    if (code.EqualsIgnoringCase(CodecTag.FromCharacters("FLV1"))) return 2;
    if (code.EqualsIgnoringCase(CodecTag.FromCharacters("FSV1"))) return 3;
    if (code.EqualsIgnoringCase(CodecTag.FromCharacters("VP6F"))) return 4;
    if (code.EqualsIgnoringCase(CodecTag.FromCharacters("VP6A"))) return 5;
    if (code.EqualsIgnoringCase(CodecTag.FromCharacters("FSV2"))) return 6;
    if (code.EqualsIgnoringCase(CodecTag.FromCharacters("H264")) || code.EqualsIgnoringCase(CodecTag.FromCharacters("avc1"))) return 7;
    throw new NotSupportedException($"FLV has no original-header video codec number for '{stream.Codec}'.");
  }

  private static int _AudioFormat(MediaStreamInfo stream) {
    if (stream.Handler.Value is > 0 and <= 15)
      return (int)stream.Handler.Value;
    if (stream.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("mp4a")))
      return 10;
    throw new NotSupportedException($"FLV has no supported audio mapping for '{stream.Codec}'.");
  }
}
