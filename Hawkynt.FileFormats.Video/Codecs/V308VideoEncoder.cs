using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes v308: 4:4:4 YUV with nothing compressed at all, three bytes a pixel and no chroma
/// subsampling to decide about.
/// </summary>
/// <remarks>
/// The mirror of <see cref="V308VideoDecoder"/>, and the layout is that decoder's own: three bytes a
/// pixel — V, then Y, then U — repeating across a row with no padding of any kind, a row exactly
/// <c>width</c> times three bytes and no header ahead of the picture. Nothing is predicted and nothing
/// is entropy coded, so every packet is a key frame and the stream this encoder describes is decodable
/// by that decoder with nothing more than the tag and the picture size.
/// <para/>
/// <b>What goes in.</b> <see cref="PixelFormat.Yuv444P8"/> is the sample-exact input: every byte of
/// every plane is written as it is, and reading the packet back through the decoder's planes returns
/// the same bytes. <see cref="PixelFormat.Rgb24"/> — the format the decoder itself hands back — is
/// taken as well, converted with the package's own converter under the same ITU-R BT.601 studio-swing
/// matrix the decoder applies on the way out; that is a colour conversion and not a byte copy, so it
/// is exact for the matrix and rounded to the sample. Every other pixel format is refused by name
/// rather than converted through a route that would lose something silently: a format with alpha has
/// no home for it here, and a subsampled one would be interpolated on the way in.
/// <para/>
/// <b>What refuses.</b> A stream that is not video or has no pixels, a picture whose geometry differs
/// from the one the encoder was created for, a picture with too little pixel data for its own
/// declared size, and any pixel format not named above.
/// </remarks>
public sealed class V308VideoEncoder : IVideoCodecEncoder<V308VideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("v308");

  private readonly MediaStreamInfo _stream;
  private readonly int _stride;

  private V308VideoEncoder(MediaStreamInfo stream) {
    this._stride = stream.Width * 3;
    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      Handler = stream.Handler,
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = 24,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "Uncompressed 4:4:4 (v308)";

  public static CodecTag Codec => _Tag;

  public static V308VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video || stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"v308 encoding requires a video stream with positive dimensions; stream {stream.Index} states "
        + $"{stream.Kind} at {stream.Width}x{stream.Height}.");

    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException(
        $"v308 geometry is fixed at {this._stream.Width}x{this._stream.Height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var planar = _ToPlanar(frame);
    var luma = planar.GetPlaneData(0);
    var cb = planar.GetPlaneData(1);
    var cr = planar.GetPlaneData(2);
    var width = this._stream.Width;
    var data = new byte[this._stride * this._stream.Height];

    for (var row = 0; row < this._stream.Height; ++row) {
      var line = data.AsSpan(row * this._stride, this._stride);
      var planeBase = row * width;

      for (var x = 0; x < width; ++x) {
        var offset = x * 3;
        line[offset] = cr[planeBase + x];
        line[offset + 1] = luma[planeBase + x];
        line[offset + 2] = cb[planeBase + x];
      }
    }

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

  private static RawImage _ToPlanar(RawImage frame) => frame.Format switch {
    PixelFormat.Yuv444P8 => frame,
    PixelFormat.Rgb24 => FastRawImageConverter.Convert(frame, PixelFormat.Yuv444P8, RawImageColorInfo.Bt601Limited),
    _ => throw new NotSupportedException(
      $"v308 takes {PixelFormat.Yuv444P8} samples as they are or {PixelFormat.Rgb24} through the decoder's own BT.601 "
      + $"studio-swing matrix; a {frame.Format} picture would lose something on the way in and is refused."),
  };
}
