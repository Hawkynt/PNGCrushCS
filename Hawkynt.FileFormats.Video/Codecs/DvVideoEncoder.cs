using System;
using System.IO;
using FileFormat.Codecs.Dv;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes DV at 25 and 50 Mbit, in whichever of the four standard-definition profiles the picture
/// handed over fits.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's LGPL-2.1-or-later DV encoder (<c>libavcodec/dvenc.c</c> and
/// <c>jfdctint_template.c</c>); provenance and licence are in
/// <c>Codecs/Dv/THIRD-PARTY-NOTICE.FFmpeg.txt</c>.
/// <para/>
/// <b>Lossy by construction, and it has to be.</b> DV is a fixed-rate recording format: a 525/60
/// frame at 25 Mbit is 120000 bytes whatever is in it, and five macroblocks share exactly 2680 bits
/// of coefficients. So there is no lossless setting to offer and no quality to choose — the encoder's
/// only decision is which coefficients to give the bits to, and a picture that will not fit is
/// quantised harder until it does rather than refused. Every other encoder in this package that is
/// ticked for writing is either lossless or refuses what it cannot hold exactly; this one cannot be,
/// and the row in the README says so.
/// <para/>
/// <b>The picture chooses the profile.</b> Two things are fixed by the raster — 720x480 is 525/60 at
/// 30000/1001, 720x576 is 625/50 at 25 — and the third is chosen by how the picture's colour is
/// sampled, exactly as FFmpeg's <c>av_dv_codec_profile2</c> chooses it. A 4:2:2 planar picture is
/// written as DVCPRO50, at twice the size; a 4:2:0 one at 625/50 is written as IEC 61834's 4:2:0
/// arrangement; anything else becomes 4:1:1 at 525/60 and 4:2:0 at 625/50, which is what a DV
/// recorder of each system wrote. That is why a caller wanting DVCPRO50 hands over 4:2:2 planes
/// rather than setting a flag: there is nothing else in the description that could carry the choice,
/// and inventing one would put the same fact in two places.
/// <para/>
/// <b>Measured by having FFmpeg decode it back.</b> The measurement is in
/// <c>codec-notes.md</c>; the summary is that FFmpeg accepts every frame, agrees about the geometry
/// and the profile, and the round trip through DV's own quantiser costs what DV costs.
/// <para/>
/// <b>What refuses.</b> A raster that is not one of the two DV defines; a picture whose size is not
/// the stream's; and a stream that changes sampling part way through, since that would change the
/// frame size and no container describes a stream whose frames are two different lengths.
/// </remarks>
public sealed class DvVideoEncoder : IVideoCodecEncoder<DvVideoEncoder> {

  /// <summary>
  /// The code this encoder's packets are named by.
  /// </summary>
  /// <remarks>
  /// <c>dvsd</c> for every profile, including DVCPRO50. The code says only that a stream is DV — the
  /// frame states its own profile and every DV decoder reads it from there — and <c>dvsd</c> is the
  /// first code containers map this codec to and the one FFmpeg's own AVI muxer writes.
  /// </remarks>
  private static readonly CodecTag _Tag = CodecTag.FromCharacters("dvsd");

  private readonly MediaStreamInfo _stream;
  private readonly int _width;
  private readonly int _height;
  private readonly DvSegmentEncoder.Scratch _scratch = new();

  private DvProfile? _profile;
  private DvGeometry.Segment[] _segments = [];

  private DvVideoEncoder(MediaStreamInfo stream) {
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
      BitsPerPixel = 24,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "DV (IEC 61834 / SMPTE 314M)";

  public static CodecTag Codec => _Tag;

  public static DvVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("DV can only encode a video stream.");

    if (DvProfile.ForPicture(stream.Width, stream.Height, DvSampling.FourOneOne) == null
        && DvProfile.ForPicture(stream.Width, stream.Height, DvSampling.FourTwoZero) == null)
      throw new NotSupportedException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}. DV defines two "
        + "standard-definition rasters and no others — 720x480 for 525/60 and 720x576 for 625/50 — and a frame is a "
        + "fixed number of DIF blocks laid out for one of them, so there is nothing to scale or pad a different "
        + "raster into.");

    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);

    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"DV geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    packet = new(
      this._stream.Index,
      this.EncodePlanes(_Planes(frame, this._width, this._height, out var sampling), sampling),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  /// <summary>
  /// Codes one frame from planes that are already at the sampling the profile wants.
  /// </summary>
  /// <remarks>
  /// The DIF skeleton is written first and the picture into the space it leaves, because the video
  /// blocks are not contiguous: the audio blocks are interleaved between every third video segment
  /// and the control blocks open every sequence, so a segment's five macroblocks have to be placed
  /// against a laid-out frame rather than appended to a stream.
  /// </remarks>
  internal byte[] EncodePlanes(DvPlanes planes, DvSampling sampling) {
    var profile = DvProfile.ForPicture(planes.Width, planes.Height, sampling)
      ?? throw new NotSupportedException(
        $"No DV profile codes a {planes.Width}x{planes.Height} picture at {_SamplingName(sampling)}.");

    if (this._profile != null && this._profile != profile)
      throw new NotSupportedException(
        $"This stream began as {this._profile.Name} and this frame would be written as {profile.Name}. The two are "
        + "different frame sizes, and no container describes a DV stream whose frames are two different lengths.");

    if (this._profile == null) {
      this._segments = DvGeometry.Segments(profile);
      this._profile = profile;
    }

    var frame = new byte[profile.FrameSize];
    DvFrameLayout.Format(frame, profile);

    foreach (var segment in this._segments)
      DvSegmentEncoder.Encode(frame, profile, segment, planes, this._scratch);

    return frame;
  }

  /// <summary>
  /// Takes a picture down to the planes DV codes, at the sampling the picture itself implies.
  /// </summary>
  /// <remarks>
  /// A planar eight-bit source is used as it stands — its samples reach the transform untouched, so
  /// the only loss is DV's own — and anything else, the <see cref="PixelFormat.Rgb24"/> the decoders
  /// here hand back included, is first converted under the ITU-R BT.601 studio-swing convention this
  /// package's decoders display with.
  /// </remarks>
  private static DvPlanes _Planes(RawImage frame, int width, int height, out DvSampling sampling) {
    sampling = frame.Format switch {
      PixelFormat.Yuv422P8 => DvSampling.FourTwoTwo,
      PixelFormat.Yuv420P8 when height == 576 => DvSampling.FourTwoZero,
      // 525/60 recorders wrote 4:1:1 and 625/50 recorders 4:2:0; a picture that says nothing about
      // its own sampling is written the way a recorder of its system would have written it.
      _ => height == 576 ? DvSampling.FourTwoZero : DvSampling.FourOneOne,
    };

    if (sampling == DvSampling.FourOneOne) {
      var (luma411, cb411, cr411) = Yuv411Planes.FromImage(frame);
      return new() {
        Width = width,
        Height = height,
        ChromaWidth = width / 4,
        ChromaHeight = height,
        Luma = luma411,
        Cb = cb411,
        Cr = cr411,
      };
    }

    var target = sampling == DvSampling.FourTwoTwo ? PixelFormat.Yuv422P8 : PixelFormat.Yuv420P8;
    var source = frame.Format == target
      ? frame
      : FastRawImageConverter.Convert(frame, target, RawImageColorInfo.Bt601Limited);

    var (chromaWidth, chromaHeight) = source.GetPlaneDimensions(1);
    return new() {
      Width = width,
      Height = height,
      ChromaWidth = chromaWidth,
      ChromaHeight = chromaHeight,
      Luma = source.GetPlaneData(0).ToArray(),
      Cb = source.GetPlaneData(1).ToArray(),
      Cr = source.GetPlaneData(2).ToArray(),
    };
  }

  private static string _SamplingName(DvSampling sampling) => sampling switch {
    DvSampling.FourOneOne => "4:1:1",
    DvSampling.FourTwoZero => "4:2:0",
    _ => "4:2:2",
  };
}
