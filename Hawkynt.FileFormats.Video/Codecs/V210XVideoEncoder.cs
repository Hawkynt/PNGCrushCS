using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes v210x/YUV10: uncompressed 10-bit 4:2:2 YUV in big-endian 32-bit words.
/// </summary>
/// <remarks>
/// The active raster is a continuous stream of <c>U, Y0, V, Y1</c> components for each two-pixel
/// 4:2:2 pair. Three consecutive components occupy bits 31-22, 21-12 and 11-2 of each big-endian
/// word; the low two bits are zero. Packing does not restart at scanline boundaries.
/// <para/>
/// FFmpeg has a decoder but no encoder for v210x. Its raw <c>.yuv10</c> demuxer nevertheless defines
/// the frame boundary it expects: width is rounded up to 48 pixels before the nominal 8/3 bytes per
/// pixel are calculated. The active words are therefore written first and the remainder of that
/// frame is zero-filled tail padding. The reference decoder ignores those trailing bytes.
/// <para/>
/// There is no temporal compression. Every encoded packet is a key frame; PTS and DTS are identical,
/// and neither encoding nor decoding keeps reference pictures.
/// <para/>
/// This writer is an independent implementation derived from the public packing behavior of
/// FFmpeg's LGPL-2.1-or-later v210x decoder and raw demuxer rather than a translation of their code.
/// </remarks>
public sealed class V210XVideoEncoder : IVideoCodecEncoder<V210XVideoEncoder> {

  private readonly MediaStreamInfo _stream;
  private readonly int _width;
  private readonly int _height;
  private readonly int _lumaSamples;
  private readonly int _chromaSamples;
  private readonly int _packetByteCount;

  private V210XVideoEncoder(MediaStreamInfo stream, int packetByteCount) {
    this._width = stream.Width;
    this._height = stream.Height;
    this._lumaSamples = checked(stream.Width * stream.Height);
    this._chromaSamples = this._lumaSamples / 2;
    this._packetByteCount = packetByteCount;
    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.None,
      Handler = CodecTag.None,
      CodecId = V210XVideoDecoder.CodecId,
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = 20,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "Uncompressed 4:2:2 10-bit (v210x)";

  /// <summary>
  /// v210x has no interoperable four-character code; FFmpeg identifies it by its codec name instead.
  /// </summary>
  public static CodecTag Codec => CodecTag.None;

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video
           && stream.Codec == CodecTag.None
           && string.Equals(stream.CodecId, V210XVideoDecoder.CodecId, StringComparison.OrdinalIgnoreCase);
  }

  public static V210XVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("v210x can only encode a video stream.");
    if (stream.Codec != CodecTag.None)
      throw new NotSupportedException($"v210x has no four-character codec tag; stream {stream.Index} asks for '{stream.Codec}'.");
    if (stream.CodecId != null
        && !string.Equals(stream.CodecId, V210XVideoDecoder.CodecId, StringComparison.OrdinalIgnoreCase))
      throw new NotSupportedException(
        $"Video stream {stream.Index} asks for codec '{stream.CodecId}', not '{V210XVideoDecoder.CodecId}'.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can be coded from.");
    if ((stream.Width & 1) != 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states an odd v210x width of {stream.Width}; 4:2:2 chroma needs whole two-pixel pairs.");

    int packetByteCount;
    try {
      _ = checked(stream.Width * stream.Height * 2);
      var paddedWidth = checked(((long)stream.Width + 47) / 48 * 48);
      var packetBytes = checked(paddedWidth * stream.Height * 8 / 3);
      if (packetBytes > int.MaxValue)
        throw new OverflowException();
      packetByteCount = (int)packetBytes;
    } catch (OverflowException error) {
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a v210x picture size of {stream.Width}x{stream.Height}, which is too large to address.", error);
    }

    return new(stream, packetByteCount);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"v210x geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var source = frame.Format == PixelFormat.Yuv422P10
      ? frame
      : FastRawImageConverter.Convert(frame, PixelFormat.Yuv422P10, RawImageColorInfo.Bt601Limited);
    if (!source.HasEnoughPixelData)
      throw new InvalidDataException(
        $"Conversion to {PixelFormat.Yuv422P10} produced too few bytes for {this._width}x{this._height}.");

    var luma = _Samples(source.GetPlaneData(0), this._lumaSamples);
    var cb = _Samples(source.GetPlaneData(1), this._chromaSamples);
    var cr = _Samples(source.GetPlaneData(2), this._chromaSamples);

    packet = new(
      this._stream.Index,
      this.EncodePlanes(luma, cb, cr),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  /// <summary>
  /// Packs right-aligned ten-bit planar samples into the v210x component stream and appends the tail
  /// padding expected by FFmpeg's raw yuv10 demuxer.
  /// </summary>
  internal byte[] EncodePlanes(ReadOnlySpan<ushort> luma, ReadOnlySpan<ushort> cb, ReadOnlySpan<ushort> cr) {
    if (luma.Length < this._lumaSamples || cb.Length < this._chromaSamples || cr.Length < this._chromaSamples)
      throw new InvalidDataException(
        $"A {this._width}x{this._height} v210x frame needs {this._lumaSamples} luma and {this._chromaSamples} samples a chroma plane; "
        + $"received {luma.Length}, {cb.Length} and {cr.Length}.");

    var data = new byte[this._packetByteCount];
    var target = data.AsSpan();
    var offset = 0;
    var slot = 0;
    uint word = 0;

    for (var pair = 0; pair < this._chromaSamples; ++pair) {
      var lumaIndex = pair << 1;
      _Append(cb[pair], "blue difference", target, ref offset, ref slot, ref word);
      _Append(luma[lumaIndex], "luma", target, ref offset, ref slot, ref word);
      _Append(cr[pair], "red difference", target, ref offset, ref slot, ref word);
      _Append(luma[lumaIndex + 1], "luma", target, ref offset, ref slot, ref word);
    }

    if (slot != 0)
      BinaryPrimitives.WriteUInt32BigEndian(target[offset..], word);

    return data;
  }

  private static void _Append(
    ushort sample,
    string component,
    Span<byte> target,
    ref int offset,
    ref int slot,
    ref uint word) {
    word |= _Ten(sample, component) << (22 - slot * 10);
    if (++slot != 3)
      return;

    BinaryPrimitives.WriteUInt32BigEndian(target[offset..], word);
    offset += 4;
    slot = 0;
    word = 0;
  }

  private static ushort[] _Samples(ReadOnlySpan<byte> plane, int count) {
    var samples = new ushort[count];
    for (var i = 0; i < count; ++i)
      samples[i] = BinaryPrimitives.ReadUInt16LittleEndian(plane[(i * 2)..]);

    return samples;
  }

  private static uint _Ten(ushort sample, string component)
    => sample <= 0x3FF
      ? sample
      : throw new InvalidDataException($"A v210x {component} sample is ten bits wide; {sample} does not fit.");
}
