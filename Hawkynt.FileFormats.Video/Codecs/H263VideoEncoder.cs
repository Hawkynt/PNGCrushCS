using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.H263;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes H.263 I/P pictures, H.263+ custom picture formats, and optionally Annex O temporal
/// B-pictures with coding/display reordering.
/// </summary>
/// <remarks>
/// The default remains an immediately emitted I/P stream, preserving the packet-per-input behaviour
/// of the original encoder. Set <see cref="BidirectionalPicturesBetweenReferences"/> before the first
/// input picture to enable Annex O temporal scalability. Then source pictures are accepted in display
/// order, the future P anchor is emitted first, and the B-pictures between the two anchors follow it in
/// coding order with PTS/DTS recording the difference.
/// <para/>
/// Every P anchor is reconstructed by this package's decoder before it can become a reference. B
/// pictures are non-reference enhancement pictures, so their reconstruction never enters that loop.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class H263VideoEncoder : IVideoCodecEncoder<H263VideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("H263");
  private const int _QUANTISER = 8;
  private const int _TEMPORAL_REFERENCE_PERIOD = 256;
  private const int _GROUP_SIZE = 12;
  private const int _MAX_BETWEEN_REFERENCES = _GROUP_SIZE - 2;

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;
  private readonly int _sourceFormat;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly H263VideoDecoder _reconstruction;
  private readonly List<Pending> _pending = [];
  private readonly Queue<CodedPacket> _ready = new();
  private readonly Queue<long?> _decodeTimestamps = new();

  private int _displayIndex;
  private int _bidirectionalPicturesBetweenReferences;
  private MediaStreamInfo? _stream;

  private readonly record struct Pending(H263Frame Source, int DisplayIndex, long? PresentationTimestamp);

  private H263VideoEncoder(MediaStreamInfo stream, int sourceFormat) {
    this._requested = stream;
    this._width = stream.Width;
    this._height = stream.Height;
    this._sourceFormat = sourceFormat;
    this._macroblockWidth = (this._width + 15) / 16;
    this._macroblockHeight = (this._height + 15) / 16;
    this._reconstruction = H263VideoDecoder.Create(stream);
  }

  public static string CodecName => "H.263 / H.263+ (Annex O temporal B-pictures, Sorenson Spark)";

  public static CodecTag Codec => _Tag;

  /// <summary>
  /// Number of Annex O B-pictures placed between reference anchors. The default is zero.
  /// </summary>
  /// <remarks>
  /// Changing this after encoding begins would alter the interpretation of pictures already buffered,
  /// so the setting is intentionally immutable once the first input picture has been accepted.
  /// </remarks>
  public int BidirectionalPicturesBetweenReferences {
    get => this._bidirectionalPicturesBetweenReferences;
    set {
      if (this._displayIndex != 0)
        throw new InvalidOperationException("The H.263 B-picture cadence must be selected before the first picture is encoded.");
      if (value is < 0 or > _MAX_BETWEEN_REFERENCES)
        throw new ArgumentOutOfRangeException(nameof(value), value,
          $"The twelve-picture H.263 group permits zero through {_MAX_BETWEEN_REFERENCES} B-pictures between reference anchors.");
      this._bidirectionalPicturesBetweenReferences = value;
    }
  }

  public static H263VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("H.263 can only encode a video stream.");

    var sourceFormat = (stream.Width, stream.Height) switch {
      (128, 96) => 1,
      (176, 144) => 2,
      (352, 288) => 3,
      (704, 576) => 4,
      (1408, 1152) => 5,
      _ => 6,
    };

    if (sourceFormat == 6
        && (stream.Width is < 4 or > 2048
            || stream.Height is < 4 or > 1152
            || (stream.Width & 3) != 0
            || (stream.Height & 3) != 0))
      throw new NotSupportedException(
        $"H.263+ custom picture formats must be 4..2048 by 4..1152 pixels with both dimensions divisible by four; {stream.Width}x{stream.Height} was requested.");

    return new(stream, sourceFormat);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);

    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"This H.263 stream is {this._width}x{this._height}; a picture of {frame.Width}x{frame.Height} arrived.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var index = this._displayIndex++;
    var pending = new Pending(this._ToPlanes(frame), index, presentationTimestamp);
    this._decodeTimestamps.Enqueue(presentationTimestamp);

    if (this._reconstruction.CurrentReference == null || index % _GROUP_SIZE == 0) {
      this._DrainPendingAsPredicted();
      this._EncodeAnchor(pending, isIntra: true);
    } else {
      this._pending.Add(pending);
      if (this._pending.Count > this._bidirectionalPicturesBetweenReferences)
        this._EncodeGroup();
    }

    if (this._ready.TryDequeue(out packet))
      return true;

    packet = default;
    return false;
  }

  public IEnumerable<CodedPacket> Flush() {
    this._DrainPendingAsPredicted();
    while (this._ready.TryDequeue(out var ready))
      yield return ready;
  }

  public MediaStreamInfo DescribeStream() {
    if (this._stream != null)
      return this._stream;

    var header = new BitmapInfoHeader(
      HeaderSize: BitmapInfoHeader.StructSize,
      Width: this._width,
      Height: this._height,
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

    return this._stream = new() {
      Index = this._requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      Handler = _Tag,
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

  /// <summary>Encodes the next reference anchor, then the B-pictures that precede it in display order.</summary>
  private void _EncodeGroup() {
    var past = this._reconstruction.CurrentReference
      ?? throw new InvalidOperationException("An H.263 B-picture group needs a preceding reference anchor.");
    var anchor = this._pending[^1];

    this._EncodeAnchor(anchor, isIntra: false);
    var future = this._reconstruction.CurrentReference
      ?? throw new InvalidOperationException("The H.263 future reference anchor did not reconstruct.");

    for (var index = 0; index < this._pending.Count - 1; ++index) {
      var between = this._pending[index];
      var bytes = new H263BidirectionalPictureEncoder(
        this._width,
        this._height,
        this._sourceFormat,
        between.DisplayIndex % _TEMPORAL_REFERENCE_PERIOD,
        _QUANTISER,
        between.Source,
        past,
        future).Encode();

      this._ready.Enqueue(this._Packet(bytes, between.PresentationTimestamp, isKeyFrame: false));
    }

    this._pending.Clear();
  }

  private void _DrainPendingAsPredicted() {
    foreach (var pending in this._pending)
      this._EncodeAnchor(pending, isIntra: false);
    this._pending.Clear();
  }

  private void _EncodeAnchor(Pending pending, bool isIntra) {
    var reference = isIntra ? null : this._reconstruction.CurrentReference;
    byte[] bytes;

    if (this._sourceFormat == 6) {
      bytes = new H263PlusPictureEncoder(
        this._width,
        this._height,
        this._sourceFormat,
        pending.DisplayIndex % _TEMPORAL_REFERENCE_PERIOD,
        _QUANTISER,
        pending.Source,
        reference).Encode();
    } else {
      bytes = new H263PictureEncoder(
        this._sourceFormat,
        pending.DisplayIndex % _TEMPORAL_REFERENCE_PERIOD,
        _QUANTISER,
        pending.Source,
        reference).Encode();
    }

    var packet = this._Packet(bytes, pending.PresentationTimestamp, isIntra);
    this._ready.Enqueue(packet);

    // Decode the bytes just written, so every later picture predicts from exactly the samples a
    // receiving decoder holds rather than from the unquantised source handed to this encoder.
    this._reconstruction.TryDecode(packet, out _);
  }

  private CodedPacket _Packet(byte[] bytes, long? presentationTimestamp, bool isKeyFrame) {
    var decodeTimestamp = this._decodeTimestamps.Count == 0 ? null : this._decodeTimestamps.Dequeue();
    return new(
      this._requested.Index,
      bytes,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: decodeTimestamp,
      Duration: 1,
      IsKeyFrame: isKeyFrame);
  }

  private H263Frame _ToPlanes(RawImage frame) {
    var planes = RawYuvPlanes.Subsampled(frame, PixelFormat.Yuv420P8, this._width / 2, this._height / 2);
    var source = new H263Frame(this._macroblockWidth, this._macroblockHeight);
    var lumaSamples = this._width * this._height;
    var chromaWidth = this._width / 2;
    var chromaHeight = this._height / 2;
    var chromaSamples = chromaWidth * chromaHeight;

    _CopyAndExtend(planes, 0, this._width, this._height, source.Luma, source.LumaWidth, source.LumaHeight);
    _CopyAndExtend(planes, lumaSamples, chromaWidth, chromaHeight, source.Cb, source.ChromaWidth, source.ChromaHeight);
    _CopyAndExtend(planes, lumaSamples + chromaSamples, chromaWidth, chromaHeight, source.Cr, source.ChromaWidth, source.ChromaHeight);
    return source;
  }

  /// <summary>
  /// H.263 custom formats not divisible by sixteen are coded as the next complete macroblock grid and
  /// cropped only for display. The coded padding repeats the nearest real edge sample.
  /// </summary>
  private static void _CopyAndExtend(
    byte[] source,
    int sourceOffset,
    int sourceWidth,
    int sourceHeight,
    byte[] target,
    int targetWidth,
    int targetHeight) {
    for (var y = 0; y < sourceHeight; ++y) {
      var sourceRow = sourceOffset + y * sourceWidth;
      var targetRow = y * targetWidth;
      Array.Copy(source, sourceRow, target, targetRow, sourceWidth);

      var edge = target[targetRow + sourceWidth - 1];
      target.AsSpan(targetRow + sourceWidth, targetWidth - sourceWidth).Fill(edge);
    }

    var lastRow = (sourceHeight - 1) * targetWidth;
    for (var y = sourceHeight; y < targetHeight; ++y)
      target.AsSpan(lastRow, targetWidth).CopyTo(target.AsSpan(y * targetWidth, targetWidth));
  }
}
