using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes v408: 4:4:4 YUV with alpha and nothing compressed at all, four bytes a pixel and no chroma
/// subsampling to decide about.
/// </summary>
/// <remarks>
/// The mirror of <see cref="V408VideoDecoder"/>, and the layout is that decoder's own: four bytes a
/// pixel — U, then Y, then V, then alpha — repeating across a row with no padding of any kind, a row
/// exactly <c>width</c> times four bytes and no header ahead of the picture. Nothing is predicted and
/// nothing is entropy coded, so every packet is a key frame and the stream this encoder describes is
/// decodable by that decoder with nothing more than the tag and the picture size.
/// <para/>
/// <b>What goes in, and where the alpha comes from.</b> This package has no 4:4:4 YUV pixel format
/// that carries alpha, so the sample-exact input is <see cref="PixelFormat.Yuv444P8"/> for the three
/// colour planes with every alpha byte written fully opaque — the one value that invents no
/// transparency the source never stated. <see cref="PixelFormat.Rgba32"/>, the format the decoder
/// itself hands back, and <see cref="PixelFormat.Bgra32"/> are taken as well: their colour goes
/// through the package's own converter under the same ITU-R BT.601 studio-swing matrix the decoder
/// applies on the way out, and their fourth byte is copied straight into the packet's alpha, so alpha
/// round-trips exactly while the colour is exact for the matrix and rounded to the sample.
/// <see cref="PixelFormat.Rgb24"/> is converted the same way with opaque alpha. Every other pixel
/// format is refused by name rather than converted through a route that would lose something
/// silently.
/// <para/>
/// <b>What refuses.</b> A stream that is not video or has no pixels, a picture whose geometry differs
/// from the one the encoder was created for, a picture with too little pixel data for its own
/// declared size, and any pixel format not named above.
/// </remarks>
public sealed class V408VideoEncoder : IVideoCodecEncoder<V408VideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("v408");

  private readonly MediaStreamInfo _stream;
  private readonly int _stride;

  private V408VideoEncoder(MediaStreamInfo stream) {
    this._stride = stream.Width * 4;
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
      BitsPerPixel = 32,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "Uncompressed 4:4:4 with alpha (v408)";

  public static CodecTag Codec => _Tag;

  public static V408VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video || stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"v408 encoding requires a video stream with positive dimensions; stream {stream.Index} states "
        + $"{stream.Kind} at {stream.Width}x{stream.Height}.");

    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException(
        $"v408 geometry is fixed at {this._stream.Width}x{this._stream.Height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var planar = _ToPlanar(frame);
    var luma = planar.GetPlaneData(0);
    var cb = planar.GetPlaneData(1);
    var cr = planar.GetPlaneData(2);
    var alpha = _Alpha(frame);
    var width = this._stream.Width;
    var data = new byte[this._stride * this._stream.Height];

    for (var row = 0; row < this._stream.Height; ++row) {
      var line = data.AsSpan(row * this._stride, this._stride);
      var planeBase = row * width;

      for (var x = 0; x < width; ++x) {
        var offset = x * 4;
        var pixel = planeBase + x;
        line[offset] = cb[pixel];
        line[offset + 1] = luma[pixel];
        line[offset + 2] = cr[pixel];
        line[offset + 3] = alpha.IsEmpty ? (byte)255 : alpha[pixel * 4 + 3];
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
    PixelFormat.Rgb24 or PixelFormat.Rgba32 or PixelFormat.Bgra32
      => FastRawImageConverter.Convert(frame, PixelFormat.Yuv444P8, RawImageColorInfo.Bt601Limited),
    _ => throw new NotSupportedException(
      $"v408 takes {PixelFormat.Yuv444P8} samples as they are, or {PixelFormat.Rgba32}, {PixelFormat.Bgra32} and "
      + $"{PixelFormat.Rgb24} through the decoder's own BT.601 studio-swing matrix; a {frame.Format} picture would "
      + "lose something on the way in and is refused."),
  };

  /// <summary>
  /// The source's own pixels where the fourth byte of each is alpha, or an empty span for a source
  /// that carries none — written as fully opaque.
  /// </summary>
  private static ReadOnlySpan<byte> _Alpha(RawImage frame) => frame.Format is PixelFormat.Rgba32 or PixelFormat.Bgra32
    ? frame.PixelData
    : ReadOnlySpan<byte>.Empty;
}
