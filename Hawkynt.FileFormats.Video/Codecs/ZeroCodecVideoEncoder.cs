using System;
using System.IO;
using System.IO.Compression;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes ZeroCodec (<c>ZECO</c>) as zlib-compressed bottom-up UYVY 4:2:2 I- and P-pictures.
/// </summary>
/// <remarks>
/// The first picture is an I-picture. Later pictures are P-pictures when the format can represent
/// their bytewise changes: an unchanged byte is written as zero and every changed byte is written
/// literally. Zero itself is the unchanged sentinel, so a byte changing from a nonzero value to zero
/// cannot be expressed by a P-picture. Such a picture is emitted as an I-picture instead, preserving
/// the source exactly rather than silently retaining the old byte.
/// <para/>
/// P-pictures reference only the immediately preceding decoded picture. There are no B-pictures,
/// future references, or reordering, so decode and presentation timestamps remain identical.
/// <para/>
/// Eight-bit planar 4:2:2 input is preserved sample for sample. Other source formats are converted
/// through the package's shared YUV conversion path before packing as UYVY. The coded rows are then
/// reversed to the bottom-up order used by the reference decoder and the resulting picture/delta is
/// stored as one complete zlib stream.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class ZeroCodecVideoEncoder : IVideoCodecEncoder<ZeroCodecVideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("ZECO");

  private readonly MediaStreamInfo _stream;
  private readonly PackedYuv422Packing _packing;
  private readonly int _stride;
  private readonly int _height;
  private readonly byte[] _previous;
  private bool _hasReference;

  private ZeroCodecVideoEncoder(MediaStreamInfo stream, PackedYuv422Packing packing) {
    this._packing = packing;
    this._stride = stream.Width * 2;
    this._height = stream.Height;
    this._previous = new byte[packing.FrameBytes];
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
      BitsPerPixel = 16,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "ZeroCodec";

  public static CodecTag Codec => _Tag;

  public static ZeroCodecVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("ZeroCodec can only encode a video stream.");

    return new(stream, PackedYuv422Packing.For(stream, PackedYuv422Order.CbLumaCrLuma, "ZeroCodec"));
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException(
        $"ZeroCodec geometry is fixed at {this._stream.Width}x{this._stream.Height}; received "
        + $"{frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        "The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var topDown = this._packing.Pack(this._packing.PlanesOf(frame));
    var current = this._FlipRows(topDown);
    var isKeyFrame = !this._hasReference || _RequiresKeyFrame(current, this._previous);
    var coded = isKeyFrame ? current : _Predict(current, this._previous);
    var compressed = _Compress(coded);

    current.CopyTo(this._previous, 0);
    this._hasReference = true;

    packet = new(
      this._stream.Index,
      compressed,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: isKeyFrame);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  private static bool _RequiresKeyFrame(ReadOnlySpan<byte> current, ReadOnlySpan<byte> previous) {
    for (var i = 0; i < current.Length; ++i)
      if (current[i] == 0 && previous[i] != 0)
        return true;

    return false;
  }

  private static byte[] _Predict(ReadOnlySpan<byte> current, ReadOnlySpan<byte> previous) {
    var result = new byte[current.Length];
    for (var i = 0; i < result.Length; ++i)
      result[i] = current[i] == previous[i] ? (byte)0 : current[i];

    return result;
  }

  private static byte[] _Compress(ReadOnlySpan<byte> data) {
    using var target = new MemoryStream();
    using (var zlib = new ZLibStream(target, CompressionLevel.Optimal, leaveOpen: true))
      zlib.Write(data);

    return target.ToArray();
  }

  private byte[] _FlipRows(ReadOnlySpan<byte> topDown) {
    var result = new byte[topDown.Length];
    for (var row = 0; row < this._height; ++row)
      topDown.Slice(row * this._stride, this._stride)
        .CopyTo(result.AsSpan((this._height - 1 - row) * this._stride, this._stride));

    return result;
  }
}
