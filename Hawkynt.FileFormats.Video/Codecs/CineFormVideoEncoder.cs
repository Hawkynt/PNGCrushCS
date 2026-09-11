using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.CineForm;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Encodes progressive GoPro CineForm as ten-bit 4:2:2 I-frames.</summary>
/// <remarks>
/// The writer targets the oldest widely interoperable three-channel layout: Y, V, U at ten bits,
/// three spatial 2/6 wavelet levels, one raw sixteen-bit lowpass and Annex-C codebook highpasses.
/// CineForm is intra-only in this path, so every input picture becomes one complete key-frame packet.
/// <para/>
/// Source pictures are converted through the repository's existing raw-image converter to planar
/// ten-bit 4:2:2. No native SDK, P/Invoke or third-party package is involved. The packet framing was
/// cross-checked against GoPro's MIT/Apache-2.0 reference SDK and FFmpeg's LGPL encoder; the transform,
/// companding and codebook are shared with the decoder so the two directions cannot drift into
/// different interpretations of the same constants.
/// <para/>
/// The old CineForm encoder family pads height to an eight-line boundary and records the visible
/// height separately; this writer does the same. Width is deliberately restricted to a multiple of
/// sixteen and at least 48 pixels, matching the geometry under which the three wavelet levels are
/// representable by the border filters and accepted by the independent FFmpeg implementation.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class CineFormVideoEncoder : IVideoCodecEncoder<CineFormVideoEncoder> {
  private static readonly CodecTag _codec = CodecTag.FromCharacters("CFHD");

  private readonly MediaStreamInfo _stream;
  private readonly RawImageColorInfo _colour;
  private readonly int _encodedHeight;
  private uint _frameNumber;

  private CineFormVideoEncoder(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("GoPro CineForm can only encode a video stream.");

    if (stream.Width < 48 || (stream.Width & 15) != 0)
      throw new NotSupportedException(
        $"This CineForm encoder needs a width of at least 48 pixels and a multiple of 16 for its three 2/6 wavelet levels; {stream.Width} was supplied.");

    if (stream.Height <= 0)
      throw new NotSupportedException($"A CineForm encoder needs a positive picture height; {stream.Height} was supplied.");

    if (stream.Width > 65_520 || stream.Height > 65_528)
      throw new NotSupportedException(
        $"CineForm's picture dimensions are sixteen-bit values after padding; {stream.Width}x{stream.Height} does not fit this writer's progressive frame header.");

    this._encodedHeight = Math.Max(32, (stream.Height + 7) & ~7);
    if ((long)stream.Width * this._encodedHeight > Array.MaxLength)
      throw new NotSupportedException(
        $"A padded CineForm frame of {stream.Width}x{this._encodedHeight} samples is too large for a managed plane.");

    this._colour = stream.Height > 576 ? RawImageColorInfo.Bt709Limited : RawImageColorInfo.Bt601Limited;
    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = _codec,
      Handler = _codec,
      CodecId = "V_MS/VFW/FOURCC",
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

  public static string CodecName => "GoPro CineForm";

  public static CodecTag Codec => _codec;

  public static CineFormVideoEncoder Create(MediaStreamInfo stream) => new(stream);

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);

    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException(
        $"This CineForm stream is {this._stream.Width}x{this._stream.Height}; a {frame.Width}x{frame.Height} picture arrived.");

    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        $"A {frame.Width}x{frame.Height} {frame.Format} picture needs {frame.MinimumPixelDataLength} bytes and carries {frame.PixelData.Length}.");

    var frameNumber = presentationTimestamp.HasValue
      ? unchecked((ushort)presentationTimestamp.Value)
      : unchecked((ushort)this._frameNumber);

    var bytes = this.EncodeFrame(frame, frameNumber);
    ++this._frameNumber;

    packet = new(
      StreamIndex: this._stream.Index,
      Data: bytes,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);
    return true;
  }

  public IEnumerable<CodedPacket> Flush() => [];

  public MediaStreamInfo DescribeStream() => this._stream;

  internal byte[] EncodeFrame(RawImage frame, ushort frameNumber = 0) {
    var source = frame.Format == PixelFormat.Yuv422P10
      ? frame
      : FastRawImageConverter.Convert(frame, PixelFormat.Yuv422P10, this._colour);

    if (!source.HasEnoughPixelData)
      throw new InvalidDataException(
        $"Conversion to {PixelFormat.Yuv422P10} produced too few bytes for {source.Width}x{source.Height}.");

    var width = this._stream.Width;
    var height = this._stream.Height;
    var chromaWidth = width >> 1;

    var y = new int[width * this._encodedHeight];
    var u = new int[chromaWidth * this._encodedHeight];
    var v = new int[chromaWidth * this._encodedHeight];

    _FillPlane(source.GetPlaneData(0), width, height, y, width, this._encodedHeight, "luma");
    _FillPlane(source.GetPlaneData(1), chromaWidth, height, u, chromaWidth, this._encodedHeight, "blue difference");
    _FillPlane(source.GetPlaneData(2), chromaWidth, height, v, chromaWidth, this._encodedHeight, "red difference");

    // CineForm's measured 4:2:2 channel order is Y, V, U rather than the conventional planar Y,U,V.
    return CineFormPictureEncoder.Encode(y, v, u, width, chromaWidth, this._encodedHeight, height, frameNumber);
  }

  private static void _FillPlane(
    ReadOnlySpan<byte> source, int width, int height,
    int[] target, int targetWidth, int targetHeight, string component) {

    for (var y = 0; y < height; ++y) {
      var sourceRow = y * width * 2;
      var targetRow = y * targetWidth;
      for (var x = 0; x < width; ++x) {
        var sample = BinaryPrimitives.ReadUInt16LittleEndian(source[(sourceRow + x * 2)..]);
        if (sample > 0x3FF)
          throw new InvalidDataException($"A CineForm {component} sample is ten-bit; {sample} does not fit.");
        target[targetRow + x] = sample;
      }
    }

    for (var y = height; y < targetHeight; ++y)
      Array.Copy(target, (height - 1) * targetWidth, target, y * targetWidth, targetWidth);
  }
}
