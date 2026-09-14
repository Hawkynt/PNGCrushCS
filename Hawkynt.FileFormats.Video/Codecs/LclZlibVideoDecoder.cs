using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using FileFormat.Bmp;
using FileFormat.Codecs.Lcl;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes the ZLIB variant of the Lossless Codec Library (LCL): an intra-only lossless picture whose
/// packed colour-space bytes are optionally delta-filtered and then stored in one or two RFC 1950 zlib streams.
/// </summary>
/// <remarks>
/// LCL ZLIB has no I/P/B hierarchy or motion references: every non-null packet is an independently decodable
/// intra frame. The null-frame stream flag belongs to AVI chunk omission rather than to an inter-frame coding mode.
/// <para/>
/// The image-type numbers and public wrapper come from Roberto Togni's published LCL notes. Colour-space unpacking,
/// the two-stream wrapper, historical raw-RGB exception, compression-level validation and inverse PNG-style
/// predictor are adapted from FFmpeg's <c>libavcodec/lcldec.c</c> and <c>lcl.h</c>, copyright (c) 2002-2004
/// Roberto Togni, LGPL-2.1-or-later. This adaptation is distributed by PNGCrushCS under LGPL-3.0-or-later.
/// <para/>
/// All six LCL image types are exposed losslessly: YUV111 as 4:4:4, YUV422 and YUV211 as 4:2:2, YUV411 as
/// 4:1:1, YUV420 as 4:2:0, and RGB24 as BGR24. LCL stores chroma as signed bytes centered at zero; canonical
/// <see cref="RawImage"/> YUV stores those same samples biased by 128. Coded rows run bottom-up, as in the
/// reference decoder. YUV422 and YUV411 historically allow a partial final horizontal group; the format contains
/// no luma samples for that tail, so the corresponding canonical samples remain zero while the one chroma sample
/// explicitly replicated by the reference decoder is replicated here too.
/// <para/>
/// Decompression is bounded by the frame size calculated from the stream header. A valid zlib stream that produces
/// fewer bytes is accepted and its missing tail remains zero, matching the compatible decoder; a stream that expands
/// beyond the declared frame capacity is rejected instead of allocating according to attacker-controlled output.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class LclZlibVideoDecoder : IVideoCodecDecoder<LclZlibVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("ZLIB");

  private const byte _IMAGE_TYPE_YUV111 = 0;
  private const byte _IMAGE_TYPE_YUV422 = 1;
  private const byte _IMAGE_TYPE_RGB24 = 2;
  private const byte _IMAGE_TYPE_YUV411 = 3;
  private const byte _IMAGE_TYPE_YUV211 = 4;
  private const byte _IMAGE_TYPE_YUV420 = 5;
  private const sbyte _COMPRESSION_NORMAL = -1;
  private const byte _CODEC_ZLIB = 3;

  private static readonly RawImageColorInfo _YuvColorInfo = new() {
    Range = RawColorRange.Full,
    Matrix = RawMatrixCoefficients.Bt601,
    ChromaLocation = RawChromaLocation.Unspecified,
  };

  private readonly int _width;
  private readonly int _height;
  private readonly int _packedRgbStride;
  private readonly int _paddedRgbStride;
  private readonly int _decodedCapacity;
  private readonly int _streamIndex;
  private readonly byte _imageType;
  private readonly sbyte _compression;
  private readonly bool _multithreaded;
  private readonly bool _pngFiltered;

  private LclZlibVideoDecoder(
    int width,
    int height,
    int packedRgbStride,
    int paddedRgbStride,
    int decodedCapacity,
    int streamIndex,
    byte imageType,
    sbyte compression,
    bool multithreaded,
    bool pngFiltered
  ) {
    this._width = width;
    this._height = height;
    this._packedRgbStride = packedRgbStride;
    this._paddedRgbStride = paddedRgbStride;
    this._decodedCapacity = decodedCapacity;
    this._streamIndex = streamIndex;
    this._imageType = imageType;
    this._compression = compression;
    this._multithreaded = multithreaded;
    this._pngFiltered = pngFiltered;
  }

  public static string CodecName => "LCL ZLIB";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static LclZlibVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can be decoded into.");

    var format = stream.CodecPrivateData.Span;
    if (format.Length < BitmapInfoHeader.StructSize + LclHeader.ExtraBytes)
      throw new InvalidDataException(
        $"Video stream {stream.Index} carries {format.Length} byte(s) of stream format, where LCL's BITMAPINFOHEADER "
        + $"and eight-byte trailer need at least {BitmapInfoHeader.StructSize + LclHeader.ExtraBytes}.");

    var header = LclHeader.Read(format[BitmapInfoHeader.StructSize..]);
    if (header.Codec != _CODEC_ZLIB)
      throw new InvalidDataException(
        $"Video stream {stream.Index} is tagged ZLIB but its LCL trailer identifies codec {header.Codec}, not ZLIB ({_CODEC_ZLIB}).");

    if (header.ImageType > _IMAGE_TYPE_YUV420)
      throw new NotSupportedException(
        $"Video stream {stream.Index} states unsupported LCL image type {header.ImageType}; defined image types are 0 through 5.");

    if (header.Compression < _COMPRESSION_NORMAL || header.Compression > 9)
      throw new NotSupportedException(
        $"Video stream {stream.Index} states unsupported LCL ZLIB compression level {header.Compression}; valid values are -1 and 0 through 9.");

    if (header.ImageType == _IMAGE_TYPE_YUV211 && (stream.Width & 1) != 0)
      throw new NotSupportedException(
        $"Video stream {stream.Index} is {stream.Width} pixels wide, but LCL YUV211 stores two luma samples per chroma pair and requires an even width.");

    if (header.ImageType == _IMAGE_TYPE_YUV420 && ((stream.Width | stream.Height) & 1) != 0)
      throw new NotSupportedException(
        $"Video stream {stream.Index} is {stream.Width}x{stream.Height}, but LCL YUV420 stores 2x2 luma blocks and requires even dimensions.");

    var packedRgbStrideLong = (long)stream.Width * 3;
    var paddedRgbStrideLong = (packedRgbStrideLong + 3) & ~3L;
    var decodedCapacityLong = header.ImageType switch {
      _IMAGE_TYPE_YUV111 => (long)stream.Width * stream.Height * 3,
      _IMAGE_TYPE_YUV422 => (long)(stream.Width & ~3) * stream.Height * 2,
      _IMAGE_TYPE_RGB24 => paddedRgbStrideLong * stream.Height,
      _IMAGE_TYPE_YUV411 => (long)(stream.Width & ~3) * stream.Height * 3 / 2,
      _IMAGE_TYPE_YUV211 => (long)stream.Width * stream.Height * 2,
      _IMAGE_TYPE_YUV420 => (long)stream.Width * stream.Height * 3 / 2,
      _ => throw new InvalidOperationException(),
    };

    if (packedRgbStrideLong > int.MaxValue || paddedRgbStrideLong > int.MaxValue || decodedCapacityLong > int.MaxValue)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, whose decoded LCL frame is too large for one managed byte array.");

    return new(
      stream.Width,
      stream.Height,
      (int)packedRgbStrideLong,
      (int)paddedRgbStrideLong,
      (int)decodedCapacityLong,
      stream.Index,
      header.ImageType,
      header.Compression,
      header.Multithreaded,
      header.PngFiltered
    );
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var source = packet.Data.Span;
    var decoded = new byte[this._decodedCapacity];
    int decodedLength;

    var packedRgbBytes = checked(this._packedRgbStride * this._height);
    if (this._compression == _COMPRESSION_NORMAL
      && this._imageType == _IMAGE_TYPE_RGB24
      && source.Length == packedRgbBytes) {
      source.CopyTo(decoded);
      decodedLength = source.Length;
    } else if (this._multithreaded) {
      this._InflateSplit(source, decoded);
      decodedLength = decoded.Length;
    } else {
      decodedLength = this._Inflate(source, decoded, "frame");
    }

    if (this._pngFiltered)
      this._UndoPngFilter(decoded);

    frame = this._imageType switch {
      _IMAGE_TYPE_YUV111 => this._UnpackYuv111(decoded),
      _IMAGE_TYPE_YUV422 => this._UnpackYuv422(decoded),
      _IMAGE_TYPE_RGB24 => this._UnpackRgb24(decoded, decodedLength),
      _IMAGE_TYPE_YUV411 => this._UnpackYuv411(decoded),
      _IMAGE_TYPE_YUV211 => this._UnpackYuv211(decoded),
      _IMAGE_TYPE_YUV420 => this._UnpackYuv420(decoded),
      _ => throw new InvalidOperationException(),
    };
    return true;
  }

  private void _InflateSplit(ReadOnlySpan<byte> source, byte[] destination) {
    if (source.Length < 8)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries a split LCL ZLIB packet shorter than its eight-byte split header.");

    var firstCompressedLength = BinaryPrimitives.ReadUInt32LittleEndian(source);
    var sectionDecodedLength = BinaryPrimitives.ReadUInt32LittleEndian(source[4..]);
    if (firstCompressedLength > int.MaxValue || sectionDecodedLength > int.MaxValue)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries a split LCL ZLIB packet whose section lengths do not fit in memory.");

    var firstInput = (int)firstCompressedLength;
    if (firstInput > source.Length - 8)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} states a first LCL ZLIB section of {firstInput} compressed byte(s), but only "
        + $"{source.Length - 8} byte(s) follow the split header.");

    var sectionOutput = Math.Min((int)sectionDecodedLength, destination.Length);
    _ = this._Inflate(source.Slice(8, firstInput), destination.AsSpan(0, sectionOutput), "first split section");

    var secondOffset = sectionOutput;
    var secondLimit = Math.Min(sectionOutput, destination.Length - secondOffset);
    _ = this._Inflate(source[(8 + firstInput)..], destination.AsSpan(secondOffset, secondLimit), "second split section");
  }

  private int _Inflate(ReadOnlySpan<byte> source, Span<byte> destination, string part) {
    using var input = new MemoryStream(source.ToArray(), writable: false);
    using var zlib = new ZLibStream(input, CompressionMode.Decompress);
    var written = 0;

    try {
      while (written < destination.Length) {
        var count = zlib.Read(destination[written..]);
        if (count == 0)
          return written;
        written += count;
      }

      Span<byte> extra = stackalloc byte[1];
      if (zlib.Read(extra) != 0)
        throw new LclOutputOverflowException();
      return written;
    } catch (LclOutputOverflowException) {
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an LCL ZLIB {part} that expands beyond its declared {destination.Length}-byte output capacity.");
    } catch (InvalidDataException ex) {
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an LCL ZLIB {part} whose zlib stream is corrupt or truncated.", ex);
    }
  }

  private void _UndoPngFilter(byte[] data) {
    switch (this._imageType) {
      case _IMAGE_TYPE_YUV111:
      case _IMAGE_TYPE_RGB24:
        for (var row = 0; row < this._height; ++row) {
          var position = row * this._width * 3;
          var first = data[position++];
          var pair = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(position, 2));
          position += 2;
          for (var column = 1; column < this._width; ++column) {
            first = data[position] = unchecked((byte)(first - data[position]));
            pair = unchecked((ushort)(pair - BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(position + 1, 2))));
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(position + 1, 2), pair);
            position += 3;
          }
        }
        break;

      case _IMAGE_TYPE_YUV422: {
        var position = 0;
        for (var row = 0; row < this._height; ++row) {
          byte y = 0, u = 0, v = 0;
          for (var group = 0; group < this._width / 4; ++group) {
            for (var i = 0; i < 4; ++i)
              y = data[position + i] = unchecked((byte)(y - data[position + i]));
            u = data[position + 4] = unchecked((byte)(u - data[position + 4]));
            u = data[position + 5] = unchecked((byte)(u - data[position + 5]));
            v = data[position + 6] = unchecked((byte)(v - data[position + 6]));
            v = data[position + 7] = unchecked((byte)(v - data[position + 7]));
            position += 8;
          }
        }
        break;
      }

      case _IMAGE_TYPE_YUV411: {
        var position = 0;
        for (var row = 0; row < this._height; ++row) {
          byte y = 0, u = 0, v = 0;
          for (var group = 0; group < this._width / 4; ++group) {
            for (var i = 0; i < 4; ++i)
              y = data[position + i] = unchecked((byte)(y - data[position + i]));
            u = data[position + 4] = unchecked((byte)(u - data[position + 4]));
            v = data[position + 5] = unchecked((byte)(v - data[position + 5]));
            position += 6;
          }
        }
        break;
      }

      case _IMAGE_TYPE_YUV211:
        for (var row = 0; row < this._height; ++row) {
          var position = row * this._width * 2;
          byte y = 0, u = 0, v = 0;
          for (var group = 0; group < this._width / 2; ++group) {
            y = data[position] = unchecked((byte)(y - data[position]));
            y = data[position + 1] = unchecked((byte)(y - data[position + 1]));
            u = data[position + 2] = unchecked((byte)(u - data[position + 2]));
            v = data[position + 3] = unchecked((byte)(v - data[position + 3]));
            position += 4;
          }
        }
        break;

      case _IMAGE_TYPE_YUV420:
        for (var row = 0; row < this._height / 2; ++row) {
          var position = row * this._width * 3;
          byte y0 = 0, y1 = 0, u = 0, v = 0;
          for (var group = 0; group < this._width / 2; ++group) {
            y0 = data[position] = unchecked((byte)(y0 - data[position]));
            y0 = data[position + 1] = unchecked((byte)(y0 - data[position + 1]));
            y1 = data[position + 2] = unchecked((byte)(y1 - data[position + 2]));
            y1 = data[position + 3] = unchecked((byte)(y1 - data[position + 3]));
            u = data[position + 4] = unchecked((byte)(u - data[position + 4]));
            v = data[position + 5] = unchecked((byte)(v - data[position + 5]));
            position += 6;
          }
        }
        break;
    }
  }

  private RawImage _UnpackRgb24(byte[] decoded, int decodedLength) {
    var rowStride = decodedLength < checked(this._paddedRgbStride * this._height)
      ? this._packedRgbStride
      : this._paddedRgbStride;
    var picture = new byte[checked(this._packedRgbStride * this._height)];

    for (var codedRow = 0; codedRow < this._height; ++codedRow) {
      var destinationRow = this._height - 1 - codedRow;
      decoded.AsSpan(codedRow * rowStride, this._packedRgbStride)
        .CopyTo(picture.AsSpan(destinationRow * this._packedRgbStride, this._packedRgbStride));
    }

    return new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Bgr24,
      PixelData = picture,
    };
  }

  private RawImage _UnpackYuv111(byte[] decoded) {
    var planeSize = checked(this._width * this._height);
    var picture = new byte[checked(planeSize * 3)];
    var uOffset = planeSize;
    var vOffset = planeSize * 2;
    var source = 0;

    for (var codedRow = 0; codedRow < this._height; ++codedRow) {
      var destinationRow = this._height - 1 - codedRow;
      var rowOffset = destinationRow * this._width;
      for (var column = 0; column < this._width; ++column) {
        picture[rowOffset + column] = decoded[source++];
        picture[uOffset + rowOffset + column] = unchecked((byte)(decoded[source++] + 128));
        picture[vOffset + rowOffset + column] = unchecked((byte)(decoded[source++] + 128));
      }
    }

    return this._YuvFrame(PixelFormat.Yuv444P8, picture);
  }

  private RawImage _UnpackYuv422(byte[] decoded) {
    var ySize = checked(this._width * this._height);
    var chromaWidth = (this._width + 1) / 2;
    var chromaSize = checked(chromaWidth * this._height);
    var picture = new byte[checked(ySize + chromaSize * 2)];
    var uOffset = ySize;
    var vOffset = ySize + chromaSize;
    var source = 0;

    for (var codedRow = 0; codedRow < this._height; ++codedRow) {
      var destinationRow = this._height - 1 - codedRow;
      var yRow = destinationRow * this._width;
      var cRow = destinationRow * chromaWidth;
      var column = 0;
      for (; column < this._width - 3; column += 4) {
        decoded.AsSpan(source, 4).CopyTo(picture.AsSpan(yRow + column, 4));
        source += 4;
        picture[uOffset + cRow + (column >> 1)] = unchecked((byte)(decoded[source++] + 128));
        picture[uOffset + cRow + (column >> 1) + 1] = unchecked((byte)(decoded[source++] + 128));
        picture[vOffset + cRow + (column >> 1)] = unchecked((byte)(decoded[source++] + 128));
        picture[vOffset + cRow + (column >> 1) + 1] = unchecked((byte)(decoded[source++] + 128));
      }

      if (column != 0 && column < this._width) {
        picture[uOffset + cRow + (column >> 1)] = picture[uOffset + cRow + (column >> 1) - 1];
        picture[vOffset + cRow + (column >> 1)] = picture[vOffset + cRow + (column >> 1) - 1];
      }
    }

    return this._YuvFrame(PixelFormat.Yuv422P8, picture);
  }

  private RawImage _UnpackYuv411(byte[] decoded) {
    var ySize = checked(this._width * this._height);
    var chromaWidth = (this._width + 3) / 4;
    var chromaSize = checked(chromaWidth * this._height);
    var picture = new byte[checked(ySize + chromaSize * 2)];
    var uOffset = ySize;
    var vOffset = ySize + chromaSize;
    var source = 0;

    for (var codedRow = 0; codedRow < this._height; ++codedRow) {
      var destinationRow = this._height - 1 - codedRow;
      var yRow = destinationRow * this._width;
      var cRow = destinationRow * chromaWidth;
      var column = 0;
      for (; column < this._width - 3; column += 4) {
        decoded.AsSpan(source, 4).CopyTo(picture.AsSpan(yRow + column, 4));
        source += 4;
        picture[uOffset + cRow + (column >> 2)] = unchecked((byte)(decoded[source++] + 128));
        picture[vOffset + cRow + (column >> 2)] = unchecked((byte)(decoded[source++] + 128));
      }

      if (column != 0 && column < this._width) {
        picture[uOffset + cRow + (column >> 2)] = picture[uOffset + cRow + (column >> 2) - 1];
        picture[vOffset + cRow + (column >> 2)] = picture[vOffset + cRow + (column >> 2) - 1];
      }
    }

    return this._YuvFrame(PixelFormat.Yuv411P8, picture);
  }

  private RawImage _UnpackYuv211(byte[] decoded) {
    var ySize = checked(this._width * this._height);
    var chromaWidth = this._width / 2;
    var chromaSize = checked(chromaWidth * this._height);
    var picture = new byte[checked(ySize + chromaSize * 2)];
    var uOffset = ySize;
    var vOffset = ySize + chromaSize;
    var source = 0;

    for (var codedRow = 0; codedRow < this._height; ++codedRow) {
      var destinationRow = this._height - 1 - codedRow;
      var yRow = destinationRow * this._width;
      var cRow = destinationRow * chromaWidth;
      for (var column = 0; column < this._width; column += 2) {
        picture[yRow + column] = decoded[source++];
        picture[yRow + column + 1] = decoded[source++];
        picture[uOffset + cRow + (column >> 1)] = unchecked((byte)(decoded[source++] + 128));
        picture[vOffset + cRow + (column >> 1)] = unchecked((byte)(decoded[source++] + 128));
      }
    }

    return this._YuvFrame(PixelFormat.Yuv422P8, picture);
  }

  private RawImage _UnpackYuv420(byte[] decoded) {
    var ySize = checked(this._width * this._height);
    var chromaWidth = this._width / 2;
    var chromaHeight = this._height / 2;
    var chromaSize = checked(chromaWidth * chromaHeight);
    var picture = new byte[checked(ySize + chromaSize * 2)];
    var uOffset = ySize;
    var vOffset = ySize + chromaSize;
    var source = 0;

    for (var codedPair = 0; codedPair < chromaHeight; ++codedPair) {
      var bottomRow = this._height - 1 - codedPair * 2;
      var topRow = bottomRow - 1;
      var chromaRow = chromaHeight - 1 - codedPair;
      for (var column = 0; column < this._width; column += 2) {
        picture[bottomRow * this._width + column] = decoded[source++];
        picture[bottomRow * this._width + column + 1] = decoded[source++];
        picture[topRow * this._width + column] = decoded[source++];
        picture[topRow * this._width + column + 1] = decoded[source++];
        picture[uOffset + chromaRow * chromaWidth + (column >> 1)] = unchecked((byte)(decoded[source++] + 128));
        picture[vOffset + chromaRow * chromaWidth + (column >> 1)] = unchecked((byte)(decoded[source++] + 128));
      }
    }

    return this._YuvFrame(PixelFormat.Yuv420P8, picture);
  }

  private RawImage _YuvFrame(PixelFormat format, byte[] data) => new() {
    Width = this._width,
    Height = this._height,
    Format = format,
    PixelData = data,
    ColorInfo = _YuvColorInfo,
  };

  private sealed class LclOutputOverflowException : Exception { }
}
