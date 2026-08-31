using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes r10k: 10-bit RGB with nothing compressed at all, one 32-bit big-endian word a pixel and no
/// row padding.
/// </summary>
/// <remarks>
/// AJA's Kona 10-bit RGB layout, and a close relative of r210 rather than the same format under a
/// second name — the two differ in where the ten-bit fields and the two unused bits sit inside the
/// word, and in whether a row carries any padding at all. Neither is written down anywhere this
/// project found; both were recovered the same way, by sweeping every reading of which component owns
/// which bit range against ffmpeg's own encoder fed a picture of known samples and keeping the one
/// that reproduces the source for every pixel.
/// <para/>
/// <b>The word.</b> Red sits in the high ten bits, bits 22-31; green in the middle ten, bits 12-21;
/// blue in the next ten down, bits 2-11; and the two unused bits are the <i>low</i> two of the word
/// rather than the high two r210 leaves spare. <b>There is no row padding at all</b> — a row is
/// exactly <c>width</c> times four bytes, measured against three geometries including one whose
/// unpadded row is not a multiple of any alignment r210's family uses, and ffmpeg's own encoder never
/// writes a byte beyond it.
/// <para/>
/// <b>Decoded straight into <see cref="PixelFormat.Rgb30"/> and nothing is lost doing it.</b> That
/// format's layout — red in bits 0-9, green in 10-19, blue in 20-29, little-endian — is a different
/// bit arrangement from r10k's own word, so unlike r210 this is a real repacking and not a plain byte
/// reversal: each component is pulled out of its own position in the big-endian word and written back
/// into <see cref="PixelFormat.Rgb30"/>'s. The two bits this format leaves unused become that format's
/// alpha field, set to fully opaque.
/// <para/>
/// <b>Verified exactly.</b> Three geometries and ninety frames of ffmpeg's <c>rgbtestsrc</c> — 8x2,
/// 33x25 and 64x40 — carried through r10k and decoded here, compared word for word against the
/// <c>gbrp10le</c> planes that went into the encoder: identical on every one, because a fixed packing
/// with nothing adaptive in it has nothing left to get wrong once the bit ranges are right. Carrying a
/// stream through ffmpeg's own r10k decoder and back out to <c>gbrp10le</c> reproduces the same source
/// exactly as well, which is what settled that the two padding bits carry nothing worth reading back.
/// <para/>
/// <b>What refuses.</b> A picture with no pixels, and a packet shorter than its stride times its
/// height.
/// </remarks>
public sealed class R10kVideoDecoder : IVideoCodecDecoder<R10kVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("R10k");

  private readonly int _width;
  private readonly int _height;
  private readonly int _streamIndex;
  private readonly int _stride;

  private R10kVideoDecoder(int width, int height, int streamIndex) {
    this._width = width;
    this._height = height;
    this._streamIndex = streamIndex;
    this._stride = width * 4;
  }

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "AJA Kona 10-bit RGB (r10k)";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  /// <summary>Creates a decoder for the specified media stream.</summary>
  public static R10kVideoDecoder Create(MediaStreamInfo stream) {
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
        $"Video stream {this._streamIndex} carries an r10k packet of {data.Length} byte(s), where a "
        + $"{this._width}x{this._height} frame at a stride of {this._stride} needs {expected}.");

    var pixels = new byte[this._width * this._height * 4];

    for (var y = 0; y < this._height; ++y) {
      var row = data.Slice(y * this._stride, this._stride);
      var target = pixels.AsSpan(y * this._width * 4);

      for (var x = 0; x < this._width; ++x) {
        var word = BinaryPrimitives.ReadUInt32BigEndian(row[(x * 4)..]);
        var red = (word >> 22) & 0x3FF;
        var green = (word >> 12) & 0x3FF;
        var blue = (word >> 2) & 0x3FF;

        // Rgb30's own layout: red in bits 0-9, green in 10-19, blue in 20-29, alpha in 30-31 — set
        // fully opaque, which is what ffmpeg's own decoder writes into the two bits r10k leaves spare.
        var packed = red | (green << 10) | (blue << 20) | 0xC0000000u;
        BinaryPrimitives.WriteUInt32LittleEndian(target[(x * 4)..], packed);
      }
    }

    frame = new() { Width = this._width, Height = this._height, Format = PixelFormat.Rgb30, PixelData = pixels };
    return true;
  }
}
