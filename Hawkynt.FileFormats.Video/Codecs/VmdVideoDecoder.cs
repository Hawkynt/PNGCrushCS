using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Codecs.Vmd;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Decodes classic eight-bit Sierra VMD video onto its persistent palettised canvas.</summary>
/// <remarks>
/// Converted from FFmpeg's LGPL-2.1-or-later <c>libavcodec/vmdvideo.c</c>, with the palette-update
/// count semantics cross-checked against ScummVM and MultimediaWiki; provenance is recorded in
/// <c>Codecs/Vmd/THIRD-PARTY-NOTICE.FFmpeg.txt</c>.
/// <para/>
/// VMD has no bidirectional pictures and no future references. Method 2 writes a rectangle directly;
/// methods 1 and 3 copy unchanged runs from the preceding decoded picture and replace the other runs.
/// The rectangle itself may cover only part of the canvas, so pixels outside it implicitly remain from
/// the previous picture as well. A first picture that actually asks for a previous-picture run is
/// malformed and refuses instead of treating the missing reference as black.
/// <para/>
/// Both LZ initialisations are accepted: the preload-marker form and the markerless form. Method 3's
/// inner pair-RLE and the 770-byte mid-stream palette record are decoded too. Codec versions 5 and 13
/// are true-colour VMD variants and remain outside this decoder; version 7 is Indeo 3 and is exposed by
/// the VMD container as Indeo 3 rather than routed through this codec.
/// </remarks>
public sealed class VmdVideoDecoder : IVideoCodecDecoder<VmdVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("VMDV");

  private const int _RECORD_LENGTH = 16;
  private const int _HEADER_LENGTH = 816;
  private const int _HEADER_CODEC_VERSION_OFFSET = 4;
  private const int _HEADER_PALETTE_OFFSET = 28;
  private const int _HEADER_PALETTE_LENGTH = 768;
  private const int _HEADER_UNPACK_BUFFER_SIZE_OFFSET = 800;
  private const int _SUPPORTED_CODEC_VERSION = 1;
  private const int _PALETTE_UPDATE_LENGTH = 770;

  private const byte _NEW_PALETTE_FLAG = 0x02;
  private const byte _LZ_FLAG = 0x80;
  private const byte _METHOD_MASK = 0x7F;
  private const byte _METHOD_SPARSE = 1;
  private const byte _METHOD_PLAIN = 2;
  private const byte _METHOD_RLE = 3;

  private readonly byte[] _palette;
  private readonly byte[] _canvas;
  private readonly int _width;
  private readonly int _height;
  private readonly int _unpackBufferSize;

  private bool _hasPreviousFrame;
  private int _xOffset;
  private int _yOffset;

  public static string CodecName => "Sierra VMD Video";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static VmdVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    var header = stream.CodecPrivateData.Span;
    if (header.Length < _HEADER_LENGTH)
      throw new InvalidDataException(
        $"A Sierra VMD video stream carries {header.Length} bytes of private data, not its classic {_HEADER_LENGTH}-byte header.");

    var codecVersion = BinaryPrimitives.ReadUInt16LittleEndian(header[_HEADER_CODEC_VERSION_OFFSET..]);
    if (codecVersion != _SUPPORTED_CODEC_VERSION)
      throw new NotSupportedException(
        $"This Sierra VMD stream states video codec version {codecVersion}. This decoder reads version 1, "
        + "the classic eight-bit palettised codec; versions 5 and 13 are true-colour and version 7 is Indeo 3.");

    if (stream.Width <= 0 || stream.Height <= 0 || (long)stream.Width * stream.Height > int.MaxValue)
      throw new InvalidDataException(
        $"A Sierra VMD video stream states an unusable picture size of {stream.Width}x{stream.Height}.");

    var palette = new byte[_HEADER_PALETTE_LENGTH];
    _ReadPalette(header.Slice(_HEADER_PALETTE_OFFSET, _HEADER_PALETTE_LENGTH), palette, 0, 256);

    var unpackBufferSize = checked((int)Math.Min(
      BinaryPrimitives.ReadUInt32LittleEndian(header[_HEADER_UNPACK_BUFFER_SIZE_OFFSET..]),
      int.MaxValue));

    return new(palette, stream.Width, stream.Height, unpackBufferSize);
  }

  private VmdVideoDecoder(byte[] palette, int width, int height, int unpackBufferSize) {
    this._palette = palette;
    this._width = width;
    this._height = height;
    this._unpackBufferSize = unpackBufferSize;
    this._canvas = new byte[checked(width * height)];
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;
    if (data.Length < _RECORD_LENGTH)
      throw new InvalidDataException(
        $"A Sierra VMD video packet is {data.Length} bytes, short of its sixteen-byte frame-information record.");

    var rawLeft = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
    var rawTop = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
    var rawRight = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]);
    var rawBottom = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
    if (rawRight < rawLeft || rawBottom < rawTop)
      throw new InvalidDataException(
        $"A Sierra VMD video packet states an inverted rectangle ({rawLeft},{rawTop})-({rawRight},{rawBottom}).");

    var width = rawRight - rawLeft + 1;
    var height = rawBottom - rawTop + 1;
    if (width == this._width && height == this._height && (rawLeft != 0 || rawTop != 0)) {
      this._xOffset = rawLeft;
      this._yOffset = rawTop;
    }

    var left = rawLeft - this._xOffset;
    var top = rawTop - this._yOffset;

    var payload = data[_RECORD_LENGTH..];
    if ((data[15] & _NEW_PALETTE_FLAG) != 0) {
      if (payload.Length < _PALETTE_UPDATE_LENGTH)
        throw new InvalidDataException(
          $"A Sierra VMD palette update is {payload.Length} bytes, short of its {_PALETTE_UPDATE_LENGTH}-byte layout.");

      var first = payload[0];
      var count = payload[1] + 1;
      if (first + count > 256)
        throw new InvalidDataException(
          $"A Sierra VMD palette update starts at {first} and contains {count} entries, past palette entry 255.");

      _ReadPalette(payload.Slice(2, _HEADER_PALETTE_LENGTH), this._palette, first, count);
      payload = payload[_PALETTE_UPDATE_LENGTH..];
    }

    if (payload.IsEmpty)
      throw new InvalidDataException("A Sierra VMD video rectangle contains no rendering-method byte.");

    var methodByte = payload[0];
    var method = (byte)(methodByte & _METHOD_MASK);
    ReadOnlySpan<byte> rectangleData = payload[1..];

    if ((methodByte & _LZ_FLAG) != 0) {
      if (this._unpackBufferSize <= 0)
        throw new InvalidDataException(
          "A Sierra VMD picture is LZ-compressed, but the stream header declares no unpack buffer.");
      rectangleData = VmdLzDecoder.Decode(rectangleData, this._unpackBufferSize);
    }

    switch (method) {
      case _METHOD_SPARSE:
        VmdRowCoder.DecodeMethod1(
          rectangleData, this._canvas, this._width, this._height,
          left, top, width, height, this._hasPreviousFrame);
        break;
      case _METHOD_PLAIN:
        VmdRowCoder.DecodeMethod2(
          rectangleData, this._canvas, this._width, this._height,
          left, top, width, height);
        break;
      case _METHOD_RLE:
        VmdRowCoder.DecodeMethod3(
          rectangleData, this._canvas, this._width, this._height,
          left, top, width, height, this._hasPreviousFrame);
        break;
      default:
        throw new NotSupportedException(
          $"This Sierra VMD version-1 picture states rendering method {method}, which the classic codec does not define here.");
    }

    this._hasPreviousFrame = true;
    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Indexed8,
      PixelData = (byte[])this._canvas.Clone(),
      Palette = (byte[])this._palette.Clone(),
      PaletteCount = 256,
    };
    return true;
  }

  private static void _ReadPalette(ReadOnlySpan<byte> source, Span<byte> destination, int first, int count) {
    var needed = count * 3;
    if (source.Length < needed)
      throw new InvalidDataException(
        $"A Sierra VMD palette carries {source.Length / 3} RGB triplets where {count} are required.");

    for (var entry = 0; entry < count; ++entry) {
      var sourceOffset = entry * 3;
      var destinationOffset = (first + entry) * 3;
      for (var channel = 0; channel < 3; ++channel) {
        var value = source[sourceOffset + channel];
        if (value > 63)
          throw new InvalidDataException(
            $"A Sierra VMD palette channel contains {value}; VGA DAC components are six-bit values from 0 through 63.");
        destination[destinationOffset + channel] = ChannelScaling.Expand6(value);
      }
    }
  }
}
