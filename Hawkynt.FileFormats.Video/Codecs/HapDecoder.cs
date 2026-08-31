using System;
using System.IO;
using FileFormat.Codecs.Hap;
using FileFormat.Core;
using FileFormat.Core.BlockDecoders;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Vidvox Hap: DXT/BC texture compression carried in a small chunked container, optionally
/// run through Snappy as a second stage.
/// </summary>
/// <remarks>
/// Hap frames are made to be handed to a GPU almost unchanged — the payload of a frame carrying
/// <c>Hap1</c> or <c>Hap5</c> is exactly the DXT1 or DXT5 texture a graphics card would be loaded
/// with, block for block. That is what makes this codec unlike almost everything else in this
/// package: a block's four or sixteen decoded pixels are not an approximation converging on the
/// source, and not the output of a transform with a stated accuracy bound — they are the one and only
/// picture that block's bits mean, defined completely by S3TC (DXT1/BC1, DXT5/BC3), BC7, BC6H and,
/// for the "Q" pixel format, by van Waveren and Castaño's Scaled YCoCg-DXT5 reconstruction.
/// <para/>
/// The frame layout is entirely published, in the Hap project's own repository on GitHub
/// (<c>documentation/HapVideoDRAFT.md</c>): a run of sections, each a type byte and a size in a header
/// that is four bytes or grows to eight when the size does not fit in three; a top-level section's
/// type names a pixel format and how its data reaches it — as-is, through a single Snappy block, or,
/// for the "consult decode instructions" forms, cut into chunks that are each decompressed on their
/// own; and one type byte, 0x0D, names no pixel format at all and instead holds one or two further
/// top-level sections whose textures are combined — the only combination the format defines being
/// Scaled YCoCg DXT5 with a separate RGTC1/BC4 alpha image, which is what the <c>HapM</c> code names.
/// The section walk, the chunk table layout and the Snappy block format (read from Google's own
/// <c>format_description.txt</c>, since Hap names Snappy as an external reference rather than
/// restating it) are all in <see cref="HapFrameParser"/> and <see cref="HapSnappyDecoder"/>. The DXT1,
/// DXT5 and RGTC1/BC4 block decode is in <see cref="HapBlockDecoding"/>, read from the OpenGL S3TC and
/// RGTC extension texts Hap names as external references — a decode of its own rather than a reuse of
/// <c>FileFormat.Core.BlockDecoders</c>, because the two disagree on the third and fourth colour of a
/// four-colour block and the interpolated steps of an alpha ramp: that code rounds them to the nearest
/// whole value, where both extension texts give a plain integer division with no rounding term, and
/// ffmpeg's Hap decoder agrees with the plain reading and not the rounded one. BC7 has no such Hap-
/// specific interpretation, so <see cref="Bc7Decoder"/> is reused directly for Hap R.
/// <para/>
/// <b>The Scaled YCoCg pixel format is a DXT5 block read for different meaning.</b> The eight-sample
/// alpha channel, reproduced at full precision rather than through a three-bit index into four
/// values, carries luma; the DXT1-style colour part carries the two chroma channels signed around 128
/// in its red and green samples and a per-block scale factor in blue, which widens them back out
/// before 5- and 6-bit quantisation crushed them. <see cref="HapYCoCgConversion"/> carries the
/// derivation from the paper's own fragment-program pseudocode in full.
/// <para/>
/// <b>Hap R and Hap HDR preserve their native precision.</b> Hap R's BC7 texture becomes
/// <see cref="PixelFormat.Rgba32"/>. Hap HDR's unsigned or signed BC6H texture becomes
/// <see cref="PixelFormat.RgbF16"/> through <see cref="Bc6HFloatDecoder"/>; values below zero and above
/// one remain representable and are not tone-mapped or clipped by the decoder. A writer or display
/// path that needs integer RGB can request that conversion later through <see cref="RawImageConverter"/>.
/// <para/>
/// <b>Measured, and lossless with respect to its own coded blocks.</b> A DXT1, DXT5 or Scaled YCoCg
/// block's decode is exactly defined, so — unlike the DCT and wavelet codecs elsewhere in this
/// package — the bar is the lossless one: max delta 0 on every sample of every frame, not a bound on
/// how close. ffmpeg's Hap encoder writes exactly the three pixel formats named <c>Hap1</c>, <c>Hap5</c>
/// and <c>HapY</c> above, so the corpus was built here rather than fetched from samples.ffmpeg.org:
/// six streams, 64x64 to 96x64, one to eight chunks, both second-stage compressors, one hundred
/// frames each — ffmpeg's Hap encoder refuses a picture size that is not a whole number of
/// four-sample blocks in each direction, so every stream in the corpus is block-aligned; the block
/// grid math itself still rounds up, per the S3TC extension's own addressing formula, for a cropped
/// picture no encoder measured here produces. Decoded here and by ffmpeg
/// (`-threads 1 -fps_mode passthrough`, frame count cross-checked against `ffprobe -count_frames`) and
/// compared **on raw RGB or RGBA planes, never through a format that composites alpha** — Hap is
/// RGB/RGBA-native, carrying no chroma subsampling of any kind, so a direct plane comparison is the
/// correct one and not merely a convenient one, stated explicitly because that is what makes the
/// number mean something. Six hundred frames, every sample of every plane, identical.
/// <para/>
/// Between the six streams: every top-level pixel format ffmpeg writes; both DXT1's implicit-alpha
/// four-colour branch and its two-colour-plus-black branch, wherever the encoder happened to choose
/// one; a whole-frame Snappy block and an uncompressed one; and, at eight chunks, ffmpeg's encoder
/// chose the "consult decode instructions" form on its own — type byte <c>0xCF</c>, a nested Decode
/// Instructions Container, a compressor table and a size table with no offset table, eight
/// independently Snappy-decompressed chunks concatenated back into one Scaled YCoCg texture — which is
/// what exercises <see cref="HapFrameParser"/>'s chunked path end to end rather than only by
/// construction. ffmpeg's own top-level section headers are all eight bytes regardless of size, so the
/// four-byte form is reached only by a hand-built frame in this codec's own tests; the section-header
/// rule itself does not change shape between the two, only how far it has to reach.
/// <para/>
/// <b>The 5-bit and 6-bit colour-endpoint expansion is not any of the formulas it looks like.</b> Not
/// bit replication, and not a single rounding or truncating division by 31 or 63 at any constant added
/// before the divide — every one of those reproduces most of the thirty-two or sixty-four values and
/// gets a handful wrong, in both directions. What is in <see cref="HapBlockDecoding"/> was read
/// directly off ffmpeg's decode: every input value, at a colour index the S3TC extension text defines
/// as the endpoint outright with no interpolation involved, appearing hundreds to hundreds of
/// thousands of times across the corpus and never once disagreeing with another occurrence of the same
/// input. The interpolated third and fourth colours of a four-colour block, and the six interpolated
/// steps of an alpha ramp, read the OpenGL S3TC and RGTC extension texts literally — plain integer
/// division, no rounding term — which is where this decoder disagrees with
/// <c>FileFormat.Core.BlockDecoders</c>' shared BC1/BC3/BC4 code; see that type's own remarks.
/// <para/>
/// <b>What refuses, and by name.</b> A section whose header does not fit, a size that runs past the
/// data holding it, a top-level type byte naming no pixel format and no multiple-image marker this
/// codec knows, a "consult decode instructions" section missing its compressor table or its size
/// table, a chunk naming a compressor that is neither uncompressed nor Snappy, a Snappy block whose
/// elements do not produce the length its own preamble states, a back-reference pointing before the
/// start of the output, and a multiple-image section holding a combination other than Scaled YCoCg
/// DXT5 with RGTC1/BC4 alpha. There is no <c>catch</c> anywhere in this decoder that hands back a blank
/// frame or repeats the one before it.
/// </remarks>
public sealed class HapDecoder : IVideoCodecDecoder<HapDecoder> {

  private static readonly CodecTag _Hap1 = CodecTag.FromCharacters("Hap1");
  private static readonly CodecTag _Hap5 = CodecTag.FromCharacters("Hap5");
  private static readonly CodecTag _HapY = CodecTag.FromCharacters("HapY");
  private static readonly CodecTag _HapM = CodecTag.FromCharacters("HapM");
  private static readonly CodecTag _HapA = CodecTag.FromCharacters("HapA");
  private static readonly CodecTag _Hap7 = CodecTag.FromCharacters("Hap7");
  private static readonly CodecTag _HapH = CodecTag.FromCharacters("HapH");

  private readonly int _width;
  private readonly int _height;
  private readonly int _streamIndex;

  private HapDecoder(int width, int height, int streamIndex) {
    this._width = width;
    this._height = height;
    this._streamIndex = streamIndex;
  }

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "Hap";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      return false;

    var tag = stream.Codec;
    return tag.EqualsIgnoringCase(_Hap1) || tag.EqualsIgnoringCase(_Hap5) || tag.EqualsIgnoringCase(_HapY)
      || tag.EqualsIgnoringCase(_HapM) || tag.EqualsIgnoringCase(_HapA) || tag.EqualsIgnoringCase(_Hap7)
      || tag.EqualsIgnoringCase(_HapH);
  }

  /// <summary>Creates a decoder for the specified media stream.</summary>
  public static HapDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can be decoded into.");

    return new(stream.Width, stream.Height, stream.Index);
  }

  /// <summary>Attempts to decode the specified coded packet into a raw image frame.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var textures = HapFrameParser.ParseFrame(packet.Data.Span);
    frame = textures.Count switch {
      1 => this._ComposeSingle(textures[0]),
      2 => this._ComposeCombined(textures[0], textures[1]),
      _ => throw new InvalidDataException($"A Hap frame on video stream {this._streamIndex} holds {textures.Count} images; the format defines combinations of one or two."),
    };

    return true;
  }

  private RawImage _ComposeSingle(HapTexture texture) => texture.Format switch {
    HapPixelFormat.Dxt1Rgb => this._DecodeDxt1Rgb(texture.Data),
    HapPixelFormat.Dxt5Rgba => this._DecodeDxt5Rgba(texture.Data),
    HapPixelFormat.Dxt5ScaledYCoCg => this._DecodeScaledYCoCg(texture.Data),
    HapPixelFormat.Bc7Rgba => this._DecodeBc7Rgba(texture.Data),
    HapPixelFormat.Rgtc1Alpha => this._DecodeRgtc1Alpha(texture.Data),
    HapPixelFormat.Bc6UnsignedFloat => this._DecodeBc6(texture.Data, isSigned: false),
    HapPixelFormat.Bc6SignedFloat => this._DecodeBc6(texture.Data, isSigned: true),
    _ => throw new NotSupportedException(
      $"Video stream {this._streamIndex} carries a frame in texture format {texture.Format}, which this decoder does not turn into pixels."),
  };

  private RawImage _ComposeCombined(HapTexture first, HapTexture second) {
    var (colour, alpha) = (first.Format, second.Format) switch {
      (HapPixelFormat.Dxt5ScaledYCoCg, HapPixelFormat.Rgtc1Alpha) => (first, second),
      (HapPixelFormat.Rgtc1Alpha, HapPixelFormat.Dxt5ScaledYCoCg) => (second, first),
      _ => throw new NotSupportedException(
        $"Video stream {this._streamIndex} carries a two-image frame combining {first.Format} with {second.Format}; the only combination Hap defines is Scaled YCoCg DXT5 with RGTC1/BC4 alpha."),
    };

    var width = this._width;
    var height = this._height;
    var rgb = this._DecodeScaledYCoCgRgb(colour.Data);
    var alphaPlane = this._DecodeAlphaPlane(alpha.Data);

    var count = width * height;
    var pixels = new byte[count * 4];
    for (var i = 0; i < count; ++i) {
      var src = i * 3;
      var dst = i * 4;
      pixels[dst] = rgb[src];
      pixels[dst + 1] = rgb[src + 1];
      pixels[dst + 2] = rgb[src + 2];
      pixels[dst + 3] = alphaPlane[i];
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = pixels };
  }

  private RawImage _DecodeDxt1Rgb(byte[] data) {
    var width = this._width;
    var height = this._height;
    this._CheckBlockDataLength(data, width, height, 8, "DXT1/BC1");

    var rgb = HapBlockDecoding.DecodeDxt1ToRgb(data, width, height);
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private RawImage _DecodeDxt5Rgba(byte[] data) {
    var width = this._width;
    var height = this._height;
    this._CheckBlockDataLength(data, width, height, 16, "DXT5/BC3");

    var rgba = HapBlockDecoding.DecodeDxt5Raw(data, width, height);
    return new() { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = rgba };
  }

  private RawImage _DecodeBc7Rgba(byte[] data) {
    var width = this._width;
    var height = this._height;
    this._CheckBlockDataLength(data, width, height, 16, "BC7");

    var rgba = new byte[width * height * 4];
    Bc7Decoder.DecodeImage(data, width, height, rgba);
    return new() { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = rgba };
  }

  private RawImage _DecodeBc6(byte[] data, bool isSigned) {
    var width = this._width;
    var height = this._height;
    this._CheckBlockDataLength(data, width, height, 16, isSigned ? "signed BC6H" : "unsigned BC6H");

    var rgb = new byte[checked(width * height * 6)];
    Bc6HFloatDecoder.DecodeImage(data, width, height, rgb, isSigned);
    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.RgbF16,
      PixelData = rgb,
      ColorInfo = new() {
        Range = RawColorRange.Full,
        Matrix = RawMatrixCoefficients.Identity,
      },
    };
  }

  private RawImage _DecodeScaledYCoCg(byte[] data) {
    var width = this._width;
    var height = this._height;
    var rgb = this._DecodeScaledYCoCgRgb(data);
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private byte[] _DecodeScaledYCoCgRgb(byte[] data) {
    var width = this._width;
    var height = this._height;
    this._CheckBlockDataLength(data, width, height, 16, "Scaled YCoCg DXT5/BC3");

    var raw = HapBlockDecoding.DecodeDxt5Raw(data, width, height);

    var count = width * height;
    var rgb = new byte[count * 3];
    for (var i = 0; i < count; ++i) {
      var at = i * 4;
      var (r, g, b) = HapYCoCgConversion.ToRgb(raw[at], raw[at + 1], raw[at + 2], raw[at + 3]);
      rgb[i * 3] = r;
      rgb[i * 3 + 1] = g;
      rgb[i * 3 + 2] = b;
    }

    return rgb;
  }

  private RawImage _DecodeRgtc1Alpha(byte[] data) {
    var width = this._width;
    var height = this._height;
    var plane = this._DecodeAlphaPlane(data);
    return new() { Width = width, Height = height, Format = PixelFormat.Gray8, PixelData = plane };
  }

  private byte[] _DecodeAlphaPlane(byte[] data) {
    var width = this._width;
    var height = this._height;
    this._CheckBlockDataLength(data, width, height, 8, "RGTC1/BC4");

    return HapBlockDecoding.DecodeRgtc1(data, width, height);
  }

  private void _CheckBlockDataLength(byte[] data, int width, int height, int blockSize, string formatName) {
    var blocksX = (width + 3) / 4;
    var blocksY = (height + 3) / 4;
    var expected = blocksX * blocksY * blockSize;
    if (data.Length != expected)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries a {formatName} texture of {data.Length} bytes for a {width}x{height} picture, which needs exactly {expected} bytes ({blocksX}x{blocksY} blocks of {blockSize}).");
  }
}