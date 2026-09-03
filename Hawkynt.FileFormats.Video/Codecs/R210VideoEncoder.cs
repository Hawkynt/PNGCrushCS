using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes r210: 10-bit RGB with nothing compressed at all, one 32-bit big-endian word a pixel and
/// every row padded out to a whole 256 bytes.
/// </summary>
/// <remarks>
/// The mirror of <see cref="R210VideoDecoder"/>, and the layout is that decoder's own: red in the
/// word's high ten bits after two left zero, bits 20-29; green in the middle ten, 10-19; blue in the
/// low ten, 0-9; the word stored big-endian and a row padded with zero bytes out to a whole 256 — the
/// bit string ffmpeg's own <c>r210enc.c</c> writes, cross-checked against it. Nothing is predicted and
/// nothing is entropy coded, so every packet is a key frame and the stream this encoder describes is
/// decodable by that decoder with nothing more than the tag and the picture size.
/// <para/>
/// <b>What goes in.</b> <see cref="PixelFormat.Rgb30"/> — the format the decoder hands back — is the
/// sample-exact input: each ten-bit field is pulled out of that format's little-endian word, where red
/// sits low and blue high, and put into its own place in r210's, where the two trade ends — a
/// repacking and not a byte reversal, and nothing is rounded. The two alpha bits that format reserves
/// have no home in r210 and are dropped; the decoder hands them back fully opaque, which is what an
/// opaque source round-trips to exactly. <see cref="PixelFormat.Rgb24"/> is taken as well through the
/// package's own converter, which widens each eight-bit sample to ten by scaling and loses nothing —
/// scaling back down returns the byte that went in. Every other pixel format is refused by name: one
/// carrying an alpha channel has nowhere to put it here, and one narrower or wider than eight or ten
/// bits would be rounded on the way in.
/// <para/>
/// <b>What refuses.</b> A stream that is not video or has no pixels, a picture whose geometry differs
/// from the one the encoder was created for, a picture with too little pixel data for its own
/// declared size, and any pixel format not named above.
/// </remarks>
public sealed class R210VideoEncoder : IVideoCodecEncoder<R210VideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("r210");

  private readonly MediaStreamInfo _stream;
  private readonly int _stride;

  private R210VideoEncoder(MediaStreamInfo stream) {
    // A row is padded out to a whole 256 bytes.
    this._stride = (stream.Width * 4 + 255) / 256 * 256;
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

  public static string CodecName => "Uncompressed RGB 10-bit (r210)";

  public static CodecTag Codec => _Tag;

  public static R210VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video || stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"r210 encoding requires a video stream with positive dimensions; stream {stream.Index} states "
        + $"{stream.Kind} at {stream.Width}x{stream.Height}.");

    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException(
        $"r210 geometry is fixed at {this._stream.Width}x{this._stream.Height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var pixels = _ToRgb30(frame).PixelData.AsSpan();
    var width = this._stream.Width;
    var data = new byte[this._stride * this._stream.Height];

    for (var y = 0; y < this._stream.Height; ++y) {
      var source = pixels.Slice(y * width * 4, width * 4);
      var row = data.AsSpan(y * this._stride, width * 4);

      for (var x = 0; x < width; ++x) {
        // Rgb30's own layout: red in bits 0-9, green in 10-19, blue in 20-29, alpha in 30-31, which
        // r210 has no room for.
        var packed = BinaryPrimitives.ReadUInt32LittleEndian(source[(x * 4)..]);
        var red = packed & 0x3FF;
        var green = (packed >> 10) & 0x3FF;
        var blue = (packed >> 20) & 0x3FF;

        var word = (red << 20) | (green << 10) | blue;
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
      $"r210 takes {PixelFormat.Rgb30} samples as they are or {PixelFormat.Rgb24} widened to ten bits; a {frame.Format} "
      + "picture would lose something on the way in and is refused."),
  };
}
