using System;
using System.IO;
using FileFormat.Codecs.ProSumer;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Brooktree ProSumer (BT20): residual byte pairs in a prefix code over nibbles, a vertical predictor
/// over 32-bit words, and 4:1:1 YUV of six bits a sample, stored bottom up.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/prosumer.c</c>, LGPL-2.1-or-later; see <c>ProSumer/THIRD-PARTY-NOTICE.FFmpeg.txt</c>.
/// Pictures come out as <see cref="PixelFormat.Yuv411P8"/>, every sample a multiple of four.
/// </remarks>
public sealed class ProSumerVideoDecoder : IVideoCodecDecoder<ProSumerVideoDecoder> {

  internal static readonly CodecTag Tag = CodecTag.FromCharacters("BT20");

  private readonly ProSumerFrame _frame;

  private ProSumerVideoDecoder(ProSumerFrame frame) => this._frame = frame;

  public static string CodecName => "Brooktree ProSumer";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(Tag);
  }

  public static ProSumerVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException($"Stream {stream.Index} is not Brooktree ProSumer.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException($"Stream {stream.Index} states no picture size, which ProSumer needs.");
    return new(new(stream.Width, stream.Height));
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var buffer = this._frame.Decode(packet.Data.Span);
    frame = new() {
      Width = this._frame.Width, Height = this._frame.Height, Format = PixelFormat.Yuv411P8, PixelData = this._frame.ToYuv411(buffer),
      ColorInfo = RawImageColorInfo.Bt601Limited,
    };
    return true;
  }
}

/// <summary>
/// Encodes Brooktree ProSumer (BT20): every picture 4:1:1 at six bits a sample, as residuals against the row
/// above in ProSumer's nibble code.
/// </summary>
/// <remarks>
/// This package's own; FFmpeg has no ProSumer encoder. ProSumer keeps six bits a sample, and its predictor
/// carries bits between the bytes of a word, so the residuals are chosen a byte at a time to land as near
/// each sample as the carries allow. Lossy, because ProSumer is. FFmpeg's decoder is the oracle that reads
/// what this writes.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class ProSumerVideoEncoder : IVideoCodecEncoder<ProSumerVideoEncoder> {

  private readonly MediaStreamInfo _stream;
  private readonly ProSumerFrame _frame;

  private ProSumerVideoEncoder(MediaStreamInfo requested) {
    this._stream = new() {
      Index = requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = ProSumerVideoDecoder.Tag,
      Handler = ProSumerVideoDecoder.Tag,
      TimeBase = requested.TimeBase,
      FrameRate = requested.FrameRate,
      DeclaredFrameCount = requested.DeclaredFrameCount,
      Width = requested.Width,
      Height = requested.Height,
      BitsPerPixel = 12,
      Language = requested.Language,
      Name = requested.Name,
    };
    this._frame = new(requested.Width, requested.Height);
  }

  public static string CodecName => ProSumerVideoDecoder.CodecName;
  public static CodecTag Codec => ProSumerVideoDecoder.Tag;

  public static bool Accepts(MediaStreamInfo stream) => stream.Codec.EqualsIgnoringCase(ProSumerVideoDecoder.Tag);

  public static ProSumerVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("ProSumer can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException($"Stream {stream.Index} states no picture size, which ProSumer needs.");
    return new(stream);
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException($"ProSumer geometry is fixed at {this._stream.Width}x{this._stream.Height}; received {frame.Width}x{frame.Height}.");
    var yuv = frame.Format == PixelFormat.Yuv411P8 ? frame : FastRawImageConverter.Convert(frame, PixelFormat.Yuv411P8, RawImageColorInfo.Bt601Limited);
    var payload = ProSumerCode.Compress(this._frame.Residuals(yuv.GetPlaneData(0), yuv.GetPlaneData(1), yuv.GetPlaneData(2)));
    var data = new byte[ProSumerCode.HeaderLength + payload.Length];
    BitConverter.TryWriteBytes(data.AsSpan(0, 4), ProSumerCode.Tag);
    BitConverter.TryWriteBytes(data.AsSpan(4, 4), 2u);
    BitConverter.TryWriteBytes(data.AsSpan(28, 4), (uint)payload.Length);
    payload.CopyTo(data, ProSumerCode.HeaderLength);
    packet = new(this._stream.Index, data, PresentationTimestamp: presentationTimestamp, DecodeTimestamp: presentationTimestamp, Duration: 1, IsKeyFrame: true);
    return true;
  }
}
