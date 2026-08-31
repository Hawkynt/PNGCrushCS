using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Yuv4Mpeg;

/// <summary>Writes uncompressed planar YUV frames as a YUV4MPEG2 stream.</summary>
public sealed class Yuv4MpegWriter : IVideoContainerWriter<Yuv4MpegWriter> {

  private readonly MemoryStream _stream = new();
  private readonly MediaStreamInfo _video;
  private readonly int _frameSize;

  private Yuv4MpegWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) {
    ArgumentNullException.ThrowIfNull(streams);
    ArgumentNullException.ThrowIfNull(metadata);
    if (streams.Count != 1)
      throw new ArgumentException("YUV4MPEG2 requires exactly one stream.", nameof(streams));

    this._video = streams[0];
    if (this._video.Index != 0 || this._video.Kind != MediaStreamKind.Video)
      throw new ArgumentException("YUV4MPEG2 requires one video stream at index zero.", nameof(streams));
    if (this._video.Width <= 0 || this._video.Height <= 0)
      throw new ArgumentException("YUV4MPEG2 requires positive video dimensions.", nameof(streams));

    var chroma = this._video.CodecPrivateData.IsEmpty
      ? "420jpeg"
      : Encoding.ASCII.GetString(this._video.CodecPrivateData.Span);
    this._frameSize = Yuv4MpegContainer.GetFrameSize(this._video.Width, this._video.Height, chroma);

    var frameRate = this._video.FrameRate;
    if (!frameRate.IsKnown && this._video.TimeBase.IsKnown)
      frameRate = new(this._video.TimeBase.Denominator, this._video.TimeBase.Numerator);
    if (!frameRate.IsKnown || frameRate.Numerator <= 0 || frameRate.Denominator <= 0)
      throw new ArgumentException("YUV4MPEG2 requires a known positive frame rate or time base.", nameof(streams));

    var header = $"YUV4MPEG2 W{this._video.Width} H{this._video.Height} F{frameRate.Numerator}:{frameRate.Denominator} Ip C{chroma}\n";
    var bytes = Encoding.ASCII.GetBytes(header);
    this._stream.Write(bytes);
  }

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".y4m";
  /// <summary>Gets the file extensions supported by this format.</summary>
  public static string[] FileExtensions => [".y4m"];

  /// <summary>Creates a writer for the specified stream descriptions and metadata.</summary>
  public static Yuv4MpegWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata)
    => new(streams, metadata);

  /// <summary>Writes the specified coded packet to the container.</summary>
  public void WritePacket(CodedPacket packet) {
    if (packet.StreamIndex != 0)
      throw new InvalidDataException($"YUV4MPEG2 only has stream zero, not stream {packet.StreamIndex}.");
    if (packet.Data.Length != this._frameSize)
      throw new InvalidDataException($"YUV4MPEG2 frame payload must be exactly {this._frameSize} bytes, not {packet.Data.Length}.");

    this._stream.Write("FRAME"u8);
    if (!packet.ContainerPrivateData.IsEmpty) {
      var extension = packet.ContainerPrivateData.Span;
      if (extension[0] != (byte)' ')
        this._stream.WriteByte((byte)' ');
      this._stream.Write(extension);
    }
    this._stream.WriteByte((byte)'\n');
    this._stream.Write(packet.Data.Span);
  }

  /// <summary>Finishes writing the container and returns its encoded bytes.</summary>
  public byte[] Finish() => this._stream.ToArray();
}
