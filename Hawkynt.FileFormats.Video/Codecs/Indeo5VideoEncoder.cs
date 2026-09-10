using System;
using FileFormat.Bmp;
using FileFormat.Codecs.Indeo;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Encodes Intel Indeo Video Interactive 5 (<c>IV50</c>).</summary>
/// <remarks>
/// The writer deliberately targets the codec's basic profile: YVU9, one band per plane, one tile per
/// band and an independently coded intra picture in every packet. Luminance uses four 8x8 slant
/// blocks per 16x16 macroblock and each chrominance plane uses the corresponding 4x4 slant block.
/// That is the smallest complete Indeo 5 writer that stays inside the format rather than inventing a
/// private subset a second decoder could not understand.
/// <para/>
/// Indeo 5 is lossy. The encoder uses the least destructive global quantiser the format defines, but
/// the eight-by-eight luminance matrix still coarsens some alternating-current coefficients. Every
/// packet is therefore a key frame, which costs compression efficiency but removes inter-picture
/// error accumulation and keeps the writer independent of motion estimation.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class Indeo5VideoEncoder : IVideoCodecEncoder<Indeo5VideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("IV50");

  private readonly MediaStreamInfo _stream;
  private readonly Indeo5Encoder _encoder;

  private Indeo5VideoEncoder(MediaStreamInfo stream) {
    this._encoder = new(stream.Width, stream.Height);

    // AVI/VfW identifies IV50 through a BITMAPINFOHEADER. Matroska's V_MS/VFW/FOURCC mapping carries
    // the same structure as CodecPrivate, so describing it here rather than letting AVI synthesize one
    // also makes the stream description useful to containers other than AVI.
    var header = new BitmapInfoHeader(
      HeaderSize: BitmapInfoHeader.StructSize,
      Width: stream.Width,
      Height: stream.Height,
      Planes: 1,
      BitsPerPixel: 24,
      Compression: unchecked((int)_Tag.Value),
      ImageSize: 0,
      XPixelsPerMeter: 0,
      YPixelsPerMeter: 0,
      ColorsUsed: 0,
      ImportantColors: 0);

    var format = new byte[BitmapInfoHeader.StructSize];
    header.WriteTo(format);

    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      Handler = _Tag,
      CodecId = "V_MS/VFW/FOURCC",
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = 24,
      CodecPrivateData = format,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "Intel Indeo Video Interactive 5";

  public static CodecTag Codec => _Tag;

  public static Indeo5VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Intel Indeo Video Interactive 5 can only encode a video stream.");
    if (stream.Width is < 1 or > 0x1FFF || stream.Height is < 1 or > 0x1FFF)
      throw new NotSupportedException(
        $"An Indeo 5 encoder needs a picture from 1x1 through 8191x8191; {stream.Width}x{stream.Height} was supplied.");
    if (stream.BitsPerPixel is not (0 or 24))
      throw new NotSupportedException(
        $"Video stream {stream.Index} asks for {stream.BitsPerPixel} bits per pixel. IV50's VfW stream "
        + "description is twenty-four bits per pixel and this encoder writes that representation only.");

    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);

    packet = new(
      this._stream.Index,
      this._encoder.Encode(frame),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;
}
