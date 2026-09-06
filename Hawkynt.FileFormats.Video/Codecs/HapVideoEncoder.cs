using System;
using System.IO;
using FileFormat.Codecs.Hap;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Vidvox Hap: a DXT/BC texture, block for block as a graphics card would be loaded with it,
/// wrapped in the format's one-section frame and offered to Snappy.
/// </summary>
/// <remarks>
/// <b>What it writes.</b> Three of Hap's seven pixel formats — <c>Hap1</c> (DXT1/BC1, no alpha),
/// <c>Hap5</c> (DXT5/BC3 with alpha) and <c>HapY</c> (Scaled YCoCg DXT5) — which are exactly the
/// three ffmpeg's own <c>hap</c> encoder writes and therefore exactly the three there is an oracle
/// for. <see cref="Create"/> takes the variant from the code the stream names and a caller naming
/// none gets <c>Hap1</c>. Every frame is a key frame; Hap has no inter-frame coding of any kind.
/// <para/>
/// <b>What it refuses, by name.</b> <c>HapM</c> (Hap Q Alpha), <c>HapA</c> (Hap Alpha-Only),
/// <c>Hap7</c> (Hap R, BC7) and <c>HapH</c> (Hap HDR, BC6) — all four decoded by
/// <see cref="HapDecoder"/> and none of them written here, because no encoder this package can be
/// measured against produces one and a BC6 or BC7 texture assembled from a specification alone would
/// be a plausible picture with nothing to check it against. A picture whose width or height is not a
/// whole number of four-sample blocks is refused too, as ffmpeg's own encoder refuses it: the format
/// carries no cropped-picture size, so the only alternative would be padding a texture the decoder
/// would then hand back at the wrong size. And a picture that is neither
/// <see cref="PixelFormat.Rgb24"/> nor <see cref="PixelFormat.Rgba32"/> is refused rather than
/// converted — those two are what <see cref="HapDecoder"/> hands back, so a decode and a re-encode
/// need no conversion, and whether any other conversion may lose something is not this codec's
/// decision.
/// <para/>
/// <b>The frame.</b> One top-level section, its header always the eight-byte form, its type byte the
/// pixel format in the low nibble and the second-stage compressor in the high one. The texture is
/// offered to Snappy whole — one chunk, no Decode Instructions Container — and the result is kept only
/// where it came out smaller than the texture; otherwise the type byte says uncompressed and the
/// texture is written as it is. That is ffmpeg's own single-chunk behaviour, header form included.
/// This encoder never writes the chunked "consult decode instructions" form, which exists so a GPU
/// upload can be split across threads and costs a decoder nothing to be without.
/// <para/>
/// <b>Lossy by construction, and here is where.</b> DXT quantises each 4x4 block to two 5-6-5
/// endpoints and a two-bit index a pixel, so a block holding three or more colours cannot come back as
/// it went in — that is the format, not a setting. <b>What the format can hold exactly does come back
/// exactly.</b> The endpoints are 5-6-5 words widened back by <see cref="HapBlockDecoding"/>'s own
/// tables, which state 32 red and blue values and 64 green ones — 65536 colours — and every one of
/// them, alone or two to a block, survives a <c>Hap1</c> or <c>Hap5</c> round trip untouched:
/// measured over all 65536, one to a block and again chequered in pairs, 1048576 pixels a picture at
/// max delta 0. <c>Hap5</c> carries any single alpha value a block holds exactly as well, all 256 of
/// them, the ramp going unused when a block's minimum and maximum coincide. Off that grid the coding
/// is as close as the reference's own endpoint search gets: no farther than 2 from any of the 256
/// greys, and no farther than 1 from any of the 32768 colours of the bit-replicated 5-5-5 grid.
/// <c>HapY</c> is lossy even for a flat block — its chroma transform is not reversible in eight bits,
/// and it is the format's own transform.
/// <para/>
/// <b>Measured against ffmpeg, in both directions.</b> The block compression is FFmpeg's
/// <c>libavcodec/texturedspenc.c</c> carried across whole — an MIT-licensed file inside an LGPL
/// project — so the comparison is not "close enough" but byte for byte. Over 24 streams ffmpeg wrote
/// at 4x4, 12x8, 68x36, 64x64, 96x64, 128x96, 160x120 and 320x240 in all three pixel formats, 96
/// textures and 1210880 texture bytes, pseudo-random pictures and photographic-looking gradients
/// alike: 20 bytes differ, in 3 textures, and every one of them is a block where the reference's own
/// endpoint choice returns a colour the grid states exactly a level or two off and this encoder
/// writes it exactly instead — 48 pixels exact here and not there, none the other way round. Every
/// other byte of every other block is the reference's own.
/// <para/>
/// The other direction is the one that matters: 24 streams written here, 240 frames, muxed and handed
/// to ffmpeg's own Hap decoder, which reads them at its own native pixel format (<c>rgb0</c> for
/// <c>Hap1</c> and <c>HapY</c>, <c>rgba</c> for <c>Hap5</c>, so nothing is converted behind the
/// comparison). Against this package's decode of the same packets: 12108800 samples, every one
/// identical. 146 of those 240 frames went out Snappy-compressed and 94 uncompressed, so Google's own
/// Snappy — which is what ffmpeg reads them with — accepted this package's block writer on 146 of
/// them.
/// </remarks>
public sealed class HapVideoEncoder : IVideoCodecEncoder<HapVideoEncoder> {

  private static readonly CodecTag _Hap1 = CodecTag.FromCharacters("Hap1");
  private static readonly CodecTag _Hap5 = CodecTag.FromCharacters("Hap5");
  private static readonly CodecTag _HapY = CodecTag.FromCharacters("HapY");
  private static readonly CodecTag _HapM = CodecTag.FromCharacters("HapM");
  private static readonly CodecTag _HapA = CodecTag.FromCharacters("HapA");
  private static readonly CodecTag _Hap7 = CodecTag.FromCharacters("Hap7");
  private static readonly CodecTag _HapH = CodecTag.FromCharacters("HapH");

  /// <summary>The side of a compressed texture block, in pixels.</summary>
  private const int _BLOCK = 4;

  /// <summary>The low nibble of a top-level type byte, one value a pixel format.</summary>
  private const byte _FORMAT_DXT1_RGB = 0x0B;
  private const byte _FORMAT_DXT5_RGBA = 0x0E;
  private const byte _FORMAT_DXT5_SCALED_YCOCG = 0x0F;

  /// <summary>The high nibble of a top-level type byte, naming the second-stage compressor.</summary>
  private const byte _COMPRESSOR_NONE = 0xA0;
  private const byte _COMPRESSOR_SNAPPY = 0xB0;

  private readonly MediaStreamInfo _stream;
  private readonly CodecTag _tag;
  private readonly HapPixelFormat _format;
  private readonly int _width;
  private readonly int _height;
  private readonly int _blocksAcross;
  private readonly int _blockRows;
  private readonly int _blockBytes;

  private HapVideoEncoder(MediaStreamInfo stream, CodecTag tag, HapPixelFormat format) {
    this._tag = tag;
    this._format = format;
    this._width = stream.Width;
    this._height = stream.Height;
    this._blocksAcross = stream.Width / _BLOCK;
    this._blockRows = stream.Height / _BLOCK;
    this._blockBytes = format == HapPixelFormat.Dxt1Rgb ? 8 : 16;
    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = tag,
      Handler = tag,
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = format == HapPixelFormat.Dxt5Rgba ? 32 : 24,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "Hap";

  /// <summary>The code the registry routes here, and what this writes unless the stream names another.</summary>
  public static CodecTag Codec => _Hap1;

  /// <summary>Builds an encoder for the stream described, taking the pixel format from its code.</summary>
  public static HapVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Hap can only encode a video stream.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can "
        + "be coded from.");

    if (stream.Width % _BLOCK != 0 || stream.Height % _BLOCK != 0)
      throw new NotSupportedException(
        $"Video stream {stream.Index} states {stream.Width}x{stream.Height}, which is not a whole number of "
        + $"{_BLOCK}x{_BLOCK} texture blocks in each direction. A Hap frame carries the texture and nothing else, so "
        + "there is nowhere to state that part of the last block or row is to be thrown away; ffmpeg's own Hap "
        + "encoder refuses the same size.");

    var (tag, format) = _VariantOf(stream.Codec, stream.Index);
    return new(stream, tag, format);
  }

  /// <summary>Which pixel format a stream's code asks for, and what to say about the ones not written.</summary>
  private static (CodecTag Tag, HapPixelFormat Format) _VariantOf(CodecTag codec, int index) {
    if (codec == CodecTag.None || codec.EqualsIgnoringCase(_Hap1))
      return (_Hap1, HapPixelFormat.Dxt1Rgb);

    if (codec.EqualsIgnoringCase(_Hap5))
      return (_Hap5, HapPixelFormat.Dxt5Rgba);

    if (codec.EqualsIgnoringCase(_HapY))
      return (_HapY, HapPixelFormat.Dxt5ScaledYCoCg);

    var unwritten = _Unwritten(codec);
    if (unwritten != null)
      throw new NotSupportedException(
        $"Video stream {index} asks for {codec}, {unwritten}. This encoder writes Hap1, Hap5 and HapY — the three "
        + "pixel formats there is a second encoder to measure the result against; a texture in any of the others "
        + "would be a plausible picture with nothing to check it against, so it is refused rather than written.");

    throw new NotSupportedException(
      $"Video stream {index} asks for {codec}, which is not a Hap code at all.");
  }

  private static string? _Unwritten(CodecTag codec) {
    if (codec.EqualsIgnoringCase(_HapM))
      return "Hap Q Alpha — a Scaled YCoCg image and a separate RGTC1/BC4 alpha image in one frame";

    if (codec.EqualsIgnoringCase(_HapA))
      return "Hap Alpha-Only — a single RGTC1/BC4 channel";

    if (codec.EqualsIgnoringCase(_Hap7))
      return "Hap R — a BC7 texture";

    return codec.EqualsIgnoringCase(_HapH) ? "Hap HDR — a signed or unsigned BC6H texture" : null;
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);

    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"This Hap stream is {this._width}x{this._height}; a picture of {frame.Width}x{frame.Height} cannot be coded "
        + "into it. The picture size is stated by the container and never by the frame.");

    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    packet = new(
      this._stream.Index,
      this._WriteFrame(this.CompressTexture(frame)),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  /// <summary>Compresses one picture into the texture a Hap section carries, framing left off.</summary>
  internal byte[] CompressTexture(RawImage frame) {
    var pixels = this._AsRgba(frame);
    var stride = this._width * 4;
    var texture = new byte[this._blocksAcross * this._blockRows * this._blockBytes];

    for (var blockRow = 0; blockRow < this._blockRows; ++blockRow) {
      var rowAt = blockRow * stride * _BLOCK;
      for (var blockColumn = 0; blockColumn < this._blocksAcross; ++blockColumn) {
        var block = pixels.AsSpan(rowAt + blockColumn * _BLOCK * 4);
        var destination = texture.AsSpan((blockRow * this._blocksAcross + blockColumn) * this._blockBytes, this._blockBytes);

        switch (this._format) {
          case HapPixelFormat.Dxt1Rgb:
            HapBlockEncoding.CompressDxt1(block, stride, destination);
            break;
          case HapPixelFormat.Dxt5Rgba:
            HapBlockEncoding.CompressDxt5(block, stride, destination);
            break;
          default:
            HapBlockEncoding.CompressScaledYCoCg(block, stride, destination);
            break;
        }
      }
    }

    return texture;
  }

  /// <summary>
  /// The picture as four bytes a pixel, which is the one layout the block compressor reads.
  /// </summary>
  /// <remarks>
  /// A <see cref="PixelFormat.Rgb24"/> picture is widened with a fully opaque alpha rather than with
  /// whatever the padding byte happened to hold: the block compressor compares whole RGBA words when
  /// it asks whether a block is one colour, so an alpha that varies would split blocks that do not.
  /// </remarks>
  private byte[] _AsRgba(RawImage frame) {
    var count = this._width * this._height;
    var pixels = new byte[count * 4];
    var source = frame.PixelData;

    switch (frame.Format) {
      case PixelFormat.Rgba32:
        source.AsSpan(0, count * 4).CopyTo(pixels);
        break;

      case PixelFormat.Rgb24:
        for (var i = 0; i < count; ++i) {
          var from = i * 3;
          var to = i * 4;
          pixels[to] = source[from];
          pixels[to + 1] = source[from + 1];
          pixels[to + 2] = source[from + 2];
          pixels[to + 3] = 255;
        }

        break;

      default:
        throw new NotSupportedException(
          $"Hap is written here from Rgb24 and Rgba32 pictures only; a {frame.Format} picture would have to be "
          + "converted first, and whether that conversion may lose anything is not this codec's decision. Those two "
          + "are what the Hap decoder hands back, so a decode and a re-encode need none.");
    }

    return pixels;
  }

  /// <summary>
  /// Wraps one texture in the format's single top-level section, compressing it where that helps.
  /// </summary>
  private byte[] _WriteFrame(byte[] texture) {
    var compressed = HapSnappyEncoder.Compress(texture);
    var payload = compressed.Length < texture.Length ? compressed : texture;
    var compressor = ReferenceEquals(payload, texture) ? _COMPRESSOR_NONE : _COMPRESSOR_SNAPPY;

    var frame = new byte[8 + payload.Length];
    frame[3] = (byte)(compressor | this._PixelFormatCode());
    frame[4] = (byte)payload.Length;
    frame[5] = (byte)(payload.Length >> 8);
    frame[6] = (byte)(payload.Length >> 16);
    frame[7] = (byte)(payload.Length >> 24);
    payload.CopyTo(frame, 8);
    return frame;
  }

  private byte _PixelFormatCode() => this._format switch {
    HapPixelFormat.Dxt1Rgb => _FORMAT_DXT1_RGB,
    HapPixelFormat.Dxt5Rgba => _FORMAT_DXT5_RGBA,
    _ => _FORMAT_DXT5_SCALED_YCOCG,
  };
}
