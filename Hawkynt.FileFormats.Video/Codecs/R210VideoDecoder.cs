using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes r210: 10-bit RGB with nothing compressed at all, one 32-bit big-endian word a pixel.
/// </summary>
/// <remarks>
/// A packing rule and not a codec in the sense most of this package's other entries are — there is
/// no prediction, no entropy coding and no reference to a frame before it. Each pixel is one 32-bit
/// word, stored big-endian, laid out red in the high ten bits after two unused ones — bits 20-29 —
/// green in the middle ten, 10-19, and blue in the low ten, 0-9, which is the bit string
/// MultimediaWiki's own page for the format states and the one ffmpeg's encoder writes: fed pure
/// red it produces <c>3F A0 00 00</c>, fed pure blue <c>00 20 03 FC</c>. A row is padded out to a
/// whole 256 bytes, which several vendors' 10-bit RGB formats share.
/// <para/>
/// <b>Decoded straight into <see cref="PixelFormat.Rgb30"/> and nothing is lost doing it.</b> That
/// format's own ten-bit layout — red in bits 0-9, green in 10-19, blue in 20-29, little-endian — is
/// r210's word with red and blue in each other's place, so this is a repacking and not a plain byte
/// reversal: each component is pulled out of its own position in the big-endian word and written back
/// into <see cref="PixelFormat.Rgb30"/>'s. An earlier reading of this decoder took the byte reversal as
/// enough, because it compared against ffmpeg's <c>x2rgb10le</c> — a format that keeps red in the
/// high ten bits like r210 itself — and mistook that for <see cref="PixelFormat.Rgb30"/>'s arrangement;
/// it handed every picture back with red and blue swapped. There is no eight-bit reduction and no
/// display convention standing between the coded samples and what a caller receives; the two padding
/// bits this format leaves unused become the alpha field <see cref="PixelFormat.Rgb30"/> reserves,
/// set to fully opaque, which is what ffmpeg's own decoder writes into them.
/// <para/>
/// <b>Verified exactly.</b> Two geometries and twenty frames of pseudo-random ten-bit samples — 64x40,
/// a whole number of 256-byte rows, and 33x25, which needs the padding — packed by this package's
/// own r210 encoder and decoded by ffmpeg's, compared plane for plane against the <c>gbrp10le</c>
/// samples that went in: identical on every one, because a fixed packing with nothing adaptive in it
/// has nothing left to get wrong once the bit ranges are right. Pure red, green and blue out of
/// ffmpeg's own encoder decode here to the same three primaries, which is what rules the swap out.
/// <para/>
/// <b>What refuses.</b> A picture with no pixels, and a packet shorter than its padded stride times
/// its height.
/// </remarks>
public sealed class R210VideoDecoder : IVideoCodecDecoder<R210VideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("r210");

  private readonly int _width;
  private readonly int _height;
  private readonly int _streamIndex;
  private readonly int _stride;

  private R210VideoDecoder(int width, int height, int streamIndex) {
    this._width = width;
    this._height = height;
    this._streamIndex = streamIndex;
    // A row is padded out to a whole 256 bytes.
    this._stride = (width * 4 + 255) / 256 * 256;
  }

  public static string CodecName => "Uncompressed RGB 10-bit (r210)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static R210VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can "
        + "be decoded into.");

    return new(stream.Width, stream.Height, stream.Index);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;
    var expected = (long)this._stride * this._height;
    if (data.Length < expected)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an r210 packet of {data.Length} byte(s), where a "
        + $"{this._width}x{this._height} frame at a stride of {this._stride} needs {expected}.");

    var pixels = new byte[this._width * this._height * 4];

    for (var y = 0; y < this._height; ++y) {
      var row = data.Slice(y * this._stride, this._width * 4);
      var target = pixels.AsSpan(y * this._width * 4);

      for (var x = 0; x < this._width; ++x) {
        var word = BinaryPrimitives.ReadUInt32BigEndian(row[(x * 4)..]);
        var red = (word >> 20) & 0x3FF;
        var green = (word >> 10) & 0x3FF;
        var blue = word & 0x3FF;

        // Rgb30's own layout: red in bits 0-9, green in 10-19, blue in 20-29, alpha in 30-31 — set
        // fully opaque, which is what ffmpeg's own decoder writes into the two bits r210 leaves spare.
        var packed = red | (green << 10) | (blue << 20) | 0xC0000000u;
        BinaryPrimitives.WriteUInt32LittleEndian(target[(x * 4)..], packed);
      }
    }

    frame = new() { Width = this._width, Height = this._height, Format = PixelFormat.Rgb30, PixelData = pixels };
    return true;
  }
}
