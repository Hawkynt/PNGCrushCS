using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Codecs.Roq;
using FileFormat.Core;
using FileFormat.Jpeg;
using FileFormat.RoqVideo;

namespace FileFormat.Codecs;

/// <summary>Decodes standard id RoQ and the older Trilobyte RoQ video extensions.</summary>
/// <remarks>
/// RoQ has no B-picture or future-reference syntax. QUAD_VQ pictures use two reconstruction buffers:
/// FCC reads the immediately preceding displayed picture, while MOT writes nothing and therefore keeps
/// the target buffer's older contents. The Trilobyte form additionally permits alpha-bearing codebooks,
/// JPEG intraframes, HANG repeat pictures and a signature variant that doubles motion-vector offsets.
/// </remarks>
public sealed class RoqVideoDecoder : IVideoCodecDecoder<RoqVideoDecoder> {
  private static readonly CodecTag _Tag = CodecTag.FromCharacters("RoQV");
  private const int _HeaderLength = 8;
  private const int _InfoLength = 8;
  private const int _Macroblock = 16;

  private readonly RoqCodebook _codebook = new();
  private readonly int _motionScale;
  private int _width;
  private int _height;
  private bool _hasAlpha;
  private RoqFrame? _bufferA;
  private RoqFrame? _bufferB;
  private RoqFrame? _lastDisplayed;
  private bool _nextTargetIsA = true;
  private bool _hasDecodedFirstPicture;

  private RoqVideoDecoder(int motionScale) => this._motionScale = motionScale;

  public static string CodecName => "id RoQ";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static RoqVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    var privateData = stream.CodecPrivateData.Span;
    var motionScale = privateData.Length > 0 && privateData[0] == 2 ? 2 : 1;
    return new(motionScale);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;
    var at = 0;
    RawImage? picture = null;

    while (at < data.Length) {
      if (data.Length - at < _HeaderLength)
        throw new InvalidDataException($"A RoQ packet ends with {data.Length - at} bytes, short of an eight-byte chunk header.");

      var header = data[at..];
      var id = BinaryPrimitives.ReadUInt16LittleEndian(header);
      var size = BinaryPrimitives.ReadUInt32LittleEndian(header[2..]);
      var argument = BinaryPrimitives.ReadUInt16LittleEndian(header[6..]);
      if (size > int.MaxValue || size > data.Length - at - _HeaderLength)
        throw new InvalidDataException($"RoQ chunk 0x{id:X4} states {size} payload bytes but only {data.Length - at - _HeaderLength} remain.");

      var payload = data.Slice(at + _HeaderLength, (int)size);
      RawImage? decoded = id switch {
        RoqChunkType.INFO => this._ReadInfo(payload, argument),
        RoqChunkType.QUAD_CODEBOOK => this._ReadCodebook(payload, argument),
        RoqChunkType.QUAD_VQ => this._DecodeVq(payload, argument),
        RoqChunkType.JPEG => this._DecodeJpeg(payload),
        RoqChunkType.HANG => this._DecodeHang(payload, argument),
        _ => throw new NotSupportedException($"A RoQ video packet contains chunk type 0x{id:X4}, which is not a video chunk this decoder reads."),
      };

      if (decoded is not null) {
        if (picture is not null)
          throw new InvalidDataException("One RoQ coded packet contains more than one picture; the codec API can return only one picture per packet.");
        picture = decoded;
      }

      at += _HeaderLength + (int)size;
    }

    frame = picture!;
    return picture is not null;
  }

  private RawImage? _ReadInfo(ReadOnlySpan<byte> payload, ushort argument) {
    if (payload.Length < _InfoLength)
      throw new InvalidDataException($"A RoQ_INFO chunk is {payload.Length} bytes, short of its eight-byte payload.");
    if (argument > 1)
      throw new NotSupportedException($"RoQ_INFO argument {argument} is neither the standard 0 nor the alpha-codebook value 1.");

    var width = BinaryPrimitives.ReadUInt16LittleEndian(payload);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]);
    var block = BinaryPrimitives.ReadUInt16LittleEndian(payload[4..]);
    var subBlock = BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]);
    if (width == 0 || height == 0)
      throw new InvalidDataException($"RoQ_INFO states a picture of {width}x{height}, which has no pixels.");
    if (block != 8 || subBlock != 4)
      throw new NotSupportedException($"RoQ_INFO states block fields {block}/{subBlock}; only the established 8/4 quadtree is defined.");
    if (width % _Macroblock != 0 || height % _Macroblock != 0)
      throw new NotSupportedException($"RoQ_INFO states {width}x{height}, not a whole number of {_Macroblock}-pixel macroblocks.");

    var hasAlpha = argument == 1;
    if (width == this._width && height == this._height && hasAlpha == this._hasAlpha)
      return null;

    this._codebook.Configure(hasAlpha);
    this._width = width;
    this._height = height;
    this._hasAlpha = hasAlpha;
    this._bufferA = new(width, height, hasAlpha);
    this._bufferB = new(width, height, hasAlpha);
    this._lastDisplayed = null;
    this._nextTargetIsA = true;
    this._hasDecodedFirstPicture = false;
    return null;
  }

  private RawImage? _ReadCodebook(ReadOnlySpan<byte> payload, ushort argument) {
    this._RequireInfo(RoqChunkType.QUAD_CODEBOOK);
    this._codebook.Replace(payload, argument);
    return null;
  }

  private RawImage _DecodeVq(ReadOnlySpan<byte> payload, ushort argument) {
    this._RequireInfo(RoqChunkType.QUAD_VQ);
    var target = this._nextTargetIsA ? this._bufferA! : this._bufferB!;
    var reference = this._nextTargetIsA ? this._bufferB! : this._bufferA!;
    RoqPictureDecoder.Decode(payload, this._codebook, (sbyte)(argument >> 8), (sbyte)argument, reference, target, this._motionScale);
    this._FinishPicture(target, reference);
    return RoqColorConversion.ToRawImage(target);
  }

  private RawImage _DecodeJpeg(ReadOnlySpan<byte> payload) {
    this._RequireInfo(RoqChunkType.JPEG);
    var image = JpegFile.ToRawImage(JpegReader.FromSpan(payload));
    if (image.Width != this._width || image.Height != this._height)
      throw new InvalidDataException($"RoQ_JPEG is {image.Width}x{image.Height}, while RoQ_INFO states {this._width}x{this._height}.");

    var rgb = image.EnsureFormat(PixelFormat.Rgb24);
    var target = this._nextTargetIsA ? this._bufferA! : this._bufferB!;
    var reference = this._nextTargetIsA ? this._bufferB! : this._bufferA!;
    RoqColorConversion.FromRgb24(rgb.PixelData, target);
    target.MakeOpaque();
    this._FinishPicture(target, reference);
    return this._hasAlpha ? RoqColorConversion.ToRawImage(target) : rgb;
  }

  private RawImage _DecodeHang(ReadOnlySpan<byte> payload, ushort argument) {
    if (!payload.IsEmpty || argument != 0)
      throw new InvalidDataException($"RoQ_HANG must be empty with argument zero; got {payload.Length} bytes and 0x{argument:X4}.");
    if (this._lastDisplayed is null)
      throw new InvalidDataException("RoQ_HANG appears before any picture exists to repeat.");
    return RoqColorConversion.ToRawImage(this._lastDisplayed);
  }

  private void _FinishPicture(RoqFrame target, RoqFrame reference) {
    if (!this._hasDecodedFirstPicture) {
      reference.CopyFrom(target);
      this._hasDecodedFirstPicture = true;
    }
    this._lastDisplayed = target;
    this._nextTargetIsA = !this._nextTargetIsA;
  }

  private void _RequireInfo(ushort chunkType) {
    if (this._bufferA is null)
      throw new InvalidDataException($"RoQ chunk 0x{chunkType:X4} arrived before RoQ_INFO stated the picture format.");
  }
}
