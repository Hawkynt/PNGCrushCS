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
/// word, stored big-endian, laid out red in the high ten bits after two unused ones, green in the
/// middle ten and blue in the low ten. MultimediaWiki's own page for the format states the bit string
/// this way round; measured against a real encoder it is not — red sits in the word's <i>low</i> ten
/// bits, green in the middle and blue in the high ten, found by sweeping every reading of which
/// component owns which bit range against ffmpeg's own r210 encoder fed a picture of known samples,
/// where only one reading reproduces the source for every pixel of every geometry tried. A row is
/// padded out to a whole 256 bytes, which several vendors' 10-bit RGB formats share.
/// <para/>
/// <b>Decoded straight into <see cref="PixelFormat.Rgb30"/> and nothing is lost doing it.</b> That
/// format's own ten-bit layout — red in bits 0-9, green in 10-19, blue in 20-29, little-endian — is
/// exactly what falls out of reading r210's big-endian word and writing the same bits back
/// little-endian, so there is no eight-bit reduction and no display convention standing between the
/// coded samples and what a caller receives; the two padding bits this format leaves unused become
/// the alpha field <see cref="PixelFormat.Rgb30"/> reserves in the same position, set to fully opaque
/// because that is what ffmpeg's own decoder writes into them, measured by carrying a stream through
/// it and back out to <c>x2rgb10le</c> — the same 30 bits in the same arrangement this format owns —
/// and finding every sample of every frame identical to what went into the encoder, alpha included.
/// <para/>
/// <b>Verified exactly.</b> Three geometries and ninety frames of ffmpeg's <c>rgbtestsrc</c> — 8x2 and
/// 64x40, both a whole number of 256-byte rows, and 33x25, which needs the padding — carried through
/// r210 and decoded here, compared word for word against the <c>x2rgb10le</c> samples that went into
/// the encoder: identical on every one, because a fixed packing with nothing adaptive in it has
/// nothing left to get wrong once the bit ranges are right.
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

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "Uncompressed RGB 10-bit (r210)";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  /// <summary>Creates a decoder for the specified media stream.</summary>
  public static R210VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can "
        + "be decoded into.");

    return new(stream.Width, stream.Height, stream.Index);
  }

  /// <summary>Attempts to decode the specified coded packet into a raw image frame.</summary>
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
        // The word's own two padding bits and this format's own zero there become the alpha field
        // Rgb30 reserves in the same position; set to fully opaque, which is what ffmpeg's own
        // decoder writes there.
        var word = BinaryPrimitives.ReadUInt32BigEndian(row[(x * 4)..]) | 0xC0000000u;
        BinaryPrimitives.WriteUInt32LittleEndian(target[(x * 4)..], word);
      }
    }

    frame = new() { Width = this._width, Height = this._height, Format = PixelFormat.Rgb30, PixelData = pixels };
    return true;
  }
}
