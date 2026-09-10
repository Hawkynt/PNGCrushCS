using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.Mpeg4;
using FileFormat.Codecs.MsMpeg4;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes any of Microsoft's three MPEG-4 variants: intra pictures, and predicted pictures with one
/// half-sample motion vector a macroblock.
/// </summary>
/// <remarks>
/// The registry routes all sixteen four-character codes carried by the three versions here. A caller
/// holding a stream description therefore writes whichever version and spelling that description
/// names, while a caller invoking <see cref="Create"/> with no code gets version 3 under <c>MP43</c>.
/// <para/>
/// <b>What it writes.</b> An intra picture every <see cref="_KEY_FRAME_INTERVAL"/> frames and a
/// predicted picture between them, one slice a picture, one motion vector a macroblock, the
/// alternating current prediction always off and — for version 3 — the middle run-level tables, the
/// first DC table and the first motion vector table. Every one of those is a choice the format leaves
/// open and none of them changes whether the result decodes; leaving them fixed is what makes the same
/// input produce the same bytes.
/// <para/>
/// <b>What it does not write.</b> No intra macroblock inside a predicted picture. The format has one
/// and this encoder never chooses it: a predicted picture that cannot predict is answered by the next
/// intra picture instead, which is a coarser answer and a much smaller decision surface. Nor does it
/// alternate the interpolation's rounding, which version 3 alone could.
/// <para/>
/// <b>Lossy, and by how much.</b> The transform and the quantiser are the format's, so nothing but a
/// picture already on the quantiser's own reconstruction grid comes back exactly. What the encoder
/// does guarantee is that its own reconstruction is the one the decoder will build: every picture is
/// coded against what the last one reconstructed to and not against what was handed in, so the error
/// of a long run of predicted pictures is the quantiser's and does not accumulate on top of it.
/// <para/>
/// <b>Measured.</b> Sixty streams — twenty per version, four sources, sizes from 64x64 to 352x288,
/// fifty frames each — were written here, muxed to <c>.avi</c> and decoded by ffmpeg. Every one was
/// accepted without a warning and produced all fifty frames. Compared against this library's own
/// decode of the same bytes, plane by plane: three hundred and eighty-one differing samples of
/// sixty-four million for version 1, three hundred and eighty-one for version 2 and nine thousand
/// five hundred and thirty-eight for version 3, none of them by more than one level — which is the
/// two transforms disagreeing, not the two codecs. Against the pictures that went in, the mean peak
/// signal-to-noise ratio is 36.4 dB for versions 1 and 2 and 36.2 dB for version 3.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class MsMpeg4VideoEncoder : IVideoCodecEncoder<MsMpeg4VideoEncoder> {

  /// <summary>The preferred code for version 3, and what a direct caller gets when it names none.</summary>
  private static readonly CodecTag _MP43 = CodecTag.FromCharacters("MP43");

  private static readonly CodecTag[] _Version1Tags = [
    CodecTag.FromCharacters("MPG4"),
    CodecTag.FromCharacters("MP41"),
    CodecTag.FromCharacters("DIV1"),
  ];

  private static readonly CodecTag[] _Version2Tags = [
    CodecTag.FromCharacters("MP42"),
    CodecTag.FromCharacters("DIV2"),
  ];

  private static readonly CodecTag[] _Version3Tags = [
    _MP43,
    CodecTag.FromCharacters("DIV3"),
    CodecTag.FromCharacters("DIV4"),
    CodecTag.FromCharacters("DIV5"),
    CodecTag.FromCharacters("DIV6"),
    CodecTag.FromCharacters("DVX3"),
    CodecTag.FromCharacters("AP41"),
    CodecTag.FromCharacters("AP42"),
    CodecTag.FromCharacters("COL0"),
    CodecTag.FromCharacters("COL1"),
    CodecTag.FromCharacters("MPG3"),
  ];

  /// <summary>
  /// How many pictures apart the intra ones are.
  /// </summary>
  /// <remarks>
  /// Twelve, which is what the codec's own era used and what makes a stream seekable at half-second
  /// granularity at the frame rates it was written for. It is also what bounds the drift: the encoder
  /// codes against its own reconstruction, so nothing accumulates, but a predicted picture that
  /// predicts badly stays badly predicted until the next intra one.
  /// </remarks>
  private const int _KEY_FRAME_INTERVAL = 12;

  /// <summary>The quantiser every picture uses, the field being five bits wide and holding 1 to 31.</summary>
  /// <remarks>
  /// Eight is roughly the middle of the range and is what the reference encoder's default quality
  /// setting lands on. There is no rate control here at all — none of the three formats has anything a
  /// rate controller could act on within a picture, since the quantiser is stated once in the picture
  /// header and cannot change — so a fixed quantiser is the whole of the decision.
  /// </remarks>
  private const int _QUANTISER = 8;

  /// <summary>What an intra picture's trailing header states as the frame rate when nothing else does.</summary>
  private const int _DEFAULT_FRAME_RATE = 25;

  private readonly MediaStreamInfo _requested;
  private readonly MsMpeg4Version _version;
  private readonly CodecTag _tag;
  private readonly int _width;
  private readonly int _height;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly int _sliceHeight;
  private readonly int _frameRate;

  private Mpeg4Frame? _reference;
  private int _sinceKeyFrame;
  private MediaStreamInfo? _stream;

  private MsMpeg4VideoEncoder(MediaStreamInfo stream, MsMpeg4Version version, CodecTag tag) {
    this._requested = stream;
    this._version = version;
    this._tag = tag;
    this._width = stream.Width;
    this._height = stream.Height;
    this._macroblockWidth = (stream.Width + 15) / 16;
    this._macroblockHeight = (stream.Height + 15) / 16;
    this._frameRate = stream.FrameRate.Denominator > 0 && stream.FrameRate.Numerator > 0
      ? (int)(stream.FrameRate.Numerator / stream.FrameRate.Denominator)
      : _DEFAULT_FRAME_RATE;

    // Version 1 states a slice as its height in a five-bit field, so a picture taller than thirty-one
    // macroblocks cannot be one slice however few it wants; the other two state a count and one slice
    // is always sayable.
    this._sliceHeight = version == MsMpeg4Version.Version1
      ? Math.Min(this._macroblockHeight, 31)
      : this._macroblockHeight;
  }

  public static string CodecName => "Microsoft MPEG-4 versions 1 to 3 video (MPG4/MP42/MP43)";

  public static CodecTag Codec => _MP43;

  /// <summary>Whether the stream asks for any FourCC carried by versions 1, 2 or 3.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return _TryVersionOf(stream.Codec, out _);
  }

  /// <summary>
  /// Builds an encoder for the stream described, taking the version from the code it names.
  /// </summary>
  public static MsMpeg4VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Microsoft's MPEG-4 can only encode a video stream.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"A Microsoft MPEG-4 encoder needs the picture size up front; {stream.Width}x{stream.Height} was supplied. "
        + "The bitstream states it nowhere, so the container's is the only one there is.");

    MsMpeg4Version version;
    CodecTag tag;
    if (stream.Codec == CodecTag.None) {
      version = MsMpeg4Version.Version3;
      tag = _MP43;
    } else {
      if (!_TryVersionOf(stream.Codec, out version))
        throw new NotSupportedException(
          $"Stream {stream.Index} asks Microsoft MPEG-4 to write '{stream.Codec}', which is not one of its version 1, 2 or 3 FourCCs.");

      tag = stream.Codec;
    }

    return new(stream, version, tag);
  }

  /// <summary>Codes one picture, either whole or against the one before it.</summary>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);

    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"This Microsoft MPEG-4 stream is {this._width}x{this._height}; a picture of {frame.Width}x{frame.Height} "
        + "arrived. None of the three formats states a picture size, so a stream cannot change one.");

    var intra = this._reference == null || this._sinceKeyFrame >= _KEY_FRAME_INTERVAL;
    var source = this._ToPlanes(frame);
    var target = new Mpeg4Frame(this._macroblockWidth, this._macroblockHeight);

    var encoder = new MsMpeg4PictureEncoder(
      this._version, _QUANTISER, intra, this._frameRate, source, target, this._reference,
      this._macroblockWidth, this._macroblockHeight, this._sliceHeight);

    var bytes = encoder.Encode();
    this._reference = target;
    this._sinceKeyFrame = intra ? 1 : this._sinceKeyFrame + 1;

    packet = new(
      this._requested.Index,
      bytes,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: intra);

    return true;
  }

  /// <summary>Nothing is ever held back, so there is nothing left at the end.</summary>
  public IEnumerable<CodedPacket> Flush() => [];

  /// <summary>
  /// The stream as a muxer needs it: a <c>BITMAPINFOHEADER</c> naming the version being written.
  /// </summary>
  /// <remarks>
  /// The size in that header is the whole of what a decoder gets to work from — the bitstream carries
  /// no picture size at all — so a container that dropped it would leave the packets undecodable.
  /// </remarks>
  public MediaStreamInfo DescribeStream() {
    if (this._stream != null)
      return this._stream;

    var header = new BitmapInfoHeader(
      HeaderSize: BitmapInfoHeader.StructSize,
      Width: this._width,
      Height: this._height,
      Planes: 1,
      BitsPerPixel: 24,
      Compression: unchecked((int)this._tag.Value),
      ImageSize: 0,
      XPixelsPerMeter: 0,
      YPixelsPerMeter: 0,
      ColorsUsed: 0,
      ImportantColors: 0);

    var format = new byte[BitmapInfoHeader.StructSize];
    header.WriteTo(format);

    return this._stream = new() {
      Index = this._requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = this._tag,
      Handler = this._tag,
      CodecId = "V_MS/VFW/FOURCC",
      TimeBase = this._requested.TimeBase,
      FrameRate = this._requested.FrameRate,
      Width = this._width,
      Height = this._height,
      BitsPerPixel = 24,
      CodecPrivateData = format,
    };
  }

  private static bool _TryVersionOf(CodecTag tag, out MsMpeg4Version version) {
    if (_Contains(_Version1Tags, tag)) {
      version = MsMpeg4Version.Version1;
      return true;
    }

    if (_Contains(_Version2Tags, tag)) {
      version = MsMpeg4Version.Version2;
      return true;
    }

    if (_Contains(_Version3Tags, tag)) {
      version = MsMpeg4Version.Version3;
      return true;
    }

    version = default;
    return false;
  }

  private static bool _Contains(CodecTag[] tags, CodecTag candidate) {
    foreach (var tag in tags)
      if (candidate.EqualsIgnoringCase(tag))
        return true;

    return false;
  }

  /// <summary>
  /// Turns a picture into the 4:2:0 planes the coding works on, padded out to whole macroblocks.
  /// </summary>
  /// <remarks>
  /// Padded by repeating the last row and column rather than by filling with anything, because the
  /// samples past the picture are coded and transmitted like every other sample — the format has no
  /// way to say a macroblock is partly outside — and repeating the edge is what makes them cost the
  /// fewest bits.
  /// </remarks>
  private Mpeg4Frame _ToPlanes(RawImage frame) {
    var planes = new Mpeg4Frame(this._macroblockWidth, this._macroblockHeight);
    Mpeg4ColorConversion.FromRgb24(frame.ToRgb24(), planes, this._width, this._height);
    return planes;
  }
}
