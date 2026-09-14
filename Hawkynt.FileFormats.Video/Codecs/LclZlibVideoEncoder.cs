using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using FileFormat.Bmp;
using FileFormat.Codecs.Lcl;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes the ZLIB variant of the Lossless Codec Library (LCL): one independently decodable intra
/// picture, optionally transformed to one of LCL's historical YUV layouts, delta-filtered, and split
/// into the format's two independently compressed zlib sections.
/// </summary>
/// <remarks>
/// The default overload remains FFmpeg-compatible RGB24: packed BGR rows, bottom-up, one zlib stream,
/// no predictor. The explicit overload exposes the additional modes understood by the original LCL
/// decoder and by FFmpeg's compatible decoder: YUV 1:1:1, 4:2:2, 4:1:1, 2:1:1 and 4:2:0, the
/// so-called PNG predictor, and the two-stream multithread wrapper.
/// <para/>
/// The public trailer fields and mode numbers come from Roberto Togni's published LCL notes. The YUV
/// byte layouts, predictor arithmetic and split wrapper are the mathematical inverse of FFmpeg's
/// <c>libavcodec/lcldec.c</c>, copyright (c) 2002-2004 Roberto Togni, LGPL-2.1-or-later. This
/// adaptation is distributed by PNGCrushCS under LGPL-3.0-or-later.
/// <para/>
/// Every coded packet remains a key frame. "Multithread" does not introduce temporal prediction: it
/// prepends two little-endian lengths and stores two independent RFC 1950 streams whose decompressed
/// bytes are concatenated before prediction and colour unpacking. The historical wrapper states one
/// decompressed length for both halves, so an odd coded byte count cannot be represented by it and is
/// refused rather than truncated.
/// <para/>
/// LCL's predictor is not PNG's filter algorithm despite the historical name. It stores modular
/// differences from the previous component value on the same coded row. RGB24 and YUV111 preserve the
/// first three-byte sample literally; the subsampled layouts start each component accumulator at zero,
/// exactly as the compatible decoder reconstructs them.
/// <para/>
/// LCL carries no colourimetry metadata. Native eight-bit YUV input in the selected sampling is coded
/// sample-for-sample; other accepted input is converted through the package's full-range BT.601 path,
/// matching the equations documented for LCL. Selecting a subsampled YUV image type is therefore an
/// explicit colour-space/subsampling choice and is not lossless with respect to arbitrary RGB input.
/// The RGB24 default keeps the previous stricter behaviour and refuses any input that cannot become
/// eight-bit RGB without changing samples.
/// <para/>
/// Historical YUV422 and YUV411 decoders tolerate widths that end in a partial four-pixel group by
/// synthesising the final chroma sample, but the omitted luma samples are not present in the bitstream.
/// This writer therefore requires widths divisible by four for those modes; deliberately dropping source
/// pixels would contradict the codec's lossless contract.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class LclZlibVideoEncoder : IVideoCodecEncoder<LclZlibVideoEncoder> {

  /// <summary>The image-type byte in LCL's extended BITMAPINFOHEADER.</summary>
  public enum ImageType : byte {
    /// <summary>Three samples per pixel; exposed canonically as planar YUV 4:4:4.</summary>
    Yuv111 = 0,
    /// <summary>Four luma bytes followed by two U and two V bytes per four pixels.</summary>
    Yuv422 = 1,
    /// <summary>Bottom-up packed BGR24.</summary>
    Rgb24 = 2,
    /// <summary>Four luma bytes followed by one U and one V byte per four pixels.</summary>
    Yuv411 = 3,
    /// <summary>Two luma bytes followed by one U and one V byte per two pixels.</summary>
    Yuv211 = 4,
    /// <summary>Two luma samples from each of two rows followed by one U and one V sample.</summary>
    Yuv420 = 5,
  }

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("ZLIB");

  private const byte _CODEC_ZLIB = 3;

  /// <summary>zlib's conventional default level; the level byte is informational to decoders.</summary>
  private const byte _COMPRESSION_LEVEL = 6;

  private static readonly RawImageColorInfo _YuvColorInfo = new() {
    Range = RawColorRange.Full,
    Matrix = RawMatrixCoefficients.Bt601,
    ChromaLocation = RawChromaLocation.Unspecified,
  };

  private readonly MediaStreamInfo _stream;
  private readonly int _width;
  private readonly int _height;
  private readonly ImageType _imageType;
  private readonly PixelFormat _pixelFormat;
  private readonly bool _pngFiltered;
  private readonly bool _multithreaded;
  private readonly int _codedSize;

  private LclZlibVideoEncoder(
    MediaStreamInfo stream,
    ImageType imageType,
    PixelFormat pixelFormat,
    bool pngFiltered,
    bool multithreaded,
    int codedSize
  ) {
    this._width = stream.Width;
    this._height = stream.Height;
    this._imageType = imageType;
    this._pixelFormat = pixelFormat;
    this._pngFiltered = pngFiltered;
    this._multithreaded = multithreaded;
    this._codedSize = codedSize;

    var flags = (byte)0;
    if (multithreaded)
      flags |= LclHeader.MultithreadFlag;
    if (pngFiltered)
      flags |= LclHeader.PngFilterFlag;

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
      CodecPrivateData = _PrivateData(stream.Width, stream.Height, imageType, flags, codedSize),
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "LCL ZLIB";

  public static CodecTag Codec => _Tag;

  /// <summary>Builds the conservative RGB24/single-stream encoder used before historical modes were exposed.</summary>
  public static LclZlibVideoEncoder Create(MediaStreamInfo stream)
    => Create(stream, ImageType.Rgb24, pngFiltered: false, multithreaded: false);

  /// <summary>
  /// Builds an encoder for one of LCL's historical image layouts and optional packet transforms.
  /// </summary>
  /// <param name="stream">Video stream geometry and timing.</param>
  /// <param name="imageType">The colour packing written into the LCL trailer.</param>
  /// <param name="pngFiltered">Apply LCL's historical per-row modular-difference predictor before zlib.</param>
  /// <param name="multithreaded">Store two independently compressed, equally sized coded sections.</param>
  public static LclZlibVideoEncoder Create(
    MediaStreamInfo stream,
    ImageType imageType,
    bool pngFiltered = false,
    bool multithreaded = false
  ) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("LCL ZLIB can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"An LCL ZLIB encoder needs positive picture dimensions before the muxer is created; {stream.Width}x{stream.Height} was supplied.");

    var pixelFormat = imageType switch {
      ImageType.Yuv111 => PixelFormat.Yuv444P8,
      ImageType.Yuv422 => PixelFormat.Yuv422P8,
      ImageType.Rgb24 => PixelFormat.Bgr24,
      ImageType.Yuv411 => PixelFormat.Yuv411P8,
      ImageType.Yuv211 => PixelFormat.Yuv422P8,
      ImageType.Yuv420 => PixelFormat.Yuv420P8,
      _ => throw new ArgumentOutOfRangeException(nameof(imageType), imageType, "LCL defines image types 0 through 5."),
    };

    if (imageType is ImageType.Yuv422 or ImageType.Yuv411 && (stream.Width & 3) != 0)
      throw new NotSupportedException(
        $"LCL {imageType} writes complete four-pixel groups only; width {stream.Width} would discard trailing luma samples.");

    if (imageType == ImageType.Yuv211 && (stream.Width & 1) != 0)
      throw new NotSupportedException(
        $"LCL YUV211 writes two luma samples per chroma pair; width {stream.Width} must be even.");

    if (imageType == ImageType.Yuv420 && ((stream.Width | stream.Height) & 1) != 0)
      throw new NotSupportedException(
        $"LCL YUV420 writes 2x2 luma blocks; {stream.Width}x{stream.Height} must have even dimensions.");

    long rowBytes = (long)stream.Width * 3;
    var paddedRgbStride = (rowBytes + 3) & ~3L;
    var codedSizeLong = imageType switch {
      ImageType.Yuv111 => rowBytes * stream.Height,
      ImageType.Yuv422 => (long)stream.Width * stream.Height * 2,
      ImageType.Rgb24 => (multithreaded ? paddedRgbStride : rowBytes) * stream.Height,
      ImageType.Yuv411 => (long)stream.Width * stream.Height * 3 / 2,
      ImageType.Yuv211 => (long)stream.Width * stream.Height * 2,
      ImageType.Yuv420 => (long)stream.Width * stream.Height * 3 / 2,
      _ => throw new InvalidOperationException(),
    };

    if (codedSizeLong > int.MaxValue)
      throw new NotSupportedException(
        $"An LCL {imageType} frame at {stream.Width}x{stream.Height} needs {codedSizeLong} coded bytes, beyond one managed array.");

    if (multithreaded && (codedSizeLong & 1) != 0)
      throw new NotSupportedException(
        $"LCL's two-stream wrapper gives both sections the same decoded length; this {imageType} frame has an odd coded size of {codedSizeLong} bytes.");

    if (pngFiltered && multithreaded && imageType == ImageType.Rgb24 && rowBytes != paddedRgbStride)
      throw new NotSupportedException(
        "LCL's RGB predictor walks packed width*3 rows while its split decoder selects four-byte-padded RGB rows; "
        + $"the combination is interoperable only when width*3 is already four-byte aligned (width {stream.Width} is not).");

    return new(stream, imageType, pixelFormat, pngFiltered, multithreaded, (int)codedSizeLong);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    var picture = this._Prepare(frame);
    var coded = this._Pack(picture);
    if (coded.Length != this._codedSize)
      throw new InvalidDataException(
        $"LCL {this._imageType} packing produced {coded.Length} byte(s), but this stream requires {this._codedSize}.");

    if (this._pngFiltered)
      this._ApplyPngFilter(coded);

    var data = this._multithreaded ? _DeflateSplit(coded) : _Deflate(coded);
    packet = new(
      this._stream.Index,
      data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: true);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  private RawImage _Prepare(RawImage frame) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"{CodecName} geometry is fixed at {this._width}x{this._height} for the life of the stream; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    if (this._imageType == ImageType.Rgb24)
      return LosslessEncoderInput.Prepare(frame, PixelFormat.Bgr24, this._width, this._height, CodecName);

    if (frame.Format == this._pixelFormat)
      return frame;

    if (frame.IsPlanarYuv) {
      if (RawImage.YuvBitDepth(frame.Format) != 8)
        throw new NotSupportedException(
          $"{CodecName} historical YUV modes carry eight-bit samples; {frame.Format} is refused rather than quantised.");
      return FastRawImageConverter.Convert(frame, this._pixelFormat, _YuvColorInfo);
    }

    var rgb = LosslessEncoderInput.Prepare(frame, PixelFormat.Bgr24, this._width, this._height, CodecName);
    return FastRawImageConverter.Convert(rgb, this._pixelFormat, _YuvColorInfo);
  }

  private byte[] _Pack(RawImage picture) => this._imageType switch {
    ImageType.Yuv111 => this._PackYuv111(picture),
    ImageType.Yuv422 => this._PackYuv422(picture),
    ImageType.Rgb24 => this._PackRgb24(picture),
    ImageType.Yuv411 => this._PackYuv411(picture),
    ImageType.Yuv211 => this._PackYuv211(picture),
    ImageType.Yuv420 => this._PackYuv420(picture),
    _ => throw new InvalidOperationException(),
  };

  private byte[] _PackRgb24(RawImage picture) {
    var rowBytes = checked(this._width * 3);
    var stride = this._multithreaded ? (rowBytes + 3) & ~3 : rowBytes;
    var coded = new byte[checked(stride * this._height)];
    for (var codedRow = 0; codedRow < this._height; ++codedRow) {
      var sourceRow = this._height - 1 - codedRow;
      picture.PixelData.AsSpan(sourceRow * rowBytes, rowBytes)
        .CopyTo(coded.AsSpan(codedRow * stride, rowBytes));
    }
    return coded;
  }

  private byte[] _PackYuv111(RawImage picture) {
    var y = picture.GetPlaneData(0);
    var u = picture.GetPlaneData(1);
    var v = picture.GetPlaneData(2);
    var coded = new byte[this._codedSize];
    var position = 0;

    for (var codedRow = 0; codedRow < this._height; ++codedRow) {
      var sourceRow = this._height - 1 - codedRow;
      var rowOffset = sourceRow * this._width;
      for (var x = 0; x < this._width; ++x) {
        var sample = rowOffset + x;
        coded[position++] = y[sample];
        coded[position++] = unchecked((byte)(u[sample] - 128));
        coded[position++] = unchecked((byte)(v[sample] - 128));
      }
    }

    return coded;
  }

  private byte[] _PackYuv422(RawImage picture) {
    var y = picture.GetPlaneData(0);
    var u = picture.GetPlaneData(1);
    var v = picture.GetPlaneData(2);
    var chromaWidth = this._width / 2;
    var coded = new byte[this._codedSize];
    var position = 0;

    for (var codedRow = 0; codedRow < this._height; ++codedRow) {
      var sourceRow = this._height - 1 - codedRow;
      var yRow = sourceRow * this._width;
      var cRow = sourceRow * chromaWidth;
      for (var x = 0; x < this._width; x += 4) {
        y.Slice(yRow + x, 4).CopyTo(coded.AsSpan(position, 4));
        position += 4;
        var c = cRow + (x >> 1);
        coded[position++] = unchecked((byte)(u[c] - 128));
        coded[position++] = unchecked((byte)(u[c + 1] - 128));
        coded[position++] = unchecked((byte)(v[c] - 128));
        coded[position++] = unchecked((byte)(v[c + 1] - 128));
      }
    }

    return coded;
  }

  private byte[] _PackYuv411(RawImage picture) {
    var y = picture.GetPlaneData(0);
    var u = picture.GetPlaneData(1);
    var v = picture.GetPlaneData(2);
    var chromaWidth = this._width / 4;
    var coded = new byte[this._codedSize];
    var position = 0;

    for (var codedRow = 0; codedRow < this._height; ++codedRow) {
      var sourceRow = this._height - 1 - codedRow;
      var yRow = sourceRow * this._width;
      var cRow = sourceRow * chromaWidth;
      for (var x = 0; x < this._width; x += 4) {
        y.Slice(yRow + x, 4).CopyTo(coded.AsSpan(position, 4));
        position += 4;
        var c = cRow + (x >> 2);
        coded[position++] = unchecked((byte)(u[c] - 128));
        coded[position++] = unchecked((byte)(v[c] - 128));
      }
    }

    return coded;
  }

  private byte[] _PackYuv211(RawImage picture) {
    var y = picture.GetPlaneData(0);
    var u = picture.GetPlaneData(1);
    var v = picture.GetPlaneData(2);
    var chromaWidth = this._width / 2;
    var coded = new byte[this._codedSize];
    var position = 0;

    for (var codedRow = 0; codedRow < this._height; ++codedRow) {
      var sourceRow = this._height - 1 - codedRow;
      var yRow = sourceRow * this._width;
      var cRow = sourceRow * chromaWidth;
      for (var x = 0; x < this._width; x += 2) {
        coded[position++] = y[yRow + x];
        coded[position++] = y[yRow + x + 1];
        var c = cRow + (x >> 1);
        coded[position++] = unchecked((byte)(u[c] - 128));
        coded[position++] = unchecked((byte)(v[c] - 128));
      }
    }

    return coded;
  }

  private byte[] _PackYuv420(RawImage picture) {
    var y = picture.GetPlaneData(0);
    var u = picture.GetPlaneData(1);
    var v = picture.GetPlaneData(2);
    var chromaWidth = this._width / 2;
    var chromaHeight = this._height / 2;
    var coded = new byte[this._codedSize];
    var position = 0;

    for (var codedPair = 0; codedPair < chromaHeight; ++codedPair) {
      var bottomRow = this._height - 1 - codedPair * 2;
      var topRow = bottomRow - 1;
      var chromaRow = chromaHeight - 1 - codedPair;
      for (var x = 0; x < this._width; x += 2) {
        coded[position++] = y[bottomRow * this._width + x];
        coded[position++] = y[bottomRow * this._width + x + 1];
        coded[position++] = y[topRow * this._width + x];
        coded[position++] = y[topRow * this._width + x + 1];
        var c = chromaRow * chromaWidth + (x >> 1);
        coded[position++] = unchecked((byte)(u[c] - 128));
        coded[position++] = unchecked((byte)(v[c] - 128));
      }
    }

    return coded;
  }

  private void _ApplyPngFilter(byte[] data) {
    switch (this._imageType) {
      case ImageType.Yuv111:
      case ImageType.Rgb24:
        for (var row = 0; row < this._height; ++row) {
          var position = row * this._width * 3;
          var previousFirst = data[position++];
          var previousPair = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(position, 2));
          position += 2;
          for (var column = 1; column < this._width; ++column) {
            var currentFirst = data[position];
            data[position] = unchecked((byte)(previousFirst - currentFirst));
            previousFirst = currentFirst;

            var currentPair = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(position + 1, 2));
            BinaryPrimitives.WriteUInt16LittleEndian(
              data.AsSpan(position + 1, 2),
              unchecked((ushort)(previousPair - currentPair)));
            previousPair = currentPair;
            position += 3;
          }
        }
        break;

      case ImageType.Yuv422: {
        var position = 0;
        for (var row = 0; row < this._height; ++row) {
          byte previousY = 0, previousU = 0, previousV = 0;
          for (var group = 0; group < this._width / 4; ++group) {
            for (var i = 0; i < 4; ++i)
              _Difference(data, position + i, ref previousY);
            _Difference(data, position + 4, ref previousU);
            _Difference(data, position + 5, ref previousU);
            _Difference(data, position + 6, ref previousV);
            _Difference(data, position + 7, ref previousV);
            position += 8;
          }
        }
        break;
      }

      case ImageType.Yuv411: {
        var position = 0;
        for (var row = 0; row < this._height; ++row) {
          byte previousY = 0, previousU = 0, previousV = 0;
          for (var group = 0; group < this._width / 4; ++group) {
            for (var i = 0; i < 4; ++i)
              _Difference(data, position + i, ref previousY);
            _Difference(data, position + 4, ref previousU);
            _Difference(data, position + 5, ref previousV);
            position += 6;
          }
        }
        break;
      }

      case ImageType.Yuv211:
        for (var row = 0; row < this._height; ++row) {
          var position = row * this._width * 2;
          byte previousY = 0, previousU = 0, previousV = 0;
          for (var group = 0; group < this._width / 2; ++group) {
            _Difference(data, position, ref previousY);
            _Difference(data, position + 1, ref previousY);
            _Difference(data, position + 2, ref previousU);
            _Difference(data, position + 3, ref previousV);
            position += 4;
          }
        }
        break;

      case ImageType.Yuv420:
        for (var rowPair = 0; rowPair < this._height / 2; ++rowPair) {
          var position = rowPair * this._width * 3;
          byte previousY0 = 0, previousY1 = 0, previousU = 0, previousV = 0;
          for (var group = 0; group < this._width / 2; ++group) {
            _Difference(data, position, ref previousY0);
            _Difference(data, position + 1, ref previousY0);
            _Difference(data, position + 2, ref previousY1);
            _Difference(data, position + 3, ref previousY1);
            _Difference(data, position + 4, ref previousU);
            _Difference(data, position + 5, ref previousV);
            position += 6;
          }
        }
        break;
    }
  }

  private static void _Difference(byte[] data, int position, ref byte previous) {
    var current = data[position];
    data[position] = unchecked((byte)(previous - current));
    previous = current;
  }

  private static byte[] _DeflateSplit(byte[] coded) {
    var sectionLength = coded.Length / 2;
    var first = _Deflate(coded.AsSpan(0, sectionLength));
    var second = _Deflate(coded.AsSpan(sectionLength, sectionLength));
    var packet = new byte[checked(8 + first.Length + second.Length)];
    BinaryPrimitives.WriteUInt32LittleEndian(packet, (uint)first.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), (uint)sectionLength);
    first.CopyTo(packet.AsSpan(8));
    second.CopyTo(packet.AsSpan(8 + first.Length));
    return packet;
  }

  private static byte[] _Deflate(ReadOnlySpan<byte> coded) {
    using var output = new MemoryStream();
    using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
      zlib.Write(coded);
    return output.ToArray();
  }

  /// <summary>A standard <c>BITMAPINFOHEADER</c> followed by LCL's eight-byte private trailer.</summary>
  private static byte[] _PrivateData(
    int width,
    int height,
    ImageType imageType,
    byte flags,
    int imageSize
  ) {
    var data = new byte[BitmapInfoHeader.StructSize + LclHeader.ExtraBytes];
    var span = data.AsSpan();
    BinaryPrimitives.WriteInt32LittleEndian(span, BitmapInfoHeader.StructSize);
    BinaryPrimitives.WriteInt32LittleEndian(span[4..], width);
    BinaryPrimitives.WriteInt32LittleEndian(span[8..], height);
    BinaryPrimitives.WriteInt16LittleEndian(span[12..], 1);
    BinaryPrimitives.WriteInt16LittleEndian(span[14..], 24);
    BinaryPrimitives.WriteUInt32LittleEndian(span[16..], _Tag.Value);
    BinaryPrimitives.WriteInt32LittleEndian(span[20..], imageSize);

    var extra = span[BitmapInfoHeader.StructSize..];
    extra[0] = 4;
    extra[4] = (byte)imageType;
    extra[5] = _COMPRESSION_LEVEL;
    extra[6] = flags;
    extra[7] = _CODEC_ZLIB;
    return data;
  }
}
