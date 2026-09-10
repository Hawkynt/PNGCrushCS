using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Creative YUV (<c>cyuv</c>) in the codec's uncompressed UYVY 4:2:2 form.
/// </summary>
/// <remarks>
/// Creative YUV has two packet shapes in real files. The differential form carries 4:1:1 samples
/// behind three sixteen-entry per-frame delta tables; the other is the picture itself as packed
/// U, Y, V, Y 4:2:2 samples, stored bottom row first. Nothing in the stream header distinguishes
/// them — packet length does — and <see cref="CreativeYuvVideoDecoder"/> reads both.
/// <para/>
/// This writer deliberately chooses the uncompressed form. The published descriptions specify how
/// to <em>read</em> the three differential tables but not how an encoder is to choose them, and no
/// reference encoder is available to settle that policy. Inventing one would therefore turn a
/// write-support claim into an uncheckable quality heuristic. The packed form has no such decision:
/// it is a byte layout observed in real <c>cyuv</c> AVI files, and FFmpeg also maps the <c>cyuv</c>
/// four-character code to packed UYVY 4:2:2.
/// <para/>
/// An eight-bit 4:2:2 source is preserved sample for sample apart from the packet's bottom-up row
/// order. Other planar YUV samplings are resited onto the 4:2:2 grid, and other pixel formats are
/// converted under the package's BT.601 studio-swing convention before packing. Every packet is a
/// complete picture and therefore a key frame.
/// <para/>
/// <b>What refuses.</b> A stream that is not video, a picture with no pixels, a width that is not a
/// whole number of four-pixel groups — the same geometry the Creative YUV decoder and FFmpeg require
/// even though the packed shape alone would only need pairs — and a frame whose geometry differs
/// from the stream's.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class CreativeYuvVideoEncoder : IVideoCodecEncoder<CreativeYuvVideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("cyuv");

  private readonly MediaStreamInfo _stream;
  private readonly PackedYuv422Packing _packing;
  private readonly int _stride;
  private readonly int _height;

  private CreativeYuvVideoEncoder(MediaStreamInfo stream) {
    this._packing = PackedYuv422Packing.For(stream, PackedYuv422Order.CbLumaCrLuma, "Creative YUV");
    this._stride = stream.Width * 2;
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
      BitsPerPixel = 16,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "Creative YUV (CYUV)";

  public static CodecTag Codec => _Tag;

  public static CreativeYuvVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Creative YUV can only encode a video stream.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can "
        + "be coded from.");

    if ((stream.Width & 3) != 0)
      throw new NotSupportedException(
        $"Video stream {stream.Index} states a width of {stream.Width}. Creative YUV streams require a whole "
        + "number of four-pixel groups, even when their packets use the uncompressed UYVY shape.");

    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException(
        $"Creative YUV geometry is fixed at {this._stream.Width}x{this._stream.Height}; received "
        + $"{frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        "The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var topDown = this._packing.Pack(this._packing.PlanesOf(frame));
    var data = this._ToBottomUp(topDown);
    packet = new(
      this._stream.Index,
      data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  /// <summary>
  /// Reverses whole packed rows. Creative YUV's raw packet is UYVY like the package's ordinary UYVY
  /// codec, but follows the Windows bitmap convention and stores its last displayed row first.
  /// </summary>
  internal byte[] ToBottomUp(ReadOnlySpan<byte> topDown) {
    var expected = checked(this._stride * this._height);
    if (topDown.Length < expected)
      throw new InvalidDataException(
        $"A {this._stream.Width}x{this._height} Creative YUV packed frame needs {expected} byte(s); "
        + $"received {topDown.Length}.");

    var result = new byte[expected];
    for (var y = 0; y < this._height; ++y)
      topDown.Slice(y * this._stride, this._stride).CopyTo(result.AsSpan((this._height - 1 - y) * this._stride));

    return result;
  }
}
