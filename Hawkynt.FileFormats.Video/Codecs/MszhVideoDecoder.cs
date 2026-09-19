using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.Lcl;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes the MSZH variant of the Lossless Codec Library (LCL): one independently coded intra
/// picture whose four-byte groups are either literals or backward copies from bytes already rebuilt
/// in the same section.
/// </summary>
/// <remarks>
/// The frame wrapper and colour layouts come from Roberto Togni's LCL notes and FFmpeg's compatible
/// LGPL-2.1-or-later decoder. MSZH itself has no P- or B-pictures and no cross-frame references: the
/// optional null-frame flag only allows an AVI writer to omit an unchanged picture at container level.
/// <para/>
/// A command mask is consumed most-significant bit first. A zero bit copies four source bytes; a one
/// bit reads a little-endian sixteen-bit word whose low eleven bits are the backward distance and
/// whose high five bits plus one are the number of four-byte groups to reproduce. Back-references
/// may overlap their destination. Distance zero is defined as zero fill, matching the reference
/// decoder's defensive behaviour.
/// <para/>
/// LCL image types 0 through 5 are all supported: YUV 4:4:4 (called YUV111 by LCL), YUV 4:2:2,
/// BGR24, YUV 4:1:1, YUV 2:1:1 and YUV 4:2:0. The package has no native 4:1:1 pixel format, so that
/// one is expanded sample-exactly to <see cref="PixelFormat.Yuv444P8"/> by repeating each chroma
/// sample across its four luma positions; the remaining YUV layouts are returned natively. LCL stores
/// chroma as signed differences biased by 128 on decode and stores all rows bottom first.
/// <para/>
/// The original codec can tag a stream as compressed while storing a complete raw RGB24 or YUV111
/// picture. It can also split one compressed frame into two independently coded equal-length sections;
/// the packet then starts with the first compressed length and one section's decoded length, both
/// little-endian 32-bit values. Both interoperable forms are handled here.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class MszhVideoDecoder : IVideoCodecDecoder<MszhVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("MSZH");
  private static readonly RawImageColorInfo _YuvColor = new() {
    Range = RawColorRange.Full,
    Matrix = RawMatrixCoefficients.Bt601,
  };

  private const byte _IMAGE_TYPE_YUV111 = 0;
  private const byte _IMAGE_TYPE_YUV422 = 1;
  private const byte _IMAGE_TYPE_RGB24 = 2;
  private const byte _IMAGE_TYPE_YUV411 = 3;
  private const byte _IMAGE_TYPE_YUV211 = 4;
  private const byte _IMAGE_TYPE_YUV420 = 5;
  private const sbyte _COMPRESSION_MSZH = 0;
  private const sbyte _COMPRESSION_NONE = 1;
  private const byte _CODEC_MSZH = 1;

  private readonly int _width;
  private readonly int _height;
  private readonly int _streamIndex;
  private readonly byte _imageType;
  private readonly sbyte _compression;
  private readonly bool _multithreaded;
  private readonly int _decodedSize;
  private readonly int _packedRgbStride;
  private readonly int _paddedRgbStride;

  private MszhVideoDecoder(
    int width,
    int height,
    int streamIndex,
    byte imageType,
    sbyte compression,
    bool multithreaded,
    int decodedSize,
    int packedRgbStride = 0,
    int paddedRgbStride = 0
  ) {
    this._width = width;
    this._height = height;
    this._streamIndex = streamIndex;
    this._imageType = imageType;
    this._compression = compression;
    this._multithreaded = multithreaded;
    this._decodedSize = decodedSize;
    this._packedRgbStride = packedRgbStride;
    this._paddedRgbStride = paddedRgbStride;
  }

  public static string CodecName => "LCL MSZH";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static MszhVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can "
        + "be decoded into.");

    var format = stream.CodecPrivateData.Span;
    if (format.Length < BitmapInfoHeader.StructSize + LclHeader.ExtraBytes)
      throw new InvalidDataException(
        $"Video stream {stream.Index} carries {format.Length} byte(s) of stream format, where LCL's BITMAPINFOHEADER "
        + $"and eight-byte trailer need at least {BitmapInfoHeader.StructSize + LclHeader.ExtraBytes}.");

    var header = LclHeader.Read(format[BitmapInfoHeader.StructSize..]);
    if (header.Codec != _CODEC_MSZH)
      throw new InvalidDataException(
        $"Video stream {stream.Index} is tagged MSZH but its LCL trailer identifies codec {header.Codec}, not MSZH "
        + $"({_CODEC_MSZH}).");

    if (header.Compression is not (_COMPRESSION_MSZH or _COMPRESSION_NONE))
      throw new NotSupportedException(
        $"Video stream {stream.Index} states unsupported MSZH compression mode {header.Compression}; known modes are "
        + $"{_COMPRESSION_MSZH} (MSZH) and {_COMPRESSION_NONE} (uncompressed).");

    var width = stream.Width;
    var height = stream.Height;
    var packedRgbStride = 0L;
    var paddedRgbStride = 0L;
    long decodedSize;

    switch (header.ImageType) {
      case _IMAGE_TYPE_YUV111:
        decodedSize = (long)width * height * 3;
        break;
      case _IMAGE_TYPE_YUV422:
        if (width < 4)
          throw new InvalidDataException(
            $"Video stream {stream.Index} states LCL YUV 4:2:2 at width {width}; the format codes chroma and luma in four-pixel groups.");
        decodedSize = (long)(width & ~3) * height * 2;
        break;
      case _IMAGE_TYPE_RGB24:
        packedRgbStride = (long)width * 3;
        paddedRgbStride = (packedRgbStride + 3) & ~3L;
        decodedSize = paddedRgbStride * height;
        break;
      case _IMAGE_TYPE_YUV411:
        if (width < 4)
          throw new InvalidDataException(
            $"Video stream {stream.Index} states LCL YUV 4:1:1 at width {width}; the format codes chroma and luma in four-pixel groups.");
        decodedSize = (long)(width & ~3) * height * 3 / 2;
        break;
      case _IMAGE_TYPE_YUV211:
        if ((width & 1) != 0)
          throw new InvalidDataException(
            $"Video stream {stream.Index} states LCL YUV 2:1:1 at odd width {width}; each coded chroma pair covers two columns.");
        decodedSize = (long)width * height * 2;
        break;
      case _IMAGE_TYPE_YUV420:
        if (((width | height) & 1) != 0)
          throw new InvalidDataException(
            $"Video stream {stream.Index} states LCL YUV 4:2:0 at {width}x{height}; both dimensions must be even.");
        decodedSize = (long)width * height * 3 / 2;
        break;
      default:
        throw new NotSupportedException(
          $"Video stream {stream.Index} states unsupported LCL image type {header.ImageType}; MSZH defines image types 0 through 5.");
    }

    if (decodedSize > int.MaxValue || packedRgbStride > int.MaxValue || paddedRgbStride > int.MaxValue)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a {width}x{height} LCL image whose decoded frame is too large to hold in one managed byte array.");

    return new(
      width,
      height,
      stream.Index,
      header.ImageType,
      header.Compression,
      header.Multithreaded,
      (int)decodedSize,
      (int)packedRgbStride,
      (int)paddedRgbStride
    );
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var source = packet.Data.Span;
    byte[] decoded;
    var rgbRowStride = this._paddedRgbStride;

    var compressedRawFallback = this._compression == _COMPRESSION_MSZH
      && this._imageType is _IMAGE_TYPE_RGB24 or _IMAGE_TYPE_YUV111
      && source.Length == this._decodedSize;

    if (this._compression == _COMPRESSION_NONE || compressedRawFallback) {
      (decoded, rgbRowStride) = this._ReadRaw(source);
    } else {
      decoded = new byte[this._decodedSize];
      if (this._multithreaded)
        this._DecodeSplit(source, decoded);
      else
        _Decompress(source, decoded, 0, decoded.Length, this._streamIndex);
    }

    frame = this._Unpack(decoded, rgbRowStride);
    return true;
  }

  private (byte[] Data, int RgbRowStride) _ReadRaw(ReadOnlySpan<byte> source) {
    if (this._imageType != _IMAGE_TYPE_RGB24) {
      if (source.Length < this._decodedSize)
        throw new InvalidDataException(
          $"Video stream {this._streamIndex} carries an uncompressed MSZH packet of {source.Length} byte(s), where its "
          + $"LCL image needs at least {this._decodedSize} byte(s).");

      return (source[..this._decodedSize].ToArray(), 0);
    }

    var packedBytes = checked(this._packedRgbStride * this._height);
    var paddedBytes = this._decodedSize;
    if (source.Length < packedBytes)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an uncompressed MSZH packet of {source.Length} byte(s), where its "
        + $"RGB24 picture needs at least {packedBytes} byte(s).");

    var rowStride = source.Length >= paddedBytes ? this._paddedRgbStride : this._packedRgbStride;
    return (source[..checked(rowStride * this._height)].ToArray(), rowStride);
  }

  private void _DecodeSplit(ReadOnlySpan<byte> source, byte[] destination) {
    if (source.Length < 8)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries a split MSZH packet shorter than its eight-byte split header.");

    var firstCompressedLength = BinaryPrimitives.ReadUInt32LittleEndian(source);
    var sectionDecodedLength = BinaryPrimitives.ReadUInt32LittleEndian(source[4..]);
    if (firstCompressedLength > int.MaxValue || sectionDecodedLength > int.MaxValue)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries a split MSZH packet whose section lengths do not fit in memory.");

    var firstInput = (int)firstCompressedLength;
    var sectionOutput = (int)sectionDecodedLength;
    if (firstInput > source.Length - 8)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} states a first MSZH section of {firstInput} compressed byte(s), but only "
        + $"{source.Length - 8} byte(s) follow the split header.");
    if (sectionOutput == 0 && destination.Length != 0)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} states zero decoded bytes for each split MSZH section of a non-empty frame.");
    if ((long)sectionOutput * 2 > destination.Length)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} states two split MSZH sections of {sectionOutput} decoded byte(s) each, "
        + $"which exceed its {destination.Length}-byte decoded frame.");

    _Decompress(source.Slice(8, firstInput), destination, 0, sectionOutput, this._streamIndex);
    _Decompress(source[(8 + firstInput)..], destination, sectionOutput, sectionOutput, this._streamIndex);
    destination.AsSpan(sectionOutput * 2).Clear();
  }

  private static void _Decompress(
    ReadOnlySpan<byte> source,
    byte[] destination,
    int destinationOffset,
    int destinationLength,
    int streamIndex
  ) {
    if (destinationLength == 0)
      return;

    var sourcePosition = 0;
    var written = 0;

    while (written < destinationLength) {
      if (sourcePosition >= source.Length)
        throw new InvalidDataException(
          $"Video stream {streamIndex} carries an MSZH packet that ends before its {destinationLength}-byte section "
          + "has been reconstructed.");

      var mask = source[sourcePosition++];
      for (var maskBit = 0x80; maskBit != 0 && written < destinationLength; maskBit >>= 1) {
        if ((mask & maskBit) == 0) {
          if (source.Length - sourcePosition < 4)
            throw new InvalidDataException(
              $"Video stream {streamIndex} carries an MSZH literal whose four bytes run past the end of the packet.");

          var count = Math.Min(4, destinationLength - written);
          source.Slice(sourcePosition, count).CopyTo(destination.AsSpan(destinationOffset + written, count));
          sourcePosition += 4;
          written += count;
          continue;
        }

        if (source.Length - sourcePosition < 2)
          throw new InvalidDataException(
            $"Video stream {streamIndex} carries an MSZH back-reference whose two-byte descriptor runs past the end "
            + "of the packet.");

        var descriptor = BinaryPrimitives.ReadUInt16LittleEndian(source[sourcePosition..]);
        sourcePosition += 2;

        var distance = descriptor & 0x07ff;
        var countBytes = ((descriptor >> 11) + 1) * 4;
        countBytes = Math.Min(countBytes, destinationLength - written);

        distance = Math.Min(distance, written);
        if (distance == 0) {
          destination.AsSpan(destinationOffset + written, countBytes).Clear();
          written += countBytes;
          continue;
        }

        for (var i = 0; i < countBytes; ++i)
          destination[destinationOffset + written + i] = destination[destinationOffset + written - distance + i];
        written += countBytes;
      }
    }
  }

  private RawImage _Unpack(ReadOnlySpan<byte> decoded, int rgbRowStride) => this._imageType switch {
    _IMAGE_TYPE_YUV111 => this._UnpackYuv111(decoded),
    _IMAGE_TYPE_YUV422 => this._UnpackYuv422(decoded),
    _IMAGE_TYPE_RGB24 => this._UnpackRgb(decoded, rgbRowStride),
    _IMAGE_TYPE_YUV411 => this._UnpackYuv411(decoded),
    _IMAGE_TYPE_YUV211 => this._UnpackYuv211(decoded),
    _IMAGE_TYPE_YUV420 => this._UnpackYuv420(decoded),
    _ => throw new InvalidDataException($"Video stream {this._streamIndex} has unsupported LCL image type {this._imageType}."),
  };

  private RawImage _UnpackRgb(ReadOnlySpan<byte> decoded, int rowStride) {
    var picture = new byte[this._packedRgbStride * this._height];
    for (var row = 0; row < this._height; ++row) {
      var destinationRow = this._height - 1 - row;
      decoded.Slice(row * rowStride, this._packedRgbStride)
        .CopyTo(picture.AsSpan(destinationRow * this._packedRgbStride, this._packedRgbStride));
    }

    return new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Bgr24,
      PixelData = picture,
    };
  }

  private RawImage _UnpackYuv111(ReadOnlySpan<byte> decoded) {
    var samples = checked(this._width * this._height);
    var pixels = new byte[checked(samples * 3)];
    var luma = pixels.AsSpan(0, samples);
    var cb = pixels.AsSpan(samples, samples);
    var cr = pixels.AsSpan(samples * 2, samples);
    var source = 0;

    for (var codedRow = 0; codedRow < this._height; ++codedRow) {
      var row = this._height - 1 - codedRow;
      var rowOffset = row * this._width;
      for (var x = 0; x < this._width; ++x) {
        var target = rowOffset + x;
        luma[target] = decoded[source++];
        cb[target] = _DecodeChroma(decoded[source++]);
        cr[target] = _DecodeChroma(decoded[source++]);
      }
    }

    return this._Yuv(PixelFormat.Yuv444P8, pixels);
  }

  private RawImage _UnpackYuv422(ReadOnlySpan<byte> decoded) {
    var lumaSamples = checked(this._width * this._height);
    var chromaWidth = (this._width + 1) / 2;
    var chromaSamples = checked(chromaWidth * this._height);
    var pixels = new byte[checked(lumaSamples + chromaSamples * 2)];
    var luma = pixels.AsSpan(0, lumaSamples);
    var cb = pixels.AsSpan(lumaSamples, chromaSamples);
    var cr = pixels.AsSpan(lumaSamples + chromaSamples, chromaSamples);
    cb.Fill(128);
    cr.Fill(128);
    var source = 0;

    for (var codedRow = 0; codedRow < this._height; ++codedRow) {
      var row = this._height - 1 - codedRow;
      var yRow = row * this._width;
      var cRow = row * chromaWidth;
      var x = 0;
      for (; x < this._width - 3; x += 4) {
        decoded.Slice(source, 4).CopyTo(luma.Slice(yRow + x, 4));
        source += 4;
        var cx = x >> 1;
        cb[cRow + cx] = _DecodeChroma(decoded[source++]);
        cb[cRow + cx + 1] = _DecodeChroma(decoded[source++]);
        cr[cRow + cx] = _DecodeChroma(decoded[source++]);
        cr[cRow + cx + 1] = _DecodeChroma(decoded[source++]);
      }

      if (x != 0 && x < this._width) {
        var cx = x >> 1;
        cb[cRow + cx] = cb[cRow + cx - 1];
        cr[cRow + cx] = cr[cRow + cx - 1];
      }
    }

    return this._Yuv(PixelFormat.Yuv422P8, pixels);
  }

  private RawImage _UnpackYuv411(ReadOnlySpan<byte> decoded) {
    var samples = checked(this._width * this._height);
    var pixels = new byte[checked(samples * 3)];
    var luma = pixels.AsSpan(0, samples);
    var cb = pixels.AsSpan(samples, samples);
    var cr = pixels.AsSpan(samples * 2, samples);
    cb.Fill(128);
    cr.Fill(128);
    var source = 0;

    for (var codedRow = 0; codedRow < this._height; ++codedRow) {
      var row = this._height - 1 - codedRow;
      var rowOffset = row * this._width;
      var x = 0;
      byte lastCb = 128;
      byte lastCr = 128;
      for (; x < this._width - 3; x += 4) {
        decoded.Slice(source, 4).CopyTo(luma.Slice(rowOffset + x, 4));
        source += 4;
        lastCb = _DecodeChroma(decoded[source++]);
        lastCr = _DecodeChroma(decoded[source++]);
        cb.Slice(rowOffset + x, 4).Fill(lastCb);
        cr.Slice(rowOffset + x, 4).Fill(lastCr);
      }

      if (x != 0 && x < this._width) {
        cb.Slice(rowOffset + x, this._width - x).Fill(lastCb);
        cr.Slice(rowOffset + x, this._width - x).Fill(lastCr);
      }
    }

    return this._Yuv(PixelFormat.Yuv444P8, pixels);
  }

  private RawImage _UnpackYuv211(ReadOnlySpan<byte> decoded) {
    var lumaSamples = checked(this._width * this._height);
    var chromaWidth = this._width / 2;
    var chromaSamples = checked(chromaWidth * this._height);
    var pixels = new byte[checked(lumaSamples + chromaSamples * 2)];
    var luma = pixels.AsSpan(0, lumaSamples);
    var cb = pixels.AsSpan(lumaSamples, chromaSamples);
    var cr = pixels.AsSpan(lumaSamples + chromaSamples, chromaSamples);
    var source = 0;

    for (var codedRow = 0; codedRow < this._height; ++codedRow) {
      var row = this._height - 1 - codedRow;
      var yRow = row * this._width;
      var cRow = row * chromaWidth;
      for (var x = 0; x < this._width; x += 2) {
        luma[yRow + x] = decoded[source++];
        luma[yRow + x + 1] = decoded[source++];
        var cx = x >> 1;
        cb[cRow + cx] = _DecodeChroma(decoded[source++]);
        cr[cRow + cx] = _DecodeChroma(decoded[source++]);
      }
    }

    return this._Yuv(PixelFormat.Yuv422P8, pixels);
  }

  private RawImage _UnpackYuv420(ReadOnlySpan<byte> decoded) {
    var lumaSamples = checked(this._width * this._height);
    var chromaWidth = this._width / 2;
    var chromaHeight = this._height / 2;
    var chromaSamples = checked(chromaWidth * chromaHeight);
    var pixels = new byte[checked(lumaSamples + chromaSamples * 2)];
    var luma = pixels.AsSpan(0, lumaSamples);
    var cb = pixels.AsSpan(lumaSamples, chromaSamples);
    var cr = pixels.AsSpan(lumaSamples + chromaSamples, chromaSamples);
    var source = 0;

    for (var codedPair = 0; codedPair < chromaHeight; ++codedPair) {
      var bottomRow = this._height - 1 - codedPair * 2;
      var topRow = bottomRow - 1;
      var chromaRow = chromaHeight - 1 - codedPair;
      var bottomOffset = bottomRow * this._width;
      var topOffset = topRow * this._width;
      var chromaOffset = chromaRow * chromaWidth;

      for (var x = 0; x < this._width; x += 2) {
        luma[bottomOffset + x] = decoded[source++];
        luma[bottomOffset + x + 1] = decoded[source++];
        luma[topOffset + x] = decoded[source++];
        luma[topOffset + x + 1] = decoded[source++];
        var cx = x >> 1;
        cb[chromaOffset + cx] = _DecodeChroma(decoded[source++]);
        cr[chromaOffset + cx] = _DecodeChroma(decoded[source++]);
      }
    }

    return this._Yuv(PixelFormat.Yuv420P8, pixels);
  }

  private RawImage _Yuv(PixelFormat format, byte[] pixels) => new() {
    Width = this._width,
    Height = this._height,
    Format = format,
    PixelData = pixels,
    ColorInfo = _YuvColor,
  };

  private static byte _DecodeChroma(byte value) => unchecked((byte)(value + 128));
}
