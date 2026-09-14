using System;
using System.IO;
using FileFormat.Codecs.Hap;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes every Vidvox Hap stream variant: BC1, BC3, Scaled YCoCg, YCoCg plus BC4 alpha, BC4
/// alpha-only, BC7 and BC6H, wrapped in Hap's sectioned frame format with optional Snappy.
/// </summary>
/// <remarks>
/// <b>There are no P- or B-frames in Hap.</b> Every packet is a complete independently decodable
/// texture image, so every packet this encoder emits is a key frame and decode/presentation order are
/// identical. Hap defines no forward or backward references, motion vectors, reference-picture lists
/// or other inter-frame state to implement.
/// <para/>
/// <b>Texture coding.</b> <c>Hap1</c>, <c>Hap5</c> and <c>HapY</c> use the existing DXT writer,
/// originally ported from FFmpeg's separately MIT-licensed <c>texturedspenc.c</c> and already measured
/// against FFmpeg in both directions. <c>HapA</c> and the alpha image of <c>HapM</c> use the RGTC1/BC4
/// endpoint ramp defined by the RGTC specification. <c>Hap7</c> uses specification-derived BC7 block
/// encoders, and <c>HapH</c> uses specification-derived BC6H block encoders, selecting BC6S for a
/// frame containing any negative finite sample and BC6U otherwise.
/// <para/>
/// <b>Odd dimensions are valid.</b> BC1/3/4/6/7 are 4x4 block formats whose image dimensions need not
/// be multiples of four. The last block is completed here by replicating its right/bottom edge and the
/// container dimensions crop those padding samples again on decode.
/// <para/>
/// <b>Framing and chunks.</b> The default is one chunk, preserving the simple whole-texture form and
/// deterministic output of older callers. <see cref="Create(MediaStreamInfo,int)"/> requests more
/// chunks for parallel second-stage decode. Hap requires equal block-aligned chunks, so if the block
/// count is not divisible by the request the request is reduced to the largest divisor below it,
/// matching the reference encoder. Each resulting chunk independently chooses Snappy or raw storage.
/// <c>HapM</c> applies the same chunk count independently to its colour and alpha textures.
/// </remarks>
public sealed class HapVideoEncoder : IVideoCodecEncoder<HapVideoEncoder> {

  private static readonly CodecTag _Hap1 = CodecTag.FromCharacters("Hap1");
  private static readonly CodecTag _Hap5 = CodecTag.FromCharacters("Hap5");
  private static readonly CodecTag _HapY = CodecTag.FromCharacters("HapY");
  private static readonly CodecTag _HapM = CodecTag.FromCharacters("HapM");
  private static readonly CodecTag _HapA = CodecTag.FromCharacters("HapA");
  private static readonly CodecTag _Hap7 = CodecTag.FromCharacters("Hap7");
  private static readonly CodecTag _HapH = CodecTag.FromCharacters("HapH");

  private const int _BLOCK = 4;

  private const byte _FORMAT_DXT1_RGB = 0x0B;
  private const byte _FORMAT_DXT5_RGBA = 0x0E;
  private const byte _FORMAT_DXT5_SCALED_YCOCG = 0x0F;
  private const byte _FORMAT_BC7_RGBA = 0x0C;
  private const byte _FORMAT_RGTC1_ALPHA = 0x01;
  private const byte _FORMAT_BC6_UNSIGNED = 0x02;
  private const byte _FORMAT_BC6_SIGNED = 0x03;
  private const byte _MULTI_IMAGE = 0x0D;

  private readonly MediaStreamInfo _stream;
  private readonly Variant _variant;
  private readonly int _width;
  private readonly int _height;
  private readonly int _blocksAcross;
  private readonly int _blockRows;
  private readonly int _blockCount;
  private readonly int _chunkCount;

  private HapVideoEncoder(MediaStreamInfo stream, CodecTag tag, Variant variant, int requestedChunkCount) {
    this._variant = variant;
    this._width = stream.Width;
    this._height = stream.Height;
    this._blocksAcross = (stream.Width - 1) / _BLOCK + 1;
    this._blockRows = (stream.Height - 1) / _BLOCK + 1;
    this._blockCount = checked(this._blocksAcross * this._blockRows);
    this._chunkCount = _LimitedChunkCount(this._blockCount, requestedChunkCount);
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
      BitsPerPixel = variant switch {
        Variant.HapA => 8,
        Variant.HapH => 48,
        Variant.Hap1 or Variant.HapY => 24,
        _ => 32,
      },
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "Hap";

  /// <summary>The registry's canonical code; the encoder's static acceptance hook handles the aliases.</summary>
  public static CodecTag Codec => _Hap1;

  /// <summary>The actual number of equal texture chunks each image section will carry.</summary>
  /// <remarks>
  /// This can be lower than the count passed to <see cref="Create(MediaStreamInfo,int)"/> because Hap
  /// chunks end on compressed-texture block boundaries and the reference encoder reduces a request
  /// until it divides the frame's block count exactly.
  /// </remarks>
  public int ChunkCount => this._chunkCount;

  static bool IVideoCodecEncoder<HapVideoEncoder>.Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      return false;

    var codec = stream.Codec;
    return codec.EqualsIgnoringCase(_Hap1) || codec.EqualsIgnoringCase(_Hap5) || codec.EqualsIgnoringCase(_HapY)
      || codec.EqualsIgnoringCase(_HapM) || codec.EqualsIgnoringCase(_HapA) || codec.EqualsIgnoringCase(_Hap7)
      || codec.EqualsIgnoringCase(_HapH);
  }

  /// <summary>Builds an encoder using Hap's traditional one-chunk frame layout.</summary>
  public static HapVideoEncoder Create(MediaStreamInfo stream) => Create(stream, 1);

  /// <summary>Builds an encoder requesting a deterministic number of independently decodable chunks.</summary>
  public static HapVideoEncoder Create(MediaStreamInfo stream, int chunkCount) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Hap can only encode a video stream.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can be coded from.");

    if (chunkCount <= 0)
      throw new ArgumentOutOfRangeException(nameof(chunkCount), "A Hap stream needs at least one chunk per texture.");

    var (tag, variant) = _VariantOf(stream.Codec, stream.Index);
    return new(stream, tag, variant, chunkCount);
  }

  private static (CodecTag Tag, Variant Variant) _VariantOf(CodecTag codec, int index) {
    if (codec == CodecTag.None || codec.EqualsIgnoringCase(_Hap1))
      return (_Hap1, Variant.Hap1);
    if (codec.EqualsIgnoringCase(_Hap5))
      return (_Hap5, Variant.Hap5);
    if (codec.EqualsIgnoringCase(_HapY))
      return (_HapY, Variant.HapY);
    if (codec.EqualsIgnoringCase(_HapM))
      return (_HapM, Variant.HapM);
    if (codec.EqualsIgnoringCase(_HapA))
      return (_HapA, Variant.HapA);
    if (codec.EqualsIgnoringCase(_Hap7))
      return (_Hap7, Variant.Hap7);
    if (codec.EqualsIgnoringCase(_HapH))
      return (_HapH, Variant.HapH);

    throw new NotSupportedException($"Video stream {index} asks for {codec}, which is not a Hap code at all.");
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);

    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"This Hap stream is {this._width}x{this._height}; a picture of {frame.Width}x{frame.Height} cannot be coded into it. "
        + "The picture size is stated by the container and never by the frame.");

    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var data = this._variant switch {
      Variant.HapM => this._WriteHapM(frame),
      Variant.HapH => this._WriteHapH(frame),
      _ => this._WriteImageSection(this.CompressTexture(frame), _PixelFormatCode(this._variant)),
    };

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

  /// <summary>Compresses the variant's primary texture, leaving Hap section framing off.</summary>
  internal byte[] CompressTexture(RawImage frame) => this._variant switch {
    Variant.Hap1 => this._CompressRgbaTexture(frame, HapPixelFormat.Dxt1Rgb),
    Variant.Hap5 => this._CompressRgbaTexture(frame, HapPixelFormat.Dxt5Rgba),
    Variant.HapY or Variant.HapM => this._CompressRgbaTexture(frame, HapPixelFormat.Dxt5ScaledYCoCg),
    Variant.HapA => this._CompressAlphaTexture(frame),
    Variant.Hap7 => this._CompressRgbaTexture(frame, HapPixelFormat.Bc7Rgba),
    Variant.HapH => this._CompressHdrTexture(frame, out _),
    _ => throw new InvalidOperationException("Unknown Hap encoder variant."),
  };

  private byte[] _CompressRgbaTexture(RawImage frame, HapPixelFormat format) {
    var pixels = this._AsRgba(frame);
    var blockBytes = format == HapPixelFormat.Dxt1Rgb ? 8 : 16;
    var texture = new byte[checked(this._blockCount * blockBytes)];
    Span<byte> block = stackalloc byte[64];

    for (var blockRow = 0; blockRow < this._blockRows; ++blockRow)
      for (var blockColumn = 0; blockColumn < this._blocksAcross; ++blockColumn) {
        this._FillRgbaBlock(pixels, blockColumn, blockRow, block);
        var destination = texture.AsSpan((blockRow * this._blocksAcross + blockColumn) * blockBytes, blockBytes);

        switch (format) {
          case HapPixelFormat.Dxt1Rgb:
            HapBlockEncoding.CompressDxt1(block, 16, destination);
            break;
          case HapPixelFormat.Dxt5Rgba:
            HapBlockEncoding.CompressDxt5(block, 16, destination);
            break;
          case HapPixelFormat.Dxt5ScaledYCoCg:
            HapBlockEncoding.CompressScaledYCoCg(block, 16, destination);
            break;
          case HapPixelFormat.Bc7Rgba:
            HapBc7Encoding.Compress(block, destination);
            break;
          default:
            throw new InvalidOperationException($"{format} is not an RGBA-input Hap texture format.");
        }
      }

    return texture;
  }

  private byte[] _CompressAlphaTexture(RawImage frame) {
    if (frame.Format is not (PixelFormat.Gray8 or PixelFormat.Rgba32 or PixelFormat.Rgb24))
      throw new NotSupportedException(
        $"Hap alpha textures are written from Gray8, Rgba32 or Rgb24 pictures; {frame.Format} would require an unrelated conversion first.");

    var texture = new byte[checked(this._blockCount * 8)];
    Span<byte> block = stackalloc byte[16];

    for (var blockRow = 0; blockRow < this._blockRows; ++blockRow)
      for (var blockColumn = 0; blockColumn < this._blocksAcross; ++blockColumn) {
        for (var y = 0; y < 4; ++y) {
          var sourceY = Math.Min(blockRow * 4 + y, this._height - 1);
          for (var x = 0; x < 4; ++x) {
            var sourceX = Math.Min(blockColumn * 4 + x, this._width - 1);
            var pixel = sourceY * this._width + sourceX;
            block[y * 4 + x] = frame.Format switch {
              PixelFormat.Gray8 => frame.PixelData[pixel],
              PixelFormat.Rgba32 => frame.PixelData[pixel * 4 + 3],
              PixelFormat.Rgb24 => 255,
              _ => 0,
            };
          }
        }

        HapBc4Encoding.Compress(block, texture.AsSpan((blockRow * this._blocksAcross + blockColumn) * 8, 8));
      }

    return texture;
  }

  private byte[] _CompressHdrTexture(RawImage frame, out byte formatCode) {
    if (frame.Format != PixelFormat.RgbF16)
      throw new NotSupportedException(
        $"Hap HDR is written from RgbF16 so BC6H receives the decoder's native half-float samples; {frame.Format} would require a conversion first.");

    var source = frame.PixelData;
    var sampleCount = checked(this._width * this._height * 3);
    var isSigned = false;
    for (var sample = 0; sample < sampleCount; ++sample) {
      var bits = _ReadU16(source, sample * 2);
      if ((bits & 0x7C00) == 0x7C00)
        throw new InvalidDataException($"Hap HDR sample {sample} is an infinity or NaN; BC6H carries finite floating-point values only.");
      if ((bits & 0x8000) != 0 && (bits & 0x7FFF) != 0)
        isSigned = true;
    }

    formatCode = isSigned ? _FORMAT_BC6_SIGNED : _FORMAT_BC6_UNSIGNED;
    var texture = new byte[checked(this._blockCount * 16)];
    Span<ushort> block = stackalloc ushort[48];

    for (var blockRow = 0; blockRow < this._blockRows; ++blockRow)
      for (var blockColumn = 0; blockColumn < this._blocksAcross; ++blockColumn) {
        for (var y = 0; y < 4; ++y) {
          var sourceY = Math.Min(blockRow * 4 + y, this._height - 1);
          for (var x = 0; x < 4; ++x) {
            var sourceX = Math.Min(blockColumn * 4 + x, this._width - 1);
            var sourcePixel = (sourceY * this._width + sourceX) * 6;
            var destinationPixel = (y * 4 + x) * 3;
            block[destinationPixel] = _ReadU16(source, sourcePixel);
            block[destinationPixel + 1] = _ReadU16(source, sourcePixel + 2);
            block[destinationPixel + 2] = _ReadU16(source, sourcePixel + 4);
          }
        }

        HapBc6Encoding.Compress(
          block,
          isSigned,
          texture.AsSpan((blockRow * this._blocksAcross + blockColumn) * 16, 16));
      }

    return texture;
  }

  private byte[] _AsRgba(RawImage frame) {
    var count = checked(this._width * this._height);
    var pixels = new byte[checked(count * 4)];
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
          $"Hap1/Hap5/HapY/HapM/Hap7 are written from Rgb24 and Rgba32 pictures; a {frame.Format} picture would have to be converted first.");
    }

    return pixels;
  }

  private void _FillRgbaBlock(ReadOnlySpan<byte> pixels, int blockColumn, int blockRow, Span<byte> block) {
    for (var y = 0; y < 4; ++y) {
      var sourceY = Math.Min(blockRow * 4 + y, this._height - 1);
      for (var x = 0; x < 4; ++x) {
        var sourceX = Math.Min(blockColumn * 4 + x, this._width - 1);
        var from = (sourceY * this._width + sourceX) * 4;
        pixels.Slice(from, 4).CopyTo(block.Slice((y * 4 + x) * 4, 4));
      }
    }
  }

  private byte[] _WriteHapM(RawImage frame) {
    var colour = this._WriteImageSection(
      this._CompressRgbaTexture(frame, HapPixelFormat.Dxt5ScaledYCoCg),
      _FORMAT_DXT5_SCALED_YCOCG);
    var alpha = this._WriteImageSection(this._CompressAlphaTexture(frame), _FORMAT_RGTC1_ALPHA);
    var payload = new byte[checked(colour.Length + alpha.Length)];
    colour.CopyTo(payload, 0);
    alpha.CopyTo(payload, colour.Length);
    return HapFrameEncoding.WriteLongSection(_MULTI_IMAGE, payload);
  }

  private byte[] _WriteHapH(RawImage frame) {
    var texture = this._CompressHdrTexture(frame, out var formatCode);
    return this._WriteImageSection(texture, formatCode);
  }

  private byte[] _WriteImageSection(byte[] texture, byte formatCode)
    => HapFrameEncoding.WriteImageSection(texture, formatCode, this._chunkCount);

  private static int _LimitedChunkCount(int blockCount, int requested) {
    var result = Math.Min(blockCount, requested);
    while (blockCount % result != 0)
      --result;
    return result;
  }

  private static byte _PixelFormatCode(Variant variant) => variant switch {
    Variant.Hap1 => _FORMAT_DXT1_RGB,
    Variant.Hap5 => _FORMAT_DXT5_RGBA,
    Variant.HapY => _FORMAT_DXT5_SCALED_YCOCG,
    Variant.HapA => _FORMAT_RGTC1_ALPHA,
    Variant.Hap7 => _FORMAT_BC7_RGBA,
    _ => throw new InvalidOperationException($"{variant} does not have one fixed single-image format code."),
  };

  private static ushort _ReadU16(ReadOnlySpan<byte> data, int offset)
    => (ushort)(data[offset] | (data[offset + 1] << 8));

  private enum Variant {
    Hap1,
    Hap5,
    HapY,
    HapM,
    HapA,
    Hap7,
    HapH,
  }
}
