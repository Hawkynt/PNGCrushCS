using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes r10k: AJA Kona 10-bit RGB with nothing compressed at all, one 32-bit big-endian word a
/// pixel and no row padding.
/// </summary>
/// <remarks>
/// The mirror of <see cref="R10kVideoDecoder"/>, and the layout is that decoder's own: red in the
/// word's high ten bits, 22-31; green in the middle ten, 12-21; blue in bits 2-11; the low two bits
/// left zero; the word stored big-endian; and a row exactly <c>width</c> times four bytes with no
/// padding at all — the two things that set it apart from r210. Nothing is predicted and nothing is
/// entropy coded, so every packet is a key frame and the stream this encoder describes is decodable
/// by that decoder with nothing more than the tag and the picture size.
/// <para/>
/// <b>What goes in.</b> <see cref="PixelFormat.Rgb30"/> — the format the decoder hands back — is the
/// sample-exact input: each ten-bit field is pulled out of that format's little-endian word and put
/// into its own place in r10k's, a repacking and not a byte reversal, and nothing is rounded. The two
/// alpha bits that format reserves have no home in r10k and are dropped; the decoder hands them back
/// fully opaque, which is what an opaque source round-trips to exactly. <see cref="PixelFormat.Rgb24"/>
/// is taken as well through the package's own converter, which widens each eight-bit sample to ten
/// by scaling and loses nothing — scaling back down returns the byte that went in. Every other pixel
/// format is refused by name: one carrying an alpha channel has nowhere to put it here, and one
/// narrower or wider than eight or ten bits would be rounded on the way in.
/// <para/>
/// <b>What refuses.</b> A stream that is not video or has no pixels, a picture whose geometry differs
/// from the one the encoder was created for, a picture with too little pixel data for its own
/// declared size, and any pixel format not named above.
/// </remarks>
public sealed class R10kVideoEncoder : IVideoCodecEncoder<R10kVideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("R10k");

  private readonly MediaStreamInfo _stream;
  private readonly int _stride;

  private R10kVideoEncoder(MediaStreamInfo stream) {
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

  public static string CodecName => "AJA Kona 10-bit RGB (r10k)";

  public static CodecTag Codec => _Tag;

  public static R10kVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video || stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"r10k encoding requires a video stream with positive dimensions; stream {stream.Index} states "
        + $"{stream.Kind} at {stream.Width}x{stream.Height}.");

    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException(
        $"r10k geometry is fixed at {this._stream.Width}x{this._stream.Height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var pixels = _ToRgb30(frame).PixelData.AsSpan();
    var width = this._stream.Width;
    var data = new byte[this._stride * this._stream.Height];

    for (var y = 0; y < this._stream.Height; ++y) {
      var source = pixels.Slice(y * width * 4, width * 4);
      var row = data.AsSpan(y * this._stride, this._stride);

      for (var x = 0; x < width; ++x) {
        // Rgb30's own layout: red in bits 0-9, green in 10-19, blue in 20-29, alpha in 30-31, which
        // r10k has no room for.
        var packed = BinaryPrimitives.ReadUInt32LittleEndian(source[(x * 4)..]);
        var red = packed & 0x3FF;
        var green = (packed >> 10) & 0x3FF;
        var blue = (packed >> 20) & 0x3FF;

        var word = (red << 22) | (green << 12) | (blue << 2);
        BinaryPrimitives.WriteUInt32BigEndian(row[(x * 4)..], word);
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

  private static RawImage _ToRgb30(RawImage frame) => frame.Format switch {
    PixelFormat.Rgb30 => frame,
    PixelFormat.Rgb24 => FastRawImageConverter.Convert(frame, PixelFormat.Rgb30),
    _ => throw new NotSupportedException(
      $"r10k takes {PixelFormat.Rgb30} samples as they are or {PixelFormat.Rgb24} widened to ten bits; a {frame.Format} "
      + "picture would lose something on the way in and is refused."),
  };
}
