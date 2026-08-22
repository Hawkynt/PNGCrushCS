using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes avrp: 10-bit RGB with nothing compressed at all, one 32-bit little-endian word a pixel and
/// a row padded out to a sixty-four-pixel block.
/// </summary>
/// <remarks>
/// Avid's own "1:1" RGB packer, and a close relative of r10k rather than the same word under a second
/// byte order — the two share exactly the same bit arrangement, red in the high ten bits, green in the
/// middle ten and blue in the next ten down with the low two left spare, but r10k's word is big-endian
/// and this one is little-endian, and r10k pads no row at all where this one pads every row out to a
/// whole number of sixty-four-pixel blocks. Neither the word nor the padding is written down anywhere
/// this project found; both were recovered by feeding known and pseudo-random samples through ffmpeg's
/// own encoder and sweeping every reading of which component owns which bit range and where a row's
/// padding begins against what came out.
/// <para/>
/// <b>The word, little-endian.</b> Red sits in bits 22-31, green in bits 12-21, blue in bits 2-11, and
/// the low two bits are always zero across every pixel measured. Reading the same bit ranges r10k uses
/// but off a little-endian rather than a big-endian word reproduces every sample of every geometry
/// tried; no other reading of the four bytes does.
/// <para/>
/// <b>The padding.</b> A row is <c>width</c> rounded up to the next whole multiple of sixty-four
/// pixels, times four bytes — measured directly against ffmpeg's own encoder at eight geometries from
/// 8x2 up to 100x30, where the coded row is 64, 64, 128 and 64 pixels respectively for source widths
/// of 8, 33, 100 and 64. There is no padding in the vertical direction at all: height scales the coded
/// size exactly linearly at every width tried. The padding columns themselves are read and thrown
/// away rather than assumed to hold anything in particular.
/// <para/>
/// <b>Decoded straight into <see cref="PixelFormat.Rgb30"/> and nothing is lost doing it</b> — the
/// same repacking r10k needs, since <see cref="PixelFormat.Rgb30"/>'s own layout puts red in the low
/// ten bits rather than the high ten, little-endian throughout. The two bits this format leaves unused
/// become that format's alpha field, set to fully opaque.
/// <para/>
/// <b>Verified exactly.</b> Four geometries and twenty frames of ffmpeg's own <c>rgbtestsrc</c> —
/// 8x2, 64x40, 100x30 and 33x25, covering a width under one block, exactly one block, more than one
/// block and one that pads by less than half a block — carried through avrp and decoded here, compared
/// word for word against the <c>gbrp10le</c> planes that went into the encoder: identical on every one,
/// with the low two bits of every word zero throughout.
/// <para/>
/// <b>What refuses.</b> A picture with no pixels, and a packet shorter than its stride times its
/// height.
/// </remarks>
public sealed class AvrpVideoDecoder : IVideoCodecDecoder<AvrpVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("AVrp");

  private readonly int _width;
  private readonly int _height;
  private readonly int _streamIndex;
  private readonly int _paddedWidth;
  private readonly int _stride;

  private AvrpVideoDecoder(int width, int height, int streamIndex) {
    this._width = width;
    this._height = height;
    this._streamIndex = streamIndex;
    this._paddedWidth = (width + 63) / 64 * 64;
    this._stride = this._paddedWidth * 4;
  }

  public static string CodecName => "Avid 1:1 10-bit RGB Packer (avrp)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static AvrpVideoDecoder Create(MediaStreamInfo stream) {
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
        $"Video stream {this._streamIndex} carries an avrp packet of {data.Length} byte(s), where a "
        + $"{this._width}x{this._height} frame at a stride of {this._stride} needs {expected}.");

    var pixels = new byte[this._width * this._height * 4];

    for (var y = 0; y < this._height; ++y) {
      var row = data.Slice(y * this._stride, this._stride);
      var target = pixels.AsSpan(y * this._width * 4);

      for (var x = 0; x < this._width; ++x) {
        var word = BinaryPrimitives.ReadUInt32LittleEndian(row[(x * 4)..]);
        var red = (word >> 22) & 0x3FF;
        var green = (word >> 12) & 0x3FF;
        var blue = (word >> 2) & 0x3FF;

        // Rgb30's own layout: red in bits 0-9, green in 10-19, blue in 20-29, alpha in 30-31 — set
        // fully opaque, which is what the two bits this format leaves spare are worth.
        var packed = red | (green << 10) | (blue << 20) | 0xC0000000u;
        BinaryPrimitives.WriteUInt32LittleEndian(target[(x * 4)..], packed);
      }
    }

    frame = new() { Width = this._width, Height = this._height, Format = PixelFormat.Rgb30, PixelData = pixels };
    return true;
  }
}
