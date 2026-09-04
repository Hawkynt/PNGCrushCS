using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes avrp: Avid's 1:1 10-bit RGB packer, one 32-bit little-endian word a pixel and every row
/// padded out to a whole sixty-four-pixel block.
/// </summary>
/// <remarks>
/// The mirror of <see cref="AvrpVideoDecoder"/>, and the layout is that decoder's own: red in bits
/// 22-31 of the word, green in 12-21, blue in 2-11, the low two bits left zero, the word stored
/// little-endian, and a row exactly <c>width</c> rounded up to the next multiple of sixty-four
/// pixels times four bytes. Nothing is predicted and nothing is entropy coded, so every packet is a
/// key frame and the stream this encoder describes is decodable by that decoder with nothing more
/// than the tag and the picture size.
/// <para/>
/// <b>The padding columns are written as zero.</b> They are not part of the picture and no decoder
/// reads them — this package's own throws them away and so does the reference — but they are in the
/// packet whatever is put there, and zero is the one value that says nothing.
/// <para/>
/// <b>What goes in.</b> <see cref="PixelFormat.Rgb30"/> — the format the decoder hands back — is the
/// sample-exact input: each ten-bit field is pulled out of that format's little-endian word, where
/// red sits low, and put into its own place here, where red sits high. The two alpha bits that
/// format reserves have no home in avrp and are dropped; the decoder hands them back fully opaque,
/// which is what an opaque source round-trips to exactly. <see cref="PixelFormat.Rgb24"/> is taken as
/// well through the package's own converter, which widens each eight-bit sample to ten by scaling and
/// loses nothing — scaling back down returns the byte that went in. Every other pixel format is
/// refused by name: one carrying an alpha channel has nowhere to put it here, and one narrower or
/// wider than eight or ten bits would be rounded on the way in.
/// <para/>
/// <b>Verified against ffmpeg's own encoder, byte for byte.</b> This is one of the few codecs here
/// whose reference has an encoder as well as a decoder, so the comparison is the strongest kind
/// available: the same <c>gbrp10le</c> planes were carried through ffmpeg's <c>avrp</c> encoder and
/// through this one at 8x2, 33x25, 64x40 and 100x30 — a width under one block, one that pads by more
/// than half a block, exactly one block, and more than one — and the two packets are identical on
/// every byte, padding columns included.
/// <para/>
/// <b>What refuses.</b> A stream that is not video or has no pixels, a picture whose geometry differs
/// from the one the encoder was created for, a picture with too little pixel data for its own
/// declared size, and any pixel format not named above.
/// </remarks>
public sealed class AvrpVideoEncoder : IVideoCodecEncoder<AvrpVideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("AVrp");

  private readonly MediaStreamInfo _stream;
  private readonly int _width;
  private readonly int _height;
  private readonly int _stride;

  private AvrpVideoEncoder(MediaStreamInfo stream) {
    this._width = stream.Width;
    this._height = stream.Height;
    // A row is the width rounded up to a whole sixty-four-pixel block, four bytes a pixel.
    this._stride = (stream.Width + 63) / 64 * 64 * 4;
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
      BitsPerPixel = 32,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "Avid 1:1 10-bit RGB Packer (avrp)";

  public static CodecTag Codec => _Tag;

  public static AvrpVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("avrp can only encode a video stream.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can "
        + "be coded from.");

    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"avrp geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var pixels = _ToRgb30(frame).PixelData.AsSpan();
    var data = new byte[checked(this._stride * this._height)];

    for (var y = 0; y < this._height; ++y) {
      var source = pixels.Slice(y * this._width * 4, this._width * 4);
      var row = data.AsSpan(y * this._stride, this._width * 4);

      for (var x = 0; x < this._width; ++x) {
        // Rgb30's own layout: red in bits 0-9, green in 10-19, blue in 20-29, alpha in 30-31, which
        // avrp has no room for.
        var packed = BinaryPrimitives.ReadUInt32LittleEndian(source[(x * 4)..]);
        var red = packed & 0x3FF;
        var green = (packed >> 10) & 0x3FF;
        var blue = (packed >> 20) & 0x3FF;

        var word = (red << 22) | (green << 12) | (blue << 2);
        BinaryPrimitives.WriteUInt32LittleEndian(row[(x * 4)..], word);
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
      $"avrp takes {PixelFormat.Rgb30} samples as they are or {PixelFormat.Rgb24} widened to ten bits; a {frame.Format} "
      + "picture would lose something on the way in and is refused."),
  };
}
