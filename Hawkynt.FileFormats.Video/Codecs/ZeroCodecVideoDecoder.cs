using System;
using System.IO;
using System.IO.Compression;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes ZeroCodec (<c>ZECO</c>): zlib-compressed bottom-up UYVY 4:2:2 pictures with a single
/// previous-picture predictor for inter frames.
/// </summary>
/// <remarks>
/// ZeroCodec has two picture types and no in-band frame header. The container's key-frame flag is the
/// discriminator: an I-picture inflates to the literal packed picture; a P-picture inflates to the
/// same number of bytes, but a zero byte means "copy this byte from the immediately preceding decoded
/// picture" while a nonzero byte is literal. A P-picture without that preceding picture is therefore
/// invalid. There are no B-pictures, forward references, or display/decode reordering.
/// <para/>
/// FFmpeg's ZeroCodec decoder is the reference for the coded shape: it marks flagged packets as I
/// pictures and the rest as P pictures, rejects an inter picture with no previous frame, walks coded
/// rows from the bottom upwards, and fixes the pixel format at packed UYVY 4:2:2. Its decoder is
/// permissively licensed; this implementation independently expresses those bitstream rules in the
/// package's own packet and packed-YUV abstractions rather than translating its source structure.
/// Container bit-depth metadata is deliberately not used to select a coded layout: the reference
/// decoder fixes UYVY itself, while the original VFW codec accepted several application-facing input
/// and output formats.
/// <para/>
/// The decompressed picture is returned as <see cref="PixelFormat.Yuv422P8"/> so the samples remain
/// lossless. Colour-space conversion is a caller decision; the codec itself carries no matrix that
/// would justify choosing one here.
/// <para/>
/// <b>What refuses.</b> A picture with no pixels, an odd width, an inter picture before a reference
/// exists, and a packet whose zlib stream is corrupt or does not inflate to exactly one picture.
/// </remarks>
public sealed class ZeroCodecVideoDecoder : IVideoCodecDecoder<ZeroCodecVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("ZECO");

  private readonly PackedYuv422Packing _packing;
  private readonly int _streamIndex;
  private readonly int _stride;
  private readonly int _height;
  private readonly byte[] _previous;
  private bool _hasReference;

  private ZeroCodecVideoDecoder(MediaStreamInfo stream, PackedYuv422Packing packing) {
    this._packing = packing;
    this._streamIndex = stream.Index;
    this._stride = stream.Width * 2;
    this._height = stream.Height;
    this._previous = new byte[packing.FrameBytes];
  }

  public static string CodecName => "ZeroCodec";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static ZeroCodecVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("ZeroCodec can only decode a video stream.");

    return new(stream, PackedYuv422Packing.For(stream, PackedYuv422Order.CbLumaCrLuma, "ZeroCodec"));
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    if (!packet.IsKeyFrame && !this._hasReference)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries a ZeroCodec P-picture before any reference picture exists.");

    var coded = this._Inflate(packet.Data);

    if (!packet.IsKeyFrame)
      for (var i = 0; i < coded.Length; ++i)
        if (coded[i] == 0)
          coded[i] = this._previous[i];

    coded.CopyTo(this._previous, 0);
    this._hasReference = true;

    var topDown = this._FlipRows(coded);
    frame = this._packing.ToImage(this._packing.Unpack(topDown));
    return true;
  }

  private byte[] _Inflate(ReadOnlyMemory<byte> data) {
    var expected = this._packing.FrameBytes;
    var decoded = new byte[expected];

    try {
      using var source = new MemoryStream(data.ToArray(), writable: false);
      using var zlib = new ZLibStream(source, CompressionMode.Decompress);
      zlib.ReadExactly(decoded);
      if (zlib.ReadByte() >= 0)
        throw new InvalidDataException("The zlib stream expands past the declared picture size.");
    } catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException) {
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries a ZeroCodec packet whose zlib stream does not inflate to "
        + $"exactly the {expected} byte(s) its picture needs.", ex);
    }

    return decoded;
  }

  /// <summary>ZeroCodec writes Windows-style bottom-up rows; <see cref="PackedYuv422Packing"/> uses
  /// display order, so reverse whole rows and leave each UYVY macropixel untouched.</summary>
  private byte[] _FlipRows(ReadOnlySpan<byte> bottomUp) {
    var result = new byte[bottomUp.Length];
    for (var row = 0; row < this._height; ++row)
      bottomUp.Slice(row * this._stride, this._stride)
        .CopyTo(result.AsSpan((this._height - 1 - row) * this._stride, this._stride));

    return result;
  }
}
