using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.Lcl;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes LCL MSZH as independently decodable intra pictures using four-byte literals and backward
/// copies selected by one mask byte for every eight commands.
/// </summary>
/// <remarks>
/// Roberto Togni's LCL description defines the wrapper, image types, row order and optional two-way
/// split, but leaves the MSZH command word itself as a placeholder. The interoperable command layout
/// is therefore taken from FFmpeg's LGPL-2.1-or-later decoder: mask bits are consumed most-significant
/// first; a zero bit carries four literals; a one bit carries a little-endian 16-bit descriptor whose
/// low eleven bits are the byte distance and whose high five bits plus one are the number of four-byte
/// groups to reproduce. This encoder is an original managed implementation of that public behaviour.
/// <para/>
/// With no codec-private template the encoder writes the traditional RGB24 profile. Supplying a valid
/// 48-byte LCL/MSZH <see cref="MediaStreamInfo.CodecPrivateData"/> selects any of the six LCL image
/// types and either compressed or explicit uncompressed storage; bit 0 of its flags additionally asks
/// for the two-section form. The output header is rebuilt rather than copied so geometry, image size,
/// compression and flags cannot contradict the packets this encoder actually emits. Null-frame and
/// PNG-filter flags are deliberately cleared: null frames are an AVI/container optimisation and the
/// PNG predictor belongs to LCL ZLIB, not MSZH.
/// <para/>
/// RGB24 accepts the same losslessly convertible eight-bit inputs as the other RGB lossless encoders.
/// YUV profiles accept their native eight-bit planar format without colour conversion. LCL's YUV411
/// has no <see cref="PixelFormat"/> counterpart, so that profile accepts <see cref="PixelFormat.Yuv444P8"/>
/// only when each four-pixel group already has constant Cb and Cr; this preserves every represented
/// sample rather than quietly turning a lossless codec into a chroma resampler.
/// <para/>
/// MSZH has no P/B frames or forward/backward frame references. Every non-null packet is a key frame;
/// back-references exist only inside the bytes of that same frame. For compressed image sizes not
/// divisible by four, the command grammar cannot represent an arbitrary final partial literal. RGB24
/// is always padded to four bytes and YUV111 may use the reference decoder's raw-frame fallback; other
/// such geometries must use explicit uncompressed mode. Split sections additionally need lengths that
/// are whole four-byte command groups.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class MszhVideoEncoder : IVideoCodecEncoder<MszhVideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("MSZH");

  private const byte _IMAGE_TYPE_YUV111 = 0;
  private const byte _IMAGE_TYPE_YUV422 = 1;
  private const byte _IMAGE_TYPE_RGB24 = 2;
  private const byte _IMAGE_TYPE_YUV411 = 3;
  private const byte _IMAGE_TYPE_YUV211 = 4;
  private const byte _IMAGE_TYPE_YUV420 = 5;
  private const sbyte _COMPRESSION_MSZH = 0;
  private const sbyte _COMPRESSION_NONE = 1;
  private const byte _CODEC_MSZH = 1;
  private const byte _FLAG_MULTITHREAD = 0x01;
  private const int _MAX_DISTANCE = 0x07ff;
  private const int _MAX_GROUPS = 32;
  private const int _GROUP_BYTES = 4;

  private readonly MediaStreamInfo _stream;
  private readonly int _width;
  private readonly int _height;
  private readonly byte _imageType;
  private readonly sbyte _compression;
  private readonly bool _multithreaded;
  private readonly int _decodedSize;
  private readonly int _packedRgbStride;
  private readonly int _paddedRgbStride;

  private MszhVideoEncoder(
    MediaStreamInfo stream,
    byte imageType,
    sbyte compression,
    bool multithreaded,
    int decodedSize,
    int bitsPerPixel,
    int packedRgbStride = 0,
    int paddedRgbStride = 0
  ) {
    this._width = stream.Width;
    this._height = stream.Height;
    this._imageType = imageType;
    this._compression = compression;
    this._multithreaded = multithreaded;
    this._decodedSize = decodedSize;
    this._packedRgbStride = packedRgbStride;
    this._paddedRgbStride = paddedRgbStride;
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
      BitsPerPixel = bitsPerPixel,
      CodecPrivateData = _PrivateData(
        stream.Width,
        stream.Height,
        decodedSize,
        bitsPerPixel,
        imageType,
        compression,
        multithreaded ? _FLAG_MULTITHREAD : (byte)0),
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "LCL MSZH";

  public static CodecTag Codec => _Tag;

  public static MszhVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("LCL MSZH can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"An LCL MSZH encoder needs positive picture dimensions before the muxer is created; {stream.Width}x{stream.Height} was supplied.");

    var imageType = _IMAGE_TYPE_RGB24;
    var compression = _COMPRESSION_MSZH;
    var multithreaded = false;
    if (!stream.CodecPrivateData.IsEmpty) {
      var format = stream.CodecPrivateData.Span;
      if (format.Length < BitmapInfoHeader.StructSize + LclHeader.ExtraBytes)
        throw new NotSupportedException(
          $"An LCL MSZH encoder profile needs at least {BitmapInfoHeader.StructSize + LclHeader.ExtraBytes} bytes of "
          + $"BITMAPINFOHEADER and LCL trailer; {format.Length} byte(s) were supplied.");

      var header = LclHeader.Read(format[BitmapInfoHeader.StructSize..]);
      if (header.Codec != _CODEC_MSZH)
        throw new NotSupportedException(
          $"The supplied LCL encoder profile identifies codec {header.Codec}, not MSZH ({_CODEC_MSZH}).");
      if (header.ImageType > _IMAGE_TYPE_YUV420)
        throw new NotSupportedException(
          $"The supplied LCL encoder profile selects image type {header.ImageType}; MSZH defines image types 0 through 5.");
      if (header.Compression is not (_COMPRESSION_MSZH or _COMPRESSION_NONE))
        throw new NotSupportedException(
          $"The supplied LCL encoder profile selects compression mode {header.Compression}; MSZH defines 0 (compressed) and 1 (raw).");

      imageType = header.ImageType;
      compression = header.Compression;
      multithreaded = header.Multithreaded && compression == _COMPRESSION_MSZH;
    }

    var width = stream.Width;
    var height = stream.Height;
    var bitsPerPixel = 0;
    var packedRgbStride = 0L;
    var paddedRgbStride = 0L;
    long decodedSize;

    switch (imageType) {
      case _IMAGE_TYPE_YUV111:
        bitsPerPixel = 24;
        decodedSize = (long)width * height * 3;
        break;
      case _IMAGE_TYPE_YUV422:
        if ((width & 3) != 0)
          throw new NotSupportedException(
            $"LCL YUV 4:2:2 stores whole four-pixel groups; encoder width {width} is not divisible by four.");
        bitsPerPixel = 16;
        decodedSize = (long)width * height * 2;
        break;
      case _IMAGE_TYPE_RGB24:
        bitsPerPixel = 24;
        packedRgbStride = (long)width * 3;
        paddedRgbStride = (packedRgbStride + 3) & ~3L;
        decodedSize = paddedRgbStride * height;
        break;
      case _IMAGE_TYPE_YUV411:
        if ((width & 3) != 0)
          throw new NotSupportedException(
            $"LCL YUV 4:1:1 stores whole four-pixel groups; encoder width {width} is not divisible by four.");
        bitsPerPixel = 12;
        decodedSize = (long)width * height * 3 / 2;
        break;
      case _IMAGE_TYPE_YUV211:
        if ((width & 1) != 0)
          throw new NotSupportedException(
            $"LCL YUV 2:1:1 stores whole two-pixel groups; encoder width {width} is not even.");
        bitsPerPixel = 16;
        decodedSize = (long)width * height * 2;
        break;
      case _IMAGE_TYPE_YUV420:
        if (((width | height) & 1) != 0)
          throw new NotSupportedException(
            $"LCL YUV 4:2:0 needs even dimensions; encoder geometry {width}x{height} does not have them.");
        bitsPerPixel = 12;
        decodedSize = (long)width * height * 3 / 2;
        break;
      default:
        throw new NotSupportedException($"Unsupported LCL MSZH image type {imageType}.");
    }

    if (decodedSize > int.MaxValue || packedRgbStride > int.MaxValue || paddedRgbStride > int.MaxValue)
      throw new NotSupportedException(
        $"An LCL MSZH encoder cannot hold a {width}x{height} frame in one managed byte array.");

    var decodedSizeInt = (int)decodedSize;
    if (compression == _COMPRESSION_MSZH
        && imageType is not (_IMAGE_TYPE_RGB24 or _IMAGE_TYPE_YUV111)
        && (decodedSizeInt & (_GROUP_BYTES - 1)) != 0)
      throw new NotSupportedException(
        $"Compressed LCL image type {imageType} at {width}x{height} produces {decodedSizeInt} bytes, not a whole "
        + "number of MSZH four-byte command groups; use the format's explicit uncompressed mode for this geometry.");

    if (multithreaded && (decodedSizeInt & 7) != 0)
      throw new NotSupportedException(
        $"Split MSZH at {width}x{height} produces {decodedSizeInt} decoded bytes; two independently coded sections "
        + "need equal lengths that are each a whole four-byte command group.");

    return new(
      stream,
      imageType,
      compression,
      multithreaded,
      decodedSizeInt,
      bitsPerPixel,
      (int)packedRgbStride,
      (int)paddedRgbStride
    );
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"LCL MSZH stream geometry is {this._width}x{this._height}, but the source picture is {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        $"The source {frame.Format} picture does not contain enough pixel data for its declared {frame.Width}x{frame.Height} geometry.");

    var raw = this._Pack(frame);
    byte[] payload;
    if (this._compression == _COMPRESSION_NONE) {
      payload = raw;
    } else if (this._multithreaded) {
      payload = _CompressSplit(raw);
    } else if ((raw.Length & (_GROUP_BYTES - 1)) != 0) {
      // Only RGB24 and YUV111 have the compressed-mode raw fallback in the reference decoder.
      payload = raw;
    } else {
      var compressed = _Compress(raw);
      payload = this._imageType is _IMAGE_TYPE_RGB24 or _IMAGE_TYPE_YUV111 && compressed.Length >= raw.Length
        ? raw
        : compressed;
    }

    packet = new(
      this._stream.Index,
      payload,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: true);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  private byte[] _Pack(RawImage frame) => this._imageType switch {
    _IMAGE_TYPE_YUV111 => this._PackYuv111(frame),
    _IMAGE_TYPE_YUV422 => this._PackYuv422(frame),
    _IMAGE_TYPE_RGB24 => this._PackRgb(frame),
    _IMAGE_TYPE_YUV411 => this._PackYuv411(frame),
    _IMAGE_TYPE_YUV211 => this._PackYuv211(frame),
    _IMAGE_TYPE_YUV420 => this._PackYuv420(frame),
    _ => throw new NotSupportedException($"Unsupported LCL MSZH image type {this._imageType}."),
  };

  private byte[] _PackRgb(RawImage frame) {
    var picture = LosslessEncoderInput.Prepare(frame, PixelFormat.Bgr24, this._width, this._height, CodecName);
    var result = new byte[this._decodedSize];
    for (var row = 0; row < this._height; ++row) {
      var sourceRow = this._height - 1 - row;
      picture.PixelData.AsSpan(sourceRow * this._packedRgbStride, this._packedRgbStride)
        .CopyTo(result.AsSpan(row * this._paddedRgbStride, this._packedRgbStride));
    }

    return result;
  }

  private byte[] _PackYuv111(RawImage frame) {
    _RequireFormat(frame, PixelFormat.Yuv444P8, "YUV 4:4:4");
    var y = frame.GetPlaneData(0);
    var cb = frame.GetPlaneData(1);
    var cr = frame.GetPlaneData(2);
    var result = new byte[this._decodedSize];
    var target = 0;

    for (var codedRow = 0; codedRow < this._height; ++codedRow) {
      var row = this._height - 1 - codedRow;
      var rowOffset = row * this._width;
      for (var x = 0; x < this._width; ++x) {
        var source = rowOffset + x;
        result[target++] = y[source];
        result[target++] = _EncodeChroma(cb[source]);
        result[target++] = _EncodeChroma(cr[source]);
      }
    }

    return result;
  }

  private byte[] _PackYuv422(RawImage frame) {
    _RequireFormat(frame, PixelFormat.Yuv422P8, "YUV 4:2:2");
    var y = frame.GetPlaneData(0);
    var cb = frame.GetPlaneData(1);
    var cr = frame.GetPlaneData(2);
    var chromaWidth = this._width / 2;
    var result = new byte[this._decodedSize];
    var target = 0;

    for (var codedRow = 0; codedRow < this._height; ++codedRow) {
      var row = this._height - 1 - codedRow;
      var yRow = row * this._width;
      var cRow = row * chromaWidth;
      for (var x = 0; x < this._width; x += 4) {
        y.Slice(yRow + x, 4).CopyTo(result.AsSpan(target, 4));
        target += 4;
        var cx = x >> 1;
        result[target++] = _EncodeChroma(cb[cRow + cx]);
        result[target++] = _EncodeChroma(cb[cRow + cx + 1]);
        result[target++] = _EncodeChroma(cr[cRow + cx]);
        result[target++] = _EncodeChroma(cr[cRow + cx + 1]);
      }
    }

    return result;
  }

  private byte[] _PackYuv411(RawImage frame) {
    _RequireFormat(frame, PixelFormat.Yuv444P8, "sample-exact YUV 4:1:1 represented as YUV444P8");
    var y = frame.GetPlaneData(0);
    var cb = frame.GetPlaneData(1);
    var cr = frame.GetPlaneData(2);
    var result = new byte[this._decodedSize];
    var target = 0;

    for (var codedRow = 0; codedRow < this._height; ++codedRow) {
      var row = this._height - 1 - codedRow;
      var rowOffset = row * this._width;
      for (var x = 0; x < this._width; x += 4) {
        var source = rowOffset + x;
        var cbValue = cb[source];
        var crValue = cr[source];
        if (cb[source + 1] != cbValue || cb[source + 2] != cbValue || cb[source + 3] != cbValue
            || cr[source + 1] != crValue || cr[source + 2] != crValue || cr[source + 3] != crValue)
          throw new NotSupportedException(
            $"LCL YUV 4:1:1 has one chroma pair per four columns; source row {row}, columns {x}..{x + 3} "
            + "do not share one Cb/Cr value and would require lossy resampling.");

        y.Slice(source, 4).CopyTo(result.AsSpan(target, 4));
        target += 4;
        result[target++] = _EncodeChroma(cbValue);
        result[target++] = _EncodeChroma(crValue);
      }
    }

    return result;
  }

  private byte[] _PackYuv211(RawImage frame) {
    _RequireFormat(frame, PixelFormat.Yuv422P8, "YUV 2:1:1");
    var y = frame.GetPlaneData(0);
    var cb = frame.GetPlaneData(1);
    var cr = frame.GetPlaneData(2);
    var chromaWidth = this._width / 2;
    var result = new byte[this._decodedSize];
    var target = 0;

    for (var codedRow = 0; codedRow < this._height; ++codedRow) {
      var row = this._height - 1 - codedRow;
      var yRow = row * this._width;
      var cRow = row * chromaWidth;
      for (var x = 0; x < this._width; x += 2) {
        result[target++] = y[yRow + x];
        result[target++] = y[yRow + x + 1];
        var cx = x >> 1;
        result[target++] = _EncodeChroma(cb[cRow + cx]);
        result[target++] = _EncodeChroma(cr[cRow + cx]);
      }
    }

    return result;
  }

  private byte[] _PackYuv420(RawImage frame) {
    _RequireFormat(frame, PixelFormat.Yuv420P8, "YUV 4:2:0");
    var y = frame.GetPlaneData(0);
    var cb = frame.GetPlaneData(1);
    var cr = frame.GetPlaneData(2);
    var chromaWidth = this._width / 2;
    var chromaHeight = this._height / 2;
    var result = new byte[this._decodedSize];
    var target = 0;

    for (var codedPair = 0; codedPair < chromaHeight; ++codedPair) {
      var bottomRow = this._height - 1 - codedPair * 2;
      var topRow = bottomRow - 1;
      var chromaRow = chromaHeight - 1 - codedPair;
      var bottomOffset = bottomRow * this._width;
      var topOffset = topRow * this._width;
      var chromaOffset = chromaRow * chromaWidth;

      for (var x = 0; x < this._width; x += 2) {
        result[target++] = y[bottomOffset + x];
        result[target++] = y[bottomOffset + x + 1];
        result[target++] = y[topOffset + x];
        result[target++] = y[topOffset + x + 1];
        var cx = x >> 1;
        result[target++] = _EncodeChroma(cb[chromaOffset + cx]);
        result[target++] = _EncodeChroma(cr[chromaOffset + cx]);
      }
    }

    return result;
  }

  private static void _RequireFormat(RawImage frame, PixelFormat expected, string lclName) {
    if (frame.Format != expected)
      throw new NotSupportedException(
        $"LCL MSZH {lclName} preserves {expected} samples directly; source format {frame.Format} would require a colour-space or subsampling conversion.");
  }

  private static byte[] _CompressSplit(ReadOnlySpan<byte> source) {
    var sectionLength = source.Length / 2;
    var first = _Compress(source[..sectionLength]);
    var second = _Compress(source[sectionLength..]);
    var output = new byte[checked(8 + first.Length + second.Length)];
    BinaryPrimitives.WriteUInt32LittleEndian(output, checked((uint)first.Length));
    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(4), checked((uint)sectionLength));
    first.CopyTo(output, 8);
    second.CopyTo(output, 8 + first.Length);
    return output;
  }

  private static byte[] _Compress(ReadOnlySpan<byte> source) {
    if (source.Length == 0)
      return [];
    if ((source.Length & (_GROUP_BYTES - 1)) != 0)
      throw new InvalidOperationException("MSZH compression consumes whole four-byte groups.");

    var groupCount = source.Length / _GROUP_BYTES;
    var output = new byte[checked(source.Length + (groupCount + 7) / 8)];
    var previous = new Dictionary<uint, int>(groupCount);
    var sourcePosition = 0;
    var outputPosition = 0;

    while (sourcePosition < source.Length) {
      var maskPosition = outputPosition++;
      byte mask = 0;

      for (var maskBit = 0x80; maskBit != 0 && sourcePosition < source.Length; maskBit >>= 1) {
        var commandLength = _GROUP_BYTES;
        if (_TryFindMatch(source, sourcePosition, previous, out var descriptor, out var matchLength)) {
          mask |= (byte)maskBit;
          BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(outputPosition), descriptor);
          outputPosition += 2;
          commandLength = matchLength;
        } else {
          source.Slice(sourcePosition, _GROUP_BYTES).CopyTo(output.AsSpan(outputPosition));
          outputPosition += _GROUP_BYTES;
        }

        var commandEnd = sourcePosition + commandLength;
        for (var position = sourcePosition; position < commandEnd; position += _GROUP_BYTES)
          previous[BinaryPrimitives.ReadUInt32LittleEndian(source[position..])] = position;
        sourcePosition = commandEnd;
      }

      output[maskPosition] = mask;
    }

    return output.AsSpan(0, outputPosition).ToArray();
  }

  private static bool _TryFindMatch(
    ReadOnlySpan<byte> source,
    int position,
    Dictionary<uint, int> previous,
    out ushort descriptor,
    out int matchLength
  ) {
    var key = BinaryPrimitives.ReadUInt32LittleEndian(source[position..]);
    if (!previous.TryGetValue(key, out var matchPosition)) {
      descriptor = 0;
      matchLength = 0;
      return false;
    }

    var distance = position - matchPosition;
    if (distance is <= 0 or > _MAX_DISTANCE) {
      descriptor = 0;
      matchLength = 0;
      return false;
    }

    var maximum = Math.Min(_MAX_GROUPS * _GROUP_BYTES, source.Length - position);
    var length = _GROUP_BYTES;
    while (length < maximum && source[position + length] == source[position - distance + length])
      ++length;
    length &= ~(_GROUP_BYTES - 1);

    var groups = length / _GROUP_BYTES;
    descriptor = checked((ushort)(((groups - 1) << 11) | distance));
    matchLength = length;
    return true;
  }

  private static byte[] _PrivateData(
    int width,
    int height,
    int decodedSize,
    int bitsPerPixel,
    byte imageType,
    sbyte compression,
    byte flags
  ) {
    var data = new byte[BitmapInfoHeader.StructSize + LclHeader.ExtraBytes];
    var span = data.AsSpan();
    BinaryPrimitives.WriteInt32LittleEndian(span, BitmapInfoHeader.StructSize);
    BinaryPrimitives.WriteInt32LittleEndian(span[4..], width);
    BinaryPrimitives.WriteInt32LittleEndian(span[8..], height);
    BinaryPrimitives.WriteInt16LittleEndian(span[12..], 1);
    BinaryPrimitives.WriteInt16LittleEndian(span[14..], checked((short)bitsPerPixel));
    BinaryPrimitives.WriteUInt32LittleEndian(span[16..], _Tag.Value);
    BinaryPrimitives.WriteInt32LittleEndian(span[20..], decodedSize);

    var extra = span[BitmapInfoHeader.StructSize..];
    extra[0] = 4;
    extra[4] = imageType;
    extra[5] = unchecked((byte)compression);
    extra[6] = flags;
    extra[7] = _CODEC_MSZH;
    return data;
  }

  private static byte _EncodeChroma(byte value) => unchecked((byte)(value - 128));
}
