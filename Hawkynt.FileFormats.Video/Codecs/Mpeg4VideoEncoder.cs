using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.Mpeg4;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes MPEG-4 Part 2 rectangular Advanced Simple video with I-, P- and B-VOPs.
/// </summary>
/// <remarks>
/// The encoder writes a twelve-picture GOP with two bidirectionally coded pictures between anchors:
/// <c>I B B P B B P B B P B B I</c>. P-VOPs use zero-vector temporal prediction plus a coded
/// residual; B-VOP macroblocks choose independently between the preceding anchor, the following
/// anchor and their interpolated average. The latter is selected by source-sample squared error before
/// quantisation. Motion search is deliberately not mixed into this first predictive implementation:
/// it is an encoder optimisation, while I/P/B syntax, reference reconstruction and decode-order
/// packetisation are format correctness.
/// <para/>
/// Every anchor is reconstructed locally from the quantised coefficients before it becomes a
/// reference. Predicting from the unquantised source instead would make this encoder and every
/// conforming decoder disagree from the first P-VOP onward and turn quantisation error into drift.
/// <para/>
/// B-VOPs require packet reordering. <see cref="TryEncode"/> therefore may accept a source picture and
/// return no packet yet, or return a packet belonging to an earlier source picture. Presentation
/// timestamps stay with their pictures; decode timestamps consume the input timeline in coding order.
/// <para/>
/// The syntax is taken from ISO/IEC 14496-2 clauses 6 and 7 and Annex B. FFmpeg is used only as a
/// conformance oracle for the produced bytes; no FFmpeg implementation code is copied here.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class Mpeg4VideoEncoder : IVideoCodecEncoder<Mpeg4VideoEncoder> {

  private static readonly CodecTag _MP4V = CodecTag.FromCharacters("mp4v");

  /// <summary>The thirteen-bit dimensions of a rectangular video object layer.</summary>
  private const int _MAX_DIMENSION = (1 << 13) - 1;

  /// <summary>The five-bit quantiser written in every VOP.</summary>
  private const int _QUANTISER = 8;

  /// <summary>Distance between intra anchors in display order.</summary>
  private const int _KEY_FRAME_INTERVAL = 12;

  /// <summary>Bidirectionally coded pictures between consecutive anchors.</summary>
  private const int _B_FRAMES = 2;

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly int _timeIncrementResolution;
  private readonly int _timeIncrementStep;
  private readonly List<PendingFrame> _pending = [];
  private readonly Queue<CodedPacket> _ready = new();
  private readonly Queue<long?> _decodeTimestamps = new();

  private MediaStreamInfo? _stream;
  private Mpeg4Frame? _anchor;
  private long _anchorSeconds;
  private long _displayIndex;

  private Mpeg4VideoEncoder(MediaStreamInfo stream, int resolution, int step) {
    this._requested = stream;
    this._width = stream.Width;
    this._height = stream.Height;
    this._macroblockWidth = (stream.Width + 15) / 16;
    this._macroblockHeight = (stream.Height + 15) / 16;
    this._timeIncrementResolution = resolution;
    this._timeIncrementStep = step;
  }

  public static string CodecName => "MPEG-4 Part 2 video (ISO/IEC 14496-2)";

  public static CodecTag Codec => _MP4V;

  /// <summary>Builds a predictive MPEG-4 Part 2 encoder for a fixed rectangular picture size.</summary>
  public static Mpeg4VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("MPEG-4 Part 2 can only encode a video stream.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"An MPEG-4 Part 2 encoder needs the picture size up front; {stream.Width}x{stream.Height} was supplied.");

    if (stream.Width > _MAX_DIMENSION || stream.Height > _MAX_DIMENSION)
      throw new NotSupportedException(
        $"MPEG-4 Part 2's rectangular video object layer states each dimension in thirteen bits; "
        + $"{stream.Width}x{stream.Height} exceeds {_MAX_DIMENSION} in at least one direction.");

    var (resolution, step) = _Timing(stream.FrameRate);
    return new(stream, resolution, step);
  }

  /// <summary>
  /// Accepts one display-order picture and returns the next coding-order packet when one is available.
  /// </summary>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);

    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"This MPEG-4 Part 2 stream is {this._width}x{this._height}; a picture of {frame.Width}x{frame.Height} arrived.");

    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        "The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var index = this._displayIndex++;
    var pending = new PendingFrame(this._ToPlanes(frame), index, presentationTimestamp);
    this._decodeTimestamps.Enqueue(presentationTimestamp);

    if (this._anchor == null) {
      var (seconds, increment) = this._PictureTime(index);
      var encoder = new Mpeg4PictureEncoder(
        pending.Source,
        forwardReference: null,
        backwardReference: null,
        Mpeg4VideoObjectPlane.IntraCoded,
        this._width,
        this._height,
        this._macroblockWidth,
        this._macroblockHeight,
        _QUANTISER,
        this._timeIncrementResolution,
        increment,
        moduloSeconds: checked((int)seconds),
        roundingType: 0);

      var bytes = encoder.Encode();
      this._anchor = encoder.Reconstructed;
      this._anchorSeconds = seconds;
      this._ready.Enqueue(this._Packet(bytes, pending.PresentationTimestamp, isKeyFrame: true));
    } else {
      this._pending.Add(pending);
      if (this._pending.Count == _B_FRAMES + 1)
        this._EncodeGroup();
    }

    if (this._ready.TryDequeue(out packet))
      return true;

    packet = default;
    return false;
  }

  /// <summary>
  /// Emits packets already delayed by B-VOP reordering, then turns a short tail with no following
  /// anchor into ordinary P-VOPs so no input picture is discarded at end of stream.
  /// </summary>
  public IEnumerable<CodedPacket> Flush() {
    while (this._ready.TryDequeue(out var ready))
      yield return ready;

    foreach (var pending in this._pending) {
      var keyFrame = pending.DisplayIndex % _KEY_FRAME_INTERVAL == 0;
      var codingType = keyFrame ? Mpeg4VideoObjectPlane.IntraCoded : Mpeg4VideoObjectPlane.PredictiveCoded;
      var oldSeconds = this._anchorSeconds;
      var (seconds, increment) = this._PictureTime(pending.DisplayIndex);
      var encoder = new Mpeg4PictureEncoder(
        pending.Source,
        codingType == Mpeg4VideoObjectPlane.IntraCoded ? null : this._anchor,
        backwardReference: null,
        codingType,
        this._width,
        this._height,
        this._macroblockWidth,
        this._macroblockHeight,
        _QUANTISER,
        this._timeIncrementResolution,
        increment,
        moduloSeconds: checked((int)(seconds - oldSeconds)),
        roundingType: (int)(pending.DisplayIndex & 1));

      var bytes = encoder.Encode();
      this._anchor = encoder.Reconstructed;
      this._anchorSeconds = seconds;
      yield return this._Packet(bytes, pending.PresentationTimestamp, keyFrame);
    }

    this._pending.Clear();

    while (this._ready.TryDequeue(out var ready))
      yield return ready;
  }

  /// <summary>
  /// Describes the stream as the VFW-style <c>mp4v</c> form used by AVI and accepted by Matroska.
  /// </summary>
  /// <remarks>
  /// The VOL itself is repeated in every packet, so the private bytes need only be the container's
  /// conventional BITMAPINFOHEADER. This also keeps codec configuration out of the container writer:
  /// a decoder starting with an intra packet learns the same geometry and coding tools from the
  /// bitstream itself.
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
      Compression: unchecked((int)_MP4V.Value),
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
      Codec = _MP4V,
      Handler = _MP4V,
      CodecId = "V_MS/VFW/FOURCC",
      TimeBase = this._requested.TimeBase,
      FrameRate = this._requested.FrameRate,
      DeclaredFrameCount = this._requested.DeclaredFrameCount,
      Width = this._width,
      Height = this._height,
      BitsPerPixel = 24,
      CodecPrivateData = format,
      Language = this._requested.Language,
      Name = this._requested.Name,
    };
  }

  /// <summary>Codes one anchor followed by the two B-VOPs that precede it in display order.</summary>
  private void _EncodeGroup() {
    var oldAnchor = this._anchor!;
    var oldAnchorSeconds = this._anchorSeconds;
    var next = this._pending[^1];
    var keyFrame = next.DisplayIndex % _KEY_FRAME_INTERVAL == 0;
    var codingType = keyFrame ? Mpeg4VideoObjectPlane.IntraCoded : Mpeg4VideoObjectPlane.PredictiveCoded;
    var (nextSeconds, nextIncrement) = this._PictureTime(next.DisplayIndex);

    var anchorEncoder = new Mpeg4PictureEncoder(
      next.Source,
      codingType == Mpeg4VideoObjectPlane.IntraCoded ? null : oldAnchor,
      backwardReference: null,
      codingType,
      this._width,
      this._height,
      this._macroblockWidth,
      this._macroblockHeight,
      _QUANTISER,
      this._timeIncrementResolution,
      nextIncrement,
      moduloSeconds: checked((int)(nextSeconds - oldAnchorSeconds)),
      roundingType: (int)(next.DisplayIndex & 1));

    var anchorBytes = anchorEncoder.Encode();
    var newAnchor = anchorEncoder.Reconstructed;
    this._ready.Enqueue(this._Packet(anchorBytes, next.PresentationTimestamp, keyFrame));

    for (var index = 0; index < _B_FRAMES; ++index) {
      var between = this._pending[index];
      var (seconds, increment) = this._PictureTime(between.DisplayIndex);
      var bEncoder = new Mpeg4PictureEncoder(
        between.Source,
        oldAnchor,
        newAnchor,
        Mpeg4VideoObjectPlane.BidirectionallyCoded,
        this._width,
        this._height,
        this._macroblockWidth,
        this._macroblockHeight,
        _QUANTISER,
        this._timeIncrementResolution,
        increment,
        moduloSeconds: checked((int)(seconds - oldAnchorSeconds)),
        roundingType: 0,
        anchorMotion: anchorEncoder.Motion);

      var bytes = bEncoder.Encode();
      this._ready.Enqueue(this._Packet(bytes, between.PresentationTimestamp, isKeyFrame: false));
    }

    this._anchor = newAnchor;
    this._anchorSeconds = nextSeconds;
    this._pending.Clear();
  }

  private CodedPacket _Packet(byte[] bytes, long? presentationTimestamp, bool isKeyFrame) {
    var decodeTimestamp = this._decodeTimestamps.Count == 0 ? null : this._decodeTimestamps.Dequeue();
    return new(
      this._requested.Index,
      bytes,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: decodeTimestamp,
      IsKeyFrame: isKeyFrame);
  }

  private Mpeg4Frame _ToPlanes(RawImage frame) {
    var source = new Mpeg4Frame(this._macroblockWidth, this._macroblockHeight);
    Mpeg4ColorConversion.FromRgb24(frame.ToRgb24(), source, this._width, this._height);
    return source;
  }

  private (long Seconds, int Increment) _PictureTime(long displayIndex) {
    var absoluteTicks = checked(displayIndex * this._timeIncrementStep);
    return (absoluteTicks / this._timeIncrementResolution, (int)(absoluteTicks % this._timeIncrementResolution));
  }

  /// <summary>
  /// Chooses an exact VOP clock for ordinary rational frame rates, and a close legal one otherwise.
  /// </summary>
  private static (int Resolution, int Step) _Timing(Rational frameRate) {
    if (frameRate.IsKnown && frameRate.Numerator > 0 && frameRate.Denominator > 0) {
      var divisor = _GreatestCommonDivisor(frameRate.Numerator, frameRate.Denominator);
      var resolution = frameRate.Numerator / divisor;
      var step = frameRate.Denominator / divisor;

      if (resolution is > 0 and <= ushort.MaxValue && step > 0 && step <= int.MaxValue)
        return ((int)resolution, (int)step);

      var rate = frameRate.ToDouble();
      if (double.IsFinite(rate) && rate > 0) {
        const int fallbackResolution = 60000;
        var fallbackStep = (int)Math.Clamp(
          Math.Round(fallbackResolution / rate, MidpointRounding.AwayFromZero),
          1d,
          int.MaxValue);
        return (fallbackResolution, fallbackStep);
      }
    }

    return (25, 1);
  }

  private static long _GreatestCommonDivisor(long left, long right) {
    left = Math.Abs(left);
    right = Math.Abs(right);
    while (right != 0)
      (left, right) = (right, left % right);

    return left == 0 ? 1 : left;
  }

  private readonly record struct PendingFrame(
    Mpeg4Frame Source,
    long DisplayIndex,
    long? PresentationTimestamp);
}
