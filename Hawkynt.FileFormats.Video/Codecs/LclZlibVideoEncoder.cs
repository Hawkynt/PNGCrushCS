using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using FileFormat.Bmp;
using FileFormat.Codecs.Lcl;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes the ZLIB variant of the Lossless Codec Library (LCL): every picture as one complete zlib
/// stream of its RGB24 rows, bottom row first, with the compressor started fresh for each frame so
/// every packet is a key frame that decodes on its own.
/// </summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/lclenc.c</c>, copyright (c) 2002-2004 Roberto Togni,
/// LGPL-2.1-or-later; this adaptation is distributed with PNGCrushCS under LGPL-3.0-or-later.
/// <para/>
/// <b>What is written.</b> The stream's <c>strf</c> is a standard 40-byte <c>BITMAPINFOHEADER</c>
/// naming 24 bits a pixel and <c>ZLIB</c> as its compression, followed by the eight-byte trailer
/// <see cref="LclHeader"/> describes: the format's own always-<c>[4,0,0,0]</c> field, image type 2
/// (RGB24), the zlib level, no flags, and codec 3 (ZLIB). Every packet is one RFC 1950 zlib stream
/// that inflates to exactly <c>width × 3 × height</c> bytes — rows packed tight with no four-byte
/// padding, exactly as FFmpeg's own encoder writes them and as its decoder sizes its buffer, bottom
/// row first, three bytes B, G, R a pixel. This package's own decoder takes packed and padded rows
/// alike, so either would have decoded here; packed is the one the other implementation in
/// existence also reads without complaint.
/// <para/>
/// <b>The compression level byte is the level actually used</b> rather than the format's "normal"
/// code of −1, because FFmpeg's decoder treats a −1-level RGB24 packet whose compressed size happens
/// to equal the picture's raw size as uncompressed pixels. Naming the real level closes that trap
/// for the cost of one byte that every decoder measured ignores anyway.
/// <para/>
/// <b>What is accepted.</b> Any picture that converts to eight-bit RGB without changing a sample —
/// RGB and BGR with or without alpha, grey, palettised, 5-6-5 — with alpha dropped since the format
/// has no place for it. Anything deeper than eight bits, floating-point, or YUV is refused by name
/// rather than quantised, this being a lossless codec. A picture whose size differs from the one the
/// stream was created for is refused too: the geometry is in the stream header and the decoder
/// sizes every frame from it.
/// </remarks>
public sealed class LclZlibVideoEncoder : IVideoCodecEncoder<LclZlibVideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("ZLIB");

  private const byte _IMAGE_TYPE_RGB24 = 2;
  private const byte _CODEC_ZLIB = 3;

  /// <summary>zlib's own default level, which is what <see cref="CompressionLevel.Optimal"/> selects.</summary>
  private const byte _COMPRESSION_LEVEL = 6;

  private readonly MediaStreamInfo _stream;
  private readonly int _width;
  private readonly int _height;

  private LclZlibVideoEncoder(MediaStreamInfo stream) {
    this._width = stream.Width;
    this._height = stream.Height;
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
      BitsPerPixel = 24,
      CodecPrivateData = _PrivateData(stream.Width, stream.Height),
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "LCL ZLIB";

  public static CodecTag Codec => _Tag;

  public static LclZlibVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("LCL ZLIB can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"An LCL ZLIB encoder needs positive picture dimensions before the muxer is created; {stream.Width}x{stream.Height} was supplied.");

    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    var picture = LosslessEncoderInput.Prepare(frame, PixelFormat.Bgr24, this._width, this._height, CodecName);

    var rowBytes = this._width * 3;
    using var output = new MemoryStream();
    using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
      for (var row = this._height - 1; row >= 0; --row)
        zlib.Write(picture.PixelData, row * rowBytes, rowBytes);

    packet = new(
      this._stream.Index,
      output.ToArray(),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: true);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  /// <summary>A standard <c>BITMAPINFOHEADER</c> with LCL's eight-byte trailer behind it, exactly
  /// the bytes the decoder reads its image type, flags and codec out of.</summary>
  private static byte[] _PrivateData(int width, int height) {
    var data = new byte[BitmapInfoHeader.StructSize + LclHeader.ExtraBytes];
    var span = data.AsSpan();
    BinaryPrimitives.WriteInt32LittleEndian(span, BitmapInfoHeader.StructSize);
    BinaryPrimitives.WriteInt32LittleEndian(span[4..], width);
    BinaryPrimitives.WriteInt32LittleEndian(span[8..], height);
    BinaryPrimitives.WriteInt16LittleEndian(span[12..], 1);
    BinaryPrimitives.WriteInt16LittleEndian(span[14..], 24);
    BinaryPrimitives.WriteUInt32LittleEndian(span[16..], _Tag.Value);
    BinaryPrimitives.WriteInt32LittleEndian(span[20..], checked(width * 3 * height));

    var extra = span[BitmapInfoHeader.StructSize..];
    extra[0] = 4; // The format's own "unknown" field, always [4, 0, 0, 0].
    extra[4] = _IMAGE_TYPE_RGB24;
    extra[5] = _COMPRESSION_LEVEL;
    extra[6] = 0; // No flags: single-threaded, no null frames, no PNG filter.
    extra[7] = _CODEC_ZLIB;
    return data;
  }
}
