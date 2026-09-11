using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.Lcl;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes the MSZH variant of the Lossless Codec Library (LCL): RGB24 pictures are stored bottom
/// row first, padded to four-byte row alignment, then represented as groups of four-byte literals or
/// backward copies selected by one mask byte for every eight commands.
/// </summary>
/// <remarks>
/// LCL's published description identifies the wrapper, RGB24 layout and the fact that MSZH copies
/// blocks from already decoded data, but leaves the match coding as an unfilled placeholder. The
/// interoperable command layout is therefore taken as public behaviour from FFmpeg's LGPL-2.1-or-
/// later MSZH decoder: mask bits are consumed most-significant first; a zero bit carries four literal
/// bytes; a one bit carries a little-endian sixteen-bit descriptor whose low eleven bits are the byte
/// distance and whose high five bits plus one are the number of four-byte groups to reproduce.
/// <para/>
/// This encoder is an original implementation of that behaviour rather than a translation of another
/// encoder: FFmpeg itself only supplies an LCL ZLIB encoder. Matches start on four-byte boundaries,
/// use the most recent equal four-byte group within the 2047-byte window, and extend for at most the
/// format's 32 groups (128 bytes). Overlap is allowed, exactly as the decoder's backward copy allows.
/// If coding a frame would not make it smaller, the complete padded RGB24 frame is emitted instead;
/// the reference decoder explicitly recognizes that packet-size form even while the stream header
/// says MSZH compression.
/// <para/>
/// Every packet stands on its own and is therefore a key frame. The writer deliberately leaves the
/// multithread, null-frame and PNG-filter flags clear: the published description does not define the
/// split-packet length fields, null frames belong to the container, and PNG filtering is ZLIB-only.
/// YUV image types remain decoder-only until their non-standard byte order is independently verified.
/// Input follows the package's other lossless RGB encoders: losslessly convertible eight-bit colour
/// is accepted, alpha is dropped because LCL RGB24 has nowhere to store it, and deeper/YUV input is
/// refused rather than quantised or colour-transformed.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class MszhVideoEncoder : IVideoCodecEncoder<MszhVideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("MSZH");

  private const byte _IMAGE_TYPE_RGB24 = 2;
  private const byte _COMPRESSION_MSZH = 0;
  private const byte _CODEC_MSZH = 1;
  private const int _MAX_DISTANCE = 0x07ff;
  private const int _MAX_GROUPS = 32;
  private const int _GROUP_BYTES = 4;

  private readonly MediaStreamInfo _stream;
  private readonly int _width;
  private readonly int _height;
  private readonly int _packedStride;
  private readonly int _paddedStride;
  private readonly int _decodedSize;

  private MszhVideoEncoder(MediaStreamInfo stream, int packedStride, int paddedStride, int decodedSize) {
    this._width = stream.Width;
    this._height = stream.Height;
    this._packedStride = packedStride;
    this._paddedStride = paddedStride;
    this._decodedSize = decodedSize;
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
      CodecPrivateData = _PrivateData(stream.Width, stream.Height, decodedSize),
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

    var packedStrideLong = (long)stream.Width * 3;
    var paddedStrideLong = (packedStrideLong + 3) & ~3L;
    var decodedSizeLong = paddedStrideLong * stream.Height;
    if (packedStrideLong > int.MaxValue || paddedStrideLong > int.MaxValue || decodedSizeLong > int.MaxValue)
      throw new NotSupportedException(
        $"An LCL MSZH encoder cannot hold a {stream.Width}x{stream.Height} RGB24 frame in one managed byte array.");

    return new(stream, (int)packedStrideLong, (int)paddedStrideLong, (int)decodedSizeLong);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    var picture = LosslessEncoderInput.Prepare(frame, PixelFormat.Bgr24, this._width, this._height, CodecName);
    var raw = this._PackBottomUp(picture.PixelData);
    var compressed = _Compress(raw);
    var payload = compressed.Length < raw.Length ? compressed : raw;

    packet = new(
      this._stream.Index,
      payload,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: true);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  private byte[] _PackBottomUp(byte[] picture) {
    var result = new byte[this._decodedSize];
    for (var row = 0; row < this._height; ++row) {
      var sourceRow = this._height - 1 - row;
      picture.AsSpan(sourceRow * this._packedStride, this._packedStride)
        .CopyTo(result.AsSpan(row * this._paddedStride, this._packedStride));
    }

    return result;
  }

  private static byte[] _Compress(ReadOnlySpan<byte> source) {
    if (source.Length == 0)
      return [];

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

  private static byte[] _PrivateData(int width, int height, int decodedSize) {
    var data = new byte[BitmapInfoHeader.StructSize + LclHeader.ExtraBytes];
    var span = data.AsSpan();
    BinaryPrimitives.WriteInt32LittleEndian(span, BitmapInfoHeader.StructSize);
    BinaryPrimitives.WriteInt32LittleEndian(span[4..], width);
    BinaryPrimitives.WriteInt32LittleEndian(span[8..], height);
    BinaryPrimitives.WriteInt16LittleEndian(span[12..], 1);
    BinaryPrimitives.WriteInt16LittleEndian(span[14..], 24);
    BinaryPrimitives.WriteUInt32LittleEndian(span[16..], _Tag.Value);
    BinaryPrimitives.WriteInt32LittleEndian(span[20..], decodedSize);

    var extra = span[BitmapInfoHeader.StructSize..];
    extra[0] = 4;
    extra[4] = _IMAGE_TYPE_RGB24;
    extra[5] = _COMPRESSION_MSZH;
    extra[6] = 0;
    extra[7] = _CODEC_MSZH;
    return data;
  }
}
