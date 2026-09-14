using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.InterplayMve;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Interplay Video (<c>IMVE</c>) as normal 0x11 pictures in either eight-bit palettised or
/// sixteen-bit RGB555 form.
/// </summary>
/// <remarks>
/// The encoder writes one complete codec packet per source picture: optional palette, decoding map,
/// VIDEO_DATA and SEND_BUFFER. <see cref="MveWriter"/> keeps those opcodes together in one MVE video
/// chunk, which is required for the original buffering model and for FFmpeg interoperability.
/// <para/>
/// Eight-bit pictures use exact block reuse from the previous and second-previous pictures, both
/// one-byte temporal motion families, current-picture spatial copies, solid/checker/cell modes, and
/// raw blocks as the final fallback. True-colour pictures use RGB555 previous/two-back reuse plus
/// solid/cell/raw coding. There are no B-frames or future-picture references in Interplay Video:
/// every reference is backward in display time or backward within the picture being built.
/// </remarks>
public sealed class MveVideoEncoder : IVideoCodecEncoder<MveVideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("IMVE");

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;
  private readonly int _bitsPerPixel;
  private byte[]? _previous8;
  private byte[]? _secondPrevious8;
  private ushort[]? _previous16;
  private ushort[]? _secondPrevious16;

  private MveVideoEncoder(MediaStreamInfo stream) {
    this._requested = stream;
    this._width = stream.Width;
    this._height = stream.Height;
    this._bitsPerPixel = stream.BitsPerPixel == 16 ? 16 : 8;
  }

  public static string CodecName => "Interplay Video";
  public static CodecTag Codec => _Tag;

  public static MveVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Interplay Video can only encode a video stream.");
    if (!stream.Codec.EqualsIgnoringCase(_Tag))
      throw new NotSupportedException($"Interplay Video writes IMVE, not {stream.Codec}.");
    if (stream.Width <= 0 || stream.Height <= 0 || (stream.Width & 7) != 0 || (stream.Height & 7) != 0)
      throw new NotSupportedException("Interplay Video dimensions must be positive multiples of eight.");
    if (stream.BitsPerPixel is not (0 or 8 or 16))
      throw new NotSupportedException("Interplay Video is either 8-bit palettised or 16-bit RGB555.");
    if ((long)stream.Width * stream.Height > int.MaxValue)
      throw new NotSupportedException("The requested Interplay picture has more pixels than a managed frame buffer can address.");
    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"Interplay Video geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    packet = this._bitsPerPixel == 16
      ? this._Encode16(frame, presentationTimestamp)
      : this._Encode8(frame, presentationTimestamp);
    return true;
  }

  public MediaStreamInfo DescribeStream() => new() {
    Index = this._requested.Index,
    Kind = MediaStreamKind.Video,
    Codec = _Tag,
    Handler = _Tag,
    TimeBase = this._requested.TimeBase,
    FrameRate = this._requested.FrameRate,
    DeclaredFrameCount = this._requested.DeclaredFrameCount,
    Width = this._width,
    Height = this._height,
    BitsPerPixel = this._bitsPerPixel,
    Language = this._requested.Language,
    Name = this._requested.Name,
  };

  private CodedPacket _Encode8(RawImage frame, long? timestamp) {
    var (indices, palette) = this._Indexed(frame);
    var blockCount = checked(this._width / 8 * (this._height / 8));
    var types = new byte[blockCount];
    using var body = new MemoryStream();
    var temporal = false;

    for (var block = 0; block < blockCount; ++block) {
      var x = block % (this._width / 8) * 8;
      var y = block / (this._width / 8) * 8;
      if (this._secondPrevious8 != null && _EqualBlock(indices, this._secondPrevious8, this._width, x, y, 0, 0)) {
        types[block] = 0x1;
        temporal = true;
        continue;
      }
      if (this._previous8 != null && _EqualBlock(indices, this._previous8, this._width, x, y, 0, 0)) {
        types[block] = 0x0;
        temporal = true;
        continue;
      }
      if (this._secondPrevious8 != null && _TryFarMotion(indices, this._secondPrevious8, this._width, x, y, negate: false, out var farBack)) {
        types[block] = 0x2;
        body.WriteByte(farBack);
        temporal = true;
        continue;
      }
      if (_TryFarMotion(indices, indices, this._width, x, y, negate: true, out var spatial)) {
        types[block] = 0x3;
        body.WriteByte(spatial);
        continue;
      }
      if (this._previous8 != null && _TryShortMotion(indices, this._previous8, this._width, x, y, out var shortMotion)) {
        types[block] = 0x4;
        body.WriteByte(shortMotion);
        temporal = true;
        continue;
      }
      if (_Solid8(indices, this._width, x, y, out var solid)) {
        types[block] = 0xE;
        body.WriteByte(solid);
        continue;
      }
      if (_Checker8(indices, this._width, x, y, out var p0, out var p1)) {
        types[block] = 0xF;
        body.WriteByte(p0);
        body.WriteByte(p1);
        continue;
      }
      if (_Cells8(indices, this._width, x, y, 4, out var cells4)) {
        types[block] = 0xD;
        body.Write(cells4);
        continue;
      }
      if (_Cells8(indices, this._width, x, y, 2, out var cells2)) {
        types[block] = 0xC;
        body.Write(cells2);
        continue;
      }

      types[block] = 0xB;
      _WriteRaw8(body, indices, this._width, x, y);
    }

    var video = this._VideoData8(body.ToArray());
    var packetData = _Join(
      _PaletteOpcode(palette),
      _Opcode(MveOpcodeType.DECODING_MAP, 0, _PackMap(types)),
      _Opcode(MveOpcodeType.VIDEO_DATA_11, 0, video),
      _SendBuffer());
    _CheckPacketSizes(packetData, video.Length);

    this._secondPrevious8 = this._previous8;
    this._previous8 = indices;
    return new(this._requested.Index, packetData, timestamp, timestamp, IsKeyFrame: !temporal);
  }

  private CodedPacket _Encode16(RawImage frame, long? timestamp) {
    var pixels = this._Rgb555(frame);
    var blockCount = checked(this._width / 8 * (this._height / 8));
    var types = new byte[blockCount];
    using var body = new MemoryStream();
    var temporal = false;

    for (var block = 0; block < blockCount; ++block) {
      var x = block % (this._width / 8) * 8;
      var y = block / (this._width / 8) * 8;
      if (this._secondPrevious16 != null && _EqualBlock16(pixels, this._secondPrevious16, this._width, x, y)) {
        types[block] = 0x1;
        temporal = true;
        continue;
      }
      if (this._previous16 != null && _EqualBlock16(pixels, this._previous16, this._width, x, y)) {
        types[block] = 0x0;
        temporal = true;
        continue;
      }
      if (_Solid16(pixels, this._width, x, y, out var solid)) {
        types[block] = 0xE;
        _Word(body, solid);
        continue;
      }
      if (_Cells16(pixels, this._width, x, y, 4, out var cells4)) {
        types[block] = 0xD;
        foreach (var value in cells4)
          _Word(body, value);
        continue;
      }
      if (_Cells16(pixels, this._width, x, y, 2, out var cells2)) {
        types[block] = 0xC;
        foreach (var value in cells2)
          _Word(body, value);
        continue;
      }

      types[block] = 0xB;
      _WriteRaw16(body, pixels, this._width, x, y);
    }

    var colourData = body.ToArray();
    var video = new byte[checked(16 + colourData.Length)];
    BinaryPrimitives.WriteUInt16LittleEndian(video.AsSpan(8), checked((ushort)(this._width / 8)));
    BinaryPrimitives.WriteUInt16LittleEndian(video.AsSpan(10), checked((ushort)(this._height / 8)));
    BinaryPrimitives.WriteUInt16LittleEndian(video.AsSpan(14), checked((ushort)(2 + colourData.Length)));
    colourData.CopyTo(video, 16);

    var packetData = _Join(
      _Opcode(MveOpcodeType.DECODING_MAP, 0, _PackMap(types)),
      _Opcode(MveOpcodeType.VIDEO_DATA_11, 0, video),
      _SendBuffer());
    _CheckPacketSizes(packetData, video.Length);

    this._secondPrevious16 = this._previous16;
    this._previous16 = pixels;
    return new(this._requested.Index, packetData, timestamp, timestamp, IsKeyFrame: !temporal);
  }

  private (byte[] Indices, byte[] Palette) _Indexed(RawImage frame) {
    if (frame.Format == PixelFormat.Indexed8) {
      if (frame.Palette == null || frame.PaletteCount is <= 0 or > 256)
        throw new InvalidDataException("An Indexed8 Interplay source needs between one and 256 palette entries.");
      if (frame.Palette.Length < frame.PaletteCount * 3)
        throw new InvalidDataException("The source Indexed8 palette is shorter than its declared PaletteCount.");

      var indices = frame.PixelData.AsSpan(0, checked(this._width * this._height)).ToArray();
      for (var i = 0; i < indices.Length; ++i)
        if (indices[i] >= frame.PaletteCount)
          throw new InvalidDataException($"Palette index {indices[i]} at pixel {i} lies outside the declared {frame.PaletteCount}-entry palette.");

      var palette = new byte[256 * 3];
      frame.Palette.AsSpan(0, frame.PaletteCount * 3).CopyTo(palette);
      return (indices, palette);
    }

    var bgra = frame.ToBgra32();
    for (var i = 3; i < bgra.Length; i += 4)
      bgra[i] = 255;
    var quantized = ColorQuantizer.Quantize(bgra, checked(this._width * this._height), 256);
    var palette256 = new byte[256 * 3];
    quantized.Palette.CopyTo(palette256, 0);
    var packed = ColorQuantizer.PackIndices(quantized.Indices, PixelFormat.Indexed8);
    return (packed, palette256);
  }

  private byte[] _VideoData8(byte[] blockData) {
    var payload = new byte[checked(14 + blockData.Length)];
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8), checked((ushort)(this._width / 8)));
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(10), checked((ushort)(this._height / 8)));
    blockData.CopyTo(payload, 14);
    return payload;
  }

  private ushort[] _Rgb555(RawImage frame) {
    var rgb = frame.ToRgb24();
    var result = new ushort[checked(this._width * this._height)];
    for (var i = 0; i < result.Length; ++i) {
      var r = _Reduce5(rgb[i * 3]);
      var g = _Reduce5(rgb[i * 3 + 1]);
      var b = _Reduce5(rgb[i * 3 + 2]);
      result[i] = (ushort)((r << 10) | (g << 5) | b);
    }
    return result;
  }

  private static byte[] _PaletteOpcode(byte[] palette) {
    var payload = new byte[4 + 256 * 3];
    BinaryPrimitives.WriteUInt16LittleEndian(payload, 0);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2), 256);
    for (var i = 0; i < 256 * 3; ++i)
      payload[4 + i] = _Reduce6(palette[i]);
    return _Opcode(MveOpcodeType.SET_PALETTE, 0, payload);
  }

  private static byte[] _SendBuffer() {
    var payload = new byte[4];
    BinaryPrimitives.WriteUInt16LittleEndian(payload, 0);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2), 256);
    return _Opcode(MveOpcodeType.SEND_BUFFER, 0, payload);
  }

  private static byte[] _Opcode(byte type, byte version, ReadOnlySpan<byte> payload) {
    if (payload.Length > ushort.MaxValue)
      throw new NotSupportedException($"Interplay opcode 0x{type:X2} would contain {payload.Length} bytes; its length field is 16-bit.");
    var result = new byte[payload.Length + 4];
    BinaryPrimitives.WriteUInt16LittleEndian(result, (ushort)payload.Length);
    result[2] = type;
    result[3] = version;
    payload.CopyTo(result.AsSpan(4));
    return result;
  }

  private static byte[] _Join(params byte[][] parts) {
    var length = 0;
    foreach (var part in parts)
      length = checked(length + part.Length);
    var result = new byte[length];
    var at = 0;
    foreach (var part in parts) {
      part.CopyTo(result, at);
      at += part.Length;
    }
    return result;
  }

  private static byte[] _PackMap(ReadOnlySpan<byte> types) {
    var result = new byte[(types.Length + 1) / 2];
    for (var i = 0; i < types.Length; ++i)
      if ((i & 1) == 0)
        result[i >> 1] = (byte)(types[i] & 0x0F);
      else
        result[i >> 1] |= (byte)((types[i] & 0x0F) << 4);
    return result;
  }

  private static void _CheckPacketSizes(byte[] packet, int videoPayloadLength) {
    if (videoPayloadLength > ushort.MaxValue)
      throw new NotSupportedException(
        $"This Interplay picture needs {videoPayloadLength} VIDEO_DATA bytes after block coding; the format's opcode length is limited to 65,535.");
    if (packet.Length > ushort.MaxValue - 4)
      throw new NotSupportedException(
        $"This Interplay picture needs {packet.Length} codec bytes; it cannot fit with END_OF_CHUNK in one 65,535-byte MVE chunk.");
  }

  private static bool _EqualBlock(byte[] current, byte[] reference, int width, int x, int y, int dx, int dy) {
    var sx = x + dx;
    var sy = y + dy;
    var height = current.Length / width;
    if (sx < 0 || sy < 0 || sx + 8 > width || sy + 8 > height)
      return false;
    for (var row = 0; row < 8; ++row)
      if (!current.AsSpan((y + row) * width + x, 8).SequenceEqual(reference.AsSpan((sy + row) * width + sx, 8)))
        return false;
    return true;
  }

  private static bool _TryShortMotion(byte[] current, byte[] previous, int width, int x, int y, out byte packed) {
    for (var dy = -8; dy <= 7; ++dy)
      for (var dx = -8; dx <= 7; ++dx) {
        if (dx == 0 && dy == 0)
          continue;
        if (!_EqualBlock(current, previous, width, x, y, dx, dy))
          continue;
        packed = (byte)(((dy + 8) << 4) | (dx + 8));
        return true;
      }
    packed = 0;
    return false;
  }

  private static bool _TryFarMotion(byte[] current, byte[] reference, int width, int x, int y, bool negate, out byte packed) {
    for (var value = 0; value < 256; ++value) {
      var (fx, fy) = _FarVector((byte)value);
      var dx = negate ? -fx : fx;
      var dy = negate ? -fy : fy;
      var sx = x + dx;
      var sy = y + dy;
      if (negate && !(sy < y || sy == y && sx < x))
        continue;
      if (_EqualBlock(current, reference, width, x, y, dx, dy)) {
        packed = (byte)value;
        return true;
      }
    }
    packed = 0;
    return false;
  }

  private static (int X, int Y) _FarVector(byte value) {
    if (value < 56)
      return (8 + value % 7, value / 7);
    var adjusted = value - 56;
    return (-14 + adjusted % 29, 8 + adjusted / 29);
  }

  private static bool _Solid8(byte[] data, int width, int x, int y, out byte value) {
    value = data[y * width + x];
    for (var row = 0; row < 8; ++row)
      for (var column = 0; column < 8; ++column)
        if (data[(y + row) * width + x + column] != value)
          return false;
    return true;
  }

  private static bool _Checker8(byte[] data, int width, int x, int y, out byte p0, out byte p1) {
    p0 = data[y * width + x];
    p1 = data[y * width + x + 1];
    if (p0 == p1)
      return false;
    for (var row = 0; row < 8; ++row)
      for (var column = 0; column < 8; ++column)
        if (data[(y + row) * width + x + column] != (((row + column) & 1) == 0 ? p0 : p1))
          return false;
    return true;
  }

  private static bool _Cells8(byte[] data, int width, int x, int y, int cellSize, out byte[] cells) {
    var cellsAcross = 8 / cellSize;
    cells = new byte[cellsAcross * cellsAcross];
    var at = 0;
    for (var cellY = 0; cellY < cellsAcross; ++cellY)
      for (var cellX = 0; cellX < cellsAcross; ++cellX) {
        var value = data[(y + cellY * cellSize) * width + x + cellX * cellSize];
        for (var row = 0; row < cellSize; ++row)
          for (var column = 0; column < cellSize; ++column)
            if (data[(y + cellY * cellSize + row) * width + x + cellX * cellSize + column] != value) {
              cells = [];
              return false;
            }
        cells[at++] = value;
      }
    return true;
  }

  private static void _WriteRaw8(Stream output, byte[] data, int width, int x, int y) {
    for (var row = 0; row < 8; ++row)
      output.Write(data, (y + row) * width + x, 8);
  }

  private static bool _EqualBlock16(ushort[] current, ushort[] reference, int width, int x, int y) {
    for (var row = 0; row < 8; ++row)
      if (!current.AsSpan((y + row) * width + x, 8).SequenceEqual(reference.AsSpan((y + row) * width + x, 8)))
        return false;
    return true;
  }

  private static bool _Solid16(ushort[] data, int width, int x, int y, out ushort value) {
    value = data[y * width + x];
    for (var row = 0; row < 8; ++row)
      for (var column = 0; column < 8; ++column)
        if (data[(y + row) * width + x + column] != value)
          return false;
    return true;
  }

  private static bool _Cells16(ushort[] data, int width, int x, int y, int cellSize, out ushort[] cells) {
    var cellsAcross = 8 / cellSize;
    cells = new ushort[cellsAcross * cellsAcross];
    var at = 0;
    for (var cellY = 0; cellY < cellsAcross; ++cellY)
      for (var cellX = 0; cellX < cellsAcross; ++cellX) {
        var value = data[(y + cellY * cellSize) * width + x + cellX * cellSize];
        for (var row = 0; row < cellSize; ++row)
          for (var column = 0; column < cellSize; ++column)
            if (data[(y + cellY * cellSize + row) * width + x + cellX * cellSize + column] != value) {
              cells = [];
              return false;
            }
        cells[at++] = value;
      }
    return true;
  }

  private static void _WriteRaw16(Stream output, ushort[] data, int width, int x, int y) {
    for (var row = 0; row < 8; ++row)
      for (var column = 0; column < 8; ++column)
        _Word(output, data[(y + row) * width + x + column]);
  }

  private static void _Word(Stream output, ushort value) {
    output.WriteByte((byte)value);
    output.WriteByte((byte)(value >> 8));
  }

  private static int _Reduce5(byte value) => (value * 31 + 127) / 255;
  private static byte _Reduce6(byte value) => (byte)((value * 63 + 127) / 255);
}
