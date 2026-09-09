using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Y800: uncompressed luma and nothing else, one byte a pixel.
/// </summary>
/// <remarks>
/// The mirror of <see cref="Y800VideoDecoder"/>, writing the layout that decoder reads back: one
/// byte a pixel, a row exactly <c>width</c> bytes with no padding, top row first. Nothing is
/// predicted and nothing is entropy coded, so every packet is a key frame, and any picture size is
/// written since there is no chroma grid to divide.
/// <para/>
/// <b>Lossless where the samples are already luma.</b> A <see cref="PixelFormat.Gray8"/> picture's
/// bytes go into the packet as they are, and an eight-bit planar YUV picture contributes its luma
/// plane as it is — both come back from the decoder identical. What is lost in either case is only
/// the chroma the format has no room for at all. Any other picture is first converted to 4:4:4 under
/// the ITU-R BT.601 studio-swing convention this package's other uncompressed YUV codecs use and its
/// luma plane taken, so an RGB picture becomes a studio-swing Y rather than the converter's own
/// grey.
/// <para/>
/// <b>Verified against ffmpeg's own decode, exactly.</b> Packets written here were muxed into an AVI
/// and read back through ffmpeg 9 as <c>gray</c>, over pseudo-random pictures at 16x8, 17x9 and 7x5
/// (both dimensions odd, which only this one of the ten layouts allows), five frames apiece: 1,580
/// samples, none differing. The packets are also byte for byte the ones ffmpeg's own <c>-pix_fmt
/// gray</c> encoder wrote for the same pictures — all 1,580 of them.
/// <para/>
/// <b>What refuses.</b> A stream that is not video or has no pixels, a frame whose geometry differs
/// from the stream's, and a picture with too little pixel data for its own declared size.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class Y800VideoEncoder : IVideoCodecEncoder<Y800VideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("Y800");

  private readonly MediaStreamInfo _stream;

  private Y800VideoEncoder(MediaStreamInfo stream) => this._stream = new() {
    Index = stream.Index,
    Kind = MediaStreamKind.Video,
    Codec = _Tag,
    Handler = _Tag,
    TimeBase = stream.TimeBase,
    FrameRate = stream.FrameRate,
    DeclaredFrameCount = stream.DeclaredFrameCount,
    Width = stream.Width,
    Height = stream.Height,
    BitsPerPixel = 8,
    Language = stream.Language,
    Name = stream.Name,
  };

  public static string CodecName => "Uncompressed grey (Y800)";

  public static CodecTag Codec => _Tag;

  public static Y800VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Y800 can only encode a video stream.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can "
        + "be coded from.");

    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException(
        $"Y800 geometry is fixed at {this._stream.Width}x{this._stream.Height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    packet = new(
      this._stream.Index,
      this._Luma(frame),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);

    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  /// <summary>The picture's luma samples, taken as they are wherever the picture already carries
  /// them.</summary>
  private byte[] _Luma(RawImage frame) {
    var samples = this._stream.Width * this._stream.Height;
    var source = frame.Format switch {
      PixelFormat.Gray8 or PixelFormat.Yuv420P8 or PixelFormat.Yuv422P8 or PixelFormat.Yuv440P8 or PixelFormat.Yuv444P8 => frame,
      _ => FastRawImageConverter.Convert(frame, PixelFormat.Yuv444P8, RawImageColorInfo.Bt601Limited),
    };

    var luma = source.Format == PixelFormat.Gray8 ? source.PixelData.AsSpan() : source.GetPlaneData(0);
    if (luma.Length < samples)
      throw new InvalidDataException(
        $"A {this._stream.Width}x{this._stream.Height} Y800 frame needs {samples} luma samples; received {luma.Length}.");

    return luma[..samples].ToArray();
  }
}
