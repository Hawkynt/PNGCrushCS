using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.H264;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.H264Video;

/// <summary>
/// Writes H.264 access units as an Annex B byte stream, accepting either Annex B packets already or
/// the length-prefixed AVC samples used by MP4, Matroska and FLV.
/// </summary>
/// <remarks>
/// A raw H.264 stream has nowhere to carry <c>AVCDecoderConfigurationRecord</c>. When the source stream
/// has one, its SPS/PPS are written in-band before the first picture and every length-prefixed NAL unit
/// is rewritten with a four-byte Annex B start code. The NAL payload itself is never decoded or
/// re-encoded, so remuxing preserves every coded byte.
/// </remarks>
public sealed class H264VideoWriter : IVideoContainerWriter<H264VideoWriter> {

  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("avc1"),
    CodecTag.FromCharacters("avc3"),
    CodecTag.FromCharacters("H264"),
    CodecTag.FromCharacters("X264"),
    CodecTag.FromCharacters("DAVC"),
    CodecTag.FromCharacters("VSSH"),
  ];

  private readonly ElementaryStreamMuxer _muxer;
  private readonly H264DecoderConfiguration? _configuration;

  private H264VideoWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) {
    this._muxer = new(streams, metadata, "H.264 Annex B", _AcceptsStream);

    var stream = streams[0];
    this._configuration = H264DecoderConfiguration.TryParse(stream.CodecPrivateData);
    if (!stream.CodecPrivateData.IsEmpty && this._configuration == null)
      throw new NotSupportedException(
        "H.264 Annex B cannot interpret this stream's CodecPrivateData as an AVCDecoderConfigurationRecord "
        + "or an MP4 visual sample entry containing one.");

    if (this._configuration == null)
      return;

    foreach (var parameterSet in this._configuration.SequenceParameterSets)
      this._WriteNal(parameterSet);
    foreach (var parameterSet in this._configuration.PictureParameterSets)
      this._WriteNal(parameterSet);
  }

  public static string PrimaryExtension => ".264";

  public static string[] FileExtensions => [".264", ".h264", ".avc", ".x264"];

  public static H264VideoWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata)
    => new(streams, metadata);

  public void WritePacket(CodedPacket packet) {
    if (this._configuration == null) {
      if (!H264NalReader.LooksLikeAnnexB(packet.Data.Span))
        throw new InvalidDataException(
          "H.264 Annex B packets must begin with a 00 00 01 or 00 00 00 01 start code when the stream "
          + "carries no AVCDecoderConfigurationRecord.");
      this._muxer.WritePacket(packet);
      return;
    }

    var data = packet.Data.Span;
    var lengthSize = this._configuration.LengthSize;
    var at = 0;
    while (at < data.Length) {
      if (data.Length - at < lengthSize)
        throw new InvalidDataException(
          $"An H.264 length-prefixed packet ends with {data.Length - at} byte(s), fewer than its "
          + $"{lengthSize}-byte NAL length field.");

      uint length = 0;
      for (var i = 0; i < lengthSize; ++i)
        length = (length << 8) | data[at + i];
      at += lengthSize;

      if (length == 0)
        continue;

      if (length > int.MaxValue || length > (uint)(data.Length - at))
        throw new InvalidDataException(
          $"An H.264 packet states a NAL unit of {length} bytes at offset {at}, but only "
          + $"{data.Length - at} remain.");

      var nalLength = (int)length;
      this._WriteNal(data.Slice(at, nalLength));
      at += nalLength;
    }
  }

  public byte[] Finish() => this._muxer.Finish();

  private void _WriteNal(ReadOnlySpan<byte> nal) {
    if (nal.IsEmpty)
      throw new InvalidDataException("H.264 Annex B cannot write an empty NAL unit.");

    var bytes = new byte[4 + nal.Length];
    BinaryPrimitives.WriteUInt32BigEndian(bytes, 1);
    nal.CopyTo(bytes.AsSpan(4));
    this._muxer.WritePacket(new(0, bytes));
  }

  private static bool _AcceptsStream(MediaStreamInfo stream) {
    if (string.Equals(stream.CodecId, "V_MPEG4/ISO/AVC", StringComparison.Ordinal))
      return true;

    foreach (var tag in _Tags)
      if (stream.Codec.EqualsIgnoringCase(tag))
        return true;

    return false;
  }
}
