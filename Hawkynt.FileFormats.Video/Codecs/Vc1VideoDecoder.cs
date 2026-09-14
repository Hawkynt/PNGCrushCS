using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.Vc1;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Decodes VC-1 / Windows Media Video 9 progressive Simple and Main profile pictures.</summary>
/// <remarks>
/// I and BI pictures use the complete intra block path. P pictures additionally support 1-MV zero-differential
/// prediction with transformed residuals, and B pictures support direct prediction from both anchor pictures with
/// transformed residuals. Anchor pictures are retained in coded order and released in display order, so a coded
/// <c>I, P, B</c> sequence is displayed as <c>I, B, P</c> as SMPTE 421M requires.
/// <para/>
/// Advanced profile remains a separate bitstream shape and is refused by name. Predictive tools that do not yet have a
/// reconstruction path (non-zero motion, mixed/4-MV, intensity compensation, compressed bitplanes, differential
/// quantisation and variable inter transforms) are likewise refused rather than approximated.
/// </remarks>
public sealed class Vc1VideoDecoder : IVideoCodecDecoder<Vc1VideoDecoder> {

  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("WMV3"),
    CodecTag.FromCharacters("WMV9"),
  ];

  private static readonly CodecTag[] _AdvancedTags = [
    CodecTag.FromCharacters("WVC1"),
    CodecTag.FromCharacters("WMVA"),
    CodecTag.FromCharacters("VC-1"),
  ];

  private readonly Vc1SequenceHeader _sequence;
  private readonly int _width;
  private readonly int _height;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly Vc1PictureDecoder _intraPictures;
  private readonly Vc1PredictivePictureDecoder _predictivePictures;
  private readonly Queue<RawImage> _ready = [];
  private Vc1Frame? _pastAnchor;
  private Vc1Frame? _futureAnchor;

  private Vc1VideoDecoder(Vc1SequenceHeader sequence, int width, int height) {
    this._sequence = sequence;
    this._width = width;
    this._height = height;
    this._macroblockWidth = (width + 15) / 16;
    this._macroblockHeight = (height + 15) / 16;
    this._intraPictures = new(sequence, this._macroblockWidth, this._macroblockHeight);
    this._predictivePictures = new(sequence, this._macroblockWidth, this._macroblockHeight);
  }

  public static string CodecName => "VC-1 / Windows Media Video 9 (SMPTE 421M, progressive Simple/Main profile)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      return false;

    return _Matches(stream.Codec, _Tags) || _Matches(stream.Codec, _AdvancedTags)
           || stream.CodecId is "V_MS/VFW/FOURCC/WMV3" or "V_VC1";
  }

  public static Vc1VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (_Matches(stream.Codec, _AdvancedTags) || stream.CodecId == "V_VC1")
      throw new NotSupportedException(
        $"Stream {stream.Index} is VC-1 Advanced profile ({stream.Codec}), which states its sequence header and entry "
        + "point inside the bitstream rather than in the container. The progressive Simple and Main profiles are read here.");

    Vc1SequenceHeader sequence;
    try {
      sequence = Vc1SequenceHeader.ReadFrom(_SequenceHeaderBytes(stream.CodecPrivateData.Span));
    } catch (InvalidDataException e) {
      throw new NotSupportedException(
        $"Stream {stream.Index} is coded as '{stream.Codec}', but its codec private data is not a Simple or Main profile "
        + $"sequence header: {e.Message} Windows Media Video states that header only in the container, so a stream without "
        + "one cannot be decoded.", e);
    }

    if (sequence.Profile == Vc1Profile.Advanced)
      throw new NotSupportedException($"Stream {stream.Index} states the Advanced profile in its sequence header, which is not read here.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"Stream {stream.Index} states a size of {stream.Width}x{stream.Height}. Simple and Main profile VC-1 carries no "
        + "picture size in the bitstream, so the container's is the only one there is.");
    if (sequence.MultiResolution)
      throw new NotSupportedException(
        $"Stream {stream.Index} is coded with multi-resolution coding (MULTIRES), whose pictures are decoded at half size and upsampled for display.");
    if (sequence.RangeReduction)
      throw new NotSupportedException(
        $"Stream {stream.Index} is coded with range reduction (RANGERED), which scales reconstructed samples after decoding.");
    if (sequence.LoopFilter)
      throw new NotSupportedException(
        $"Stream {stream.Index} is coded with the in-loop deblocking filter (LOOPFILTER), which is part of reference reconstruction and cannot be omitted.");

    return new(sequence, stream.Width, stream.Height);
  }

  private const int _BITMAP_INFO_HEADER_SIZE = 40;

  private static ReadOnlySpan<byte> _SequenceHeaderBytes(ReadOnlySpan<byte> privateData) {
    if (privateData.Length <= _BITMAP_INFO_HEADER_SIZE)
      return privateData;

    var declared = BinaryPrimitives.ReadUInt32LittleEndian(privateData);
    return declared is >= _BITMAP_INFO_HEADER_SIZE and <= 0xFFFF
      ? privateData[_BITMAP_INFO_HEADER_SIZE..]
      : privateData;
  }

  private static bool _Matches(CodecTag codec, CodecTag[] tags) {
    foreach (var tag in tags)
      if (codec.EqualsIgnoringCase(tag))
        return true;
    return false;
  }

  /// <summary>Consumes one coded picture and returns the next picture that is due for display, if one is ready.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;

    if (data.Length <= 1) {
      this._DecodeSkippedAnchor();
      return this._TakeReady(out frame);
    }

    var peek = new Vc1BitReader(data);
    var header = Vc1PictureHeader.ReadFrom(ref peek, this._sequence);

    switch (header.PictureType) {
      case Vc1PictureType.Intra:
        this._DecodeAnchorIntra(data);
        break;
      case Vc1PictureType.Predicted:
        this._DecodeAnchorPredicted(data);
        break;
      case Vc1PictureType.Bidirectional:
        this._DecodeB(data);
        break;
      case Vc1PictureType.BidirectionalIntra:
        this._Enqueue(this._DecodeIntra(data));
        break;
      default:
        throw new InvalidDataException($"Unexpected VC-1 picture type {header.PictureType}.");
    }

    return this._TakeReady(out frame);
  }

  private void _DecodeAnchorIntra(ReadOnlySpan<byte> data) {
    var decoded = this._DecodeIntra(data);
    if (this._sequence.MaxBFrames == 0) {
      this._pastAnchor = decoded;
      this._Enqueue(decoded);
      return;
    }

    if (this._pastAnchor == null && this._futureAnchor == null) {
      this._pastAnchor = decoded;
      this._Enqueue(decoded);
      return;
    }

    this._PromoteFutureAnchor();
    this._futureAnchor = decoded;
  }

  private void _DecodeAnchorPredicted(ReadOnlySpan<byte> data) {
    if (this._sequence.MaxBFrames == 0) {
      var reference = this._pastAnchor
                      ?? throw new InvalidDataException("A VC-1 P picture appears before any anchor picture it can reference.");
      var decoded = this._predictivePictures.DecodePredicted(data, reference, out _);
      this._pastAnchor = decoded;
      this._Enqueue(decoded);
      return;
    }

    this._PromoteFutureAnchor();
    var past = this._pastAnchor
               ?? throw new InvalidDataException("A VC-1 P picture appears before any anchor picture it can reference.");
    this._futureAnchor = this._predictivePictures.DecodePredicted(data, past, out _);
  }

  private void _DecodeB(ReadOnlySpan<byte> data) {
    if (this._sequence.MaxBFrames == 0)
      throw new InvalidDataException("A VC-1 B picture is present although MAXBFRAMES is zero.");

    var past = this._pastAnchor
               ?? throw new InvalidDataException("A VC-1 B picture has no temporally previous anchor picture.");
    var future = this._futureAnchor
                 ?? throw new InvalidDataException("A VC-1 B picture has no already-decoded subsequent anchor picture.");
    this._Enqueue(this._predictivePictures.DecodeBidirectional(data, past, future, out _));
  }

  private void _DecodeSkippedAnchor() {
    if (this._sequence.MaxBFrames == 0) {
      var reference = this._pastAnchor;
      if (reference == null)
        return;
      var repeated = _Clone(reference);
      this._pastAnchor = repeated;
      this._Enqueue(repeated);
      return;
    }

    this._PromoteFutureAnchor();
    if (this._pastAnchor != null)
      this._futureAnchor = _Clone(this._pastAnchor);
  }

  private Vc1Frame _DecodeIntra(ReadOnlySpan<byte> data) {
    var reader = new Vc1BitReader(data);
    var header = Vc1PictureHeader.ReadFrom(ref reader, this._sequence);
    var result = new Vc1Frame(this._macroblockWidth, this._macroblockHeight);
    this._intraPictures.DecodeIntra(ref reader, header, result);
    return result;
  }

  private void _PromoteFutureAnchor() {
    if (this._futureAnchor == null)
      return;

    this._Enqueue(this._futureAnchor);
    this._pastAnchor = this._futureAnchor;
    this._futureAnchor = null;
  }

  private void _Enqueue(Vc1Frame picture) {
    this._ready.Enqueue(new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Rgb24,
      PixelData = Vc1ColorConversion.ToRgb24(picture, this._width, this._height),
    });
  }

  private bool _TakeReady(out RawImage frame) {
    if (this._ready.Count == 0) {
      frame = null!;
      return false;
    }

    frame = this._ready.Dequeue();
    return true;
  }

  private static Vc1Frame _Clone(Vc1Frame source) {
    var result = new Vc1Frame(source.LumaWidth / 16, source.LumaHeight / 16);
    source.Luma.CopyTo(result.Luma, 0);
    source.Cb.CopyTo(result.Cb, 0);
    source.Cr.CopyTo(result.Cr, 0);
    return result;
  }

  /// <summary>Releases any delayed anchor picture after the final B picture has been consumed.</summary>
  public IEnumerable<RawImage> Flush() {
    this._PromoteFutureAnchor();
    while (this._ready.Count != 0)
      yield return this._ready.Dequeue();
  }
}
