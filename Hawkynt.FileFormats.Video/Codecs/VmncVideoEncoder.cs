using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Encodes VMware Screen Codec / VMware Video (<c>VMnc</c>).</summary>
/// <remarks>
/// VMnc is an RFB framebuffer-update recording rather than a transform codec. Its inter pictures are
/// therefore literal updates to a persistent screen: unchanged pixels are references to the previous
/// framebuffer, and there is no B-picture or future-reference syntax to manufacture.
/// <para/>
/// This encoder writes the canonical 32-bit little-endian true-colour form (B, G, R, padding in
/// memory). A key picture starts with VMware's <c>WMVi</c> display-mode record and then replaces the
/// whole framebuffer with one Raw RFB rectangle. Between key pictures only the smallest rectangle
/// containing every changed pixel is sent; a completely unchanged picture is a legal framebuffer
/// update containing zero rectangles. A key picture is forced every 120 pictures so a long capture
/// does not acquire an unbounded reference chain.
/// <para/>
/// The wire layout follows the VMnc description published by MultimediaWiki and RFB's
/// FramebufferUpdate/Raw encoding. FFmpeg's LGPL-2.1-or-later <c>vmnc.c</c> decoder is used as the
/// independent interoperability oracle; no external dependency is required at runtime.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class VmncVideoEncoder : IVideoCodecEncoder<VmncVideoEncoder> {

  private const uint _Raw = 0;
  private const uint _ServerInitialization = 0x574D5669; // WMVi
  private const int _KEY_FRAME_INTERVAL = 120;

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("VMnc");

  private readonly MediaStreamInfo _stream;
  private readonly int _width;
  private readonly int _height;

  private byte[]? _previous;
  private int _framesSinceKeyFrame;

  private VmncVideoEncoder(MediaStreamInfo stream) {
    this._width = stream.Width;
    this._height = stream.Height;
    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      Handler = _Tag,
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = 32,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "VMware Screen Codec / VMware Video";

  public static CodecTag Codec => _Tag;

  public static VmncVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("VMware Screen Codec can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"A VMnc encoder needs positive picture dimensions before the muxer is created; {stream.Width}x{stream.Height} was supplied.");
    if (stream.Width > ushort.MaxValue || stream.Height > ushort.MaxValue)
      throw new NotSupportedException(
        $"VMnc rectangle dimensions are unsigned 16-bit values; {stream.Width}x{stream.Height} cannot be represented.");
    if ((long)stream.Width * stream.Height * 4 > int.MaxValue)
      throw new NotSupportedException(
        $"A {stream.Width}x{stream.Height} VMnc framebuffer is too large to represent as one managed packet.");

    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    var picture = LosslessEncoderInput.Prepare(frame, PixelFormat.Bgr24, this._width, this._height, CodecName);
    var current = picture.PixelData;
    var keyFrame = this._previous == null || this._framesSinceKeyFrame >= _KEY_FRAME_INTERVAL;

    using var output = new MemoryStream();
    output.WriteByte(0); // RFB FramebufferUpdate message type
    output.WriteByte(0); // padding

    if (keyFrame) {
      _WriteUInt16BigEndian(output, 2);
      this._WriteDisplayMode(output);
      this._WriteRawRectangle(output, current, 0, 0, this._width, this._height);
      this._framesSinceKeyFrame = 1;
    } else if (_ChangedBounds(current, this._previous!, this._width, this._height) is { } changed) {
      _WriteUInt16BigEndian(output, 1);
      this._WriteRawRectangle(output, current, changed.X, changed.Y, changed.Width, changed.Height);
      ++this._framesSinceKeyFrame;
    } else {
      _WriteUInt16BigEndian(output, 0);
      ++this._framesSinceKeyFrame;
    }

    this._previous = (byte[])current.Clone();
    packet = new(
      this._stream.Index,
      output.ToArray(),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: keyFrame);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  private void _WriteDisplayMode(Stream output) {
    this._WriteRectangleHeader(output, 0, 0, this._width, this._height, _ServerInitialization);
    output.WriteByte(32); // bits stored per pixel
    output.WriteByte(24); // meaningful colour depth
    output.WriteByte(0);  // little-endian pixel values
    output.WriteByte(1);  // true colour
    _WriteUInt16BigEndian(output, 255);
    _WriteUInt16BigEndian(output, 255);
    _WriteUInt16BigEndian(output, 255);
    output.WriteByte(16); // red shift
    output.WriteByte(8);  // green shift
    output.WriteByte(0);  // blue shift
    output.WriteByte(0);
    output.WriteByte(0);
    output.WriteByte(0);
  }

  private void _WriteRawRectangle(Stream output, byte[] bgr, int x, int y, int width, int height) {
    this._WriteRectangleHeader(output, x, y, width, height, _Raw);

    Span<byte> pixel = stackalloc byte[4];
    for (var row = 0; row < height; ++row) {
      var source = ((y + row) * this._width + x) * 3;
      for (var column = 0; column < width; ++column) {
        pixel[0] = bgr[source++];
        pixel[1] = bgr[source++];
        pixel[2] = bgr[source++];
        pixel[3] = 0;
        output.Write(pixel);
      }
    }
  }

  private void _WriteRectangleHeader(Stream output, int x, int y, int width, int height, uint encoding) {
    _WriteUInt16BigEndian(output, checked((ushort)x));
    _WriteUInt16BigEndian(output, checked((ushort)y));
    _WriteUInt16BigEndian(output, checked((ushort)width));
    _WriteUInt16BigEndian(output, checked((ushort)height));
    _WriteUInt32BigEndian(output, encoding);
  }

  private static Rectangle? _ChangedBounds(byte[] current, byte[] previous, int width, int height) {
    var left = width;
    var top = height;
    var right = -1;
    var bottom = -1;

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      if (current[at] == previous[at]
          && current[at + 1] == previous[at + 1]
          && current[at + 2] == previous[at + 2])
        continue;

      left = Math.Min(left, x);
      top = Math.Min(top, y);
      right = Math.Max(right, x);
      bottom = Math.Max(bottom, y);
    }

    return right < left ? null : new(left, top, right - left + 1, bottom - top + 1);
  }

  private static void _WriteUInt16BigEndian(Stream output, ushort value) {
    Span<byte> bytes = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
    output.Write(bytes);
  }

  private static void _WriteUInt32BigEndian(Stream output, uint value) {
    Span<byte> bytes = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
    output.Write(bytes);
  }

  private readonly record struct Rectangle(int X, int Y, int Width, int Height);
}
