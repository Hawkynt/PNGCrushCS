using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Avid Meridien Uncompressed (AVUI): SD UYVY 4:2:2 with Avid's run-blanking and field layout,
/// optionally followed by its inverted alpha companion.
/// </summary>
/// <remarks>
/// The packet and private-data behaviour is implemented independently from the observable layout used
/// by FFmpeg's LGPL-2.1-or-later AVUI encoder/decoder and Avid's published codec guidance. New streams
/// default progressive because <see cref="MediaStreamInfo"/> has no field-order property; handing
/// <see cref="Create"/> an AVUI sample entry whose APRG atom marks interlace preserves that mode.
/// <para/>
/// At depth 16, transparent source pixels are refused rather than silently discarded. Request depth
/// 32 to write alpha; AVUI stores it inverted, in a second field-laid-out plane where every other byte
/// is significant. Every picture is a key frame because the format has no prediction.
/// <para/>
/// Measured against ffmpeg 9.0.1 on pseudo-random pictures at both geometries and in both field modes:
/// this encoder writes byte for byte what ffmpeg's own avui encoder writes, on all 3,133,456 bytes of
/// the four packets. Ffmpeg has no alpha-bearing avui encoder to compare against, so the depth-32
/// companion was measured the other way about — ffmpeg's decoder reads all 1,529,280 alpha samples of
/// the same four cases back exactly as they were handed in.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class AvuiVideoEncoder : IVideoCodecEncoder<AvuiVideoEncoder> {

  private static readonly CodecTag _AVUI = CodecTag.FromCharacters("AVUI");

  private readonly MediaStreamInfo _requested;
  private readonly AvuiVideoFormat _layout;
  private readonly PackedYuv422Packing _packing;
  private readonly int _depth;
  private MediaStreamInfo? _stream;

  private AvuiVideoEncoder(MediaStreamInfo stream, AvuiVideoFormat layout, int depth) {
    this._requested = stream;
    this._layout = layout;
    this._depth = depth;
    this._packing = PackedYuv422Packing.For(stream, PackedYuv422Order.CbLumaCrLuma, CodecName);
  }

  public static string CodecName => "Avid Meridien Uncompressed";
  public static CodecTag Codec => _AVUI;

  public static AvuiVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Avid Meridien Uncompressed can only encode a video stream.");
    if (stream.Codec != CodecTag.None && !stream.Codec.EqualsIgnoringCase(_AVUI))
      throw new NotSupportedException($"'{stream.Codec}' is not Avid Meridien Uncompressed ('AVUI').");

    var layout = AvuiVideoFormat.For(stream, defaultInterlaced: false);
    var describedDepth = AvuiVideoFormat.SampleDepth(stream.CodecPrivateData.Span);
    var depth = stream.BitsPerPixel != 0 ? stream.BitsPerPixel : describedDepth;
    if (depth == 0)
      depth = AvuiVideoFormat.OpaqueDepth;
    if (depth is not (AvuiVideoFormat.OpaqueDepth or AvuiVideoFormat.AlphaDepth))
      throw new NotSupportedException(
        $"Avid Meridien Uncompressed is written at 16 bits for UYVY or 32 bits when its alpha companion is present; "
        + $"the stream asks for {depth} bits per pixel.");

    return new(stream, layout, depth);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != AvuiVideoFormat.Width || frame.Height != this._layout.Height)
      throw new InvalidDataException(
        $"This Avid Meridien stream is {AvuiVideoFormat.Width}x{this._layout.Height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var alpha = this._depth == AvuiVideoFormat.AlphaDepth ? _Alpha(frame) : null;
    if (alpha == null && this._depth == AvuiVideoFormat.OpaqueDepth && _HasTransparency(frame))
      throw new NotSupportedException(
        "This AVUI stream was described at 16 bits, so it has no alpha companion. Request BitsPerPixel = 32 to encode transparent pixels.");

    var uyvy = this._packing.Pack(this._packing.PlanesOf(frame));
    var data = new byte[alpha == null ? this._layout.OpaquePacketLength : this._layout.AlphaPacketLength];

    this._WriteFields(uyvy, data, 0);
    if (alpha != null)
      this._WriteFields(alpha, data, this._layout.OpaqueLength + 5);

    packet = new(
      this._requested.Index,
      data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream ??= new() {
    Index = this._requested.Index,
    Kind = MediaStreamKind.Video,
    Codec = _AVUI,
    Handler = _AVUI,
    TimeBase = this._requested.TimeBase,
    FrameRate = this._requested.FrameRate,
    DeclaredFrameCount = this._requested.DeclaredFrameCount,
    Width = AvuiVideoFormat.Width,
    Height = this._layout.Height,
    BitsPerPixel = this._depth,
    CodecPrivateData = this._layout.SampleEntry(this._depth),
    Language = this._requested.Language,
    Name = this._requested.Name,
  };

  /// <summary>
  /// Writes rows through the AVUI field grid. For colour <paramref name="source"/> is packed UYVY;
  /// for alpha it is already laid out as two bytes per pixel with the inverted value first, which is
  /// the same row stride.
  /// </summary>
  private void _WriteFields(ReadOnlySpan<byte> source, Span<byte> destination, int baseOffset) {
    const int sourceRowBytes = AvuiVideoFormat.Width * 2;

    for (var field = 0; field < this._layout.Fields; ++field) {
      var destinationOffset = checked(baseOffset + this._layout.FieldOffset(field));
      for (var row = this._layout.FirstRow(field); row < this._layout.Height; row += this._layout.RowStep) {
        // A packet ends on the last byte a reference decoder reads, and for the alpha companion that
        // is the significant byte of its final pixel pair: the unused byte behind it falls one past
        // the packet. Colour rows always fit whole, so only that one byte is ever left out.
        var count = Math.Min(sourceRowBytes, destination.Length - destinationOffset);
        source.Slice(checked(row * sourceRowBytes), count).CopyTo(destination[destinationOffset..]);
        destinationOffset += sourceRowBytes;
      }
    }
  }

  /// <summary>Builds AVUI's two-byte-per-pixel inverted-alpha storage, leaving the unused bytes zero.</summary>
  private static byte[] _Alpha(RawImage frame) {
    var result = new byte[checked(frame.Width * frame.Height * 2)];
    if (!frame.HasAlpha)
      return result; // inverted 255 alpha is zero

    var rgba = frame.ToRgba32();
    for (var i = 0; i < frame.Width * frame.Height; ++i)
      result[i * 2] = (byte)(byte.MaxValue - rgba[i * 4 + 3]);

    return result;
  }

  private static bool _HasTransparency(RawImage frame) {
    if (!frame.HasAlpha)
      return false;

    var rgba = frame.ToRgba32();
    for (var i = 3; i < rgba.Length; i += 4)
      if (rgba[i] != byte.MaxValue)
        return true;

    return false;
  }
}
