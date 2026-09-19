using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Codecs.Mve;
using FileFormat.Core;
using FileFormat.InterplayMve;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Interplay Video (<c>IMVE</c>): the 8-bit palettised and 16-bit RGB555 0x11 block codec,
/// plus the older 0x06 and 0x10 page-update video forms carried by MVE.
/// </summary>
/// <remarks>
/// Interplay Video is temporal but not MPEG-shaped. There are no independently signalled I/P/B
/// picture types and no future-picture references. Prediction happens per 8x8 block: from an earlier
/// reconstructed page, from two decode pages back, or from an already reconstructed region of the
/// picture currently being built. The 8-bit 0x11 form naturally alternates two backing buffers; the
/// RGB555 form additionally has a separate motion-byte stream and gives block 0x6 a defined
/// second-previous signed-vector meaning.
/// <para/>
/// VIDEO_DATA reconstructs a page but does not by itself present a frame. Opcode 0x07 SEND_BUFFER is
/// the display boundary used by the public MVE description, FFmpeg and ScummVM, so <see cref="TryDecode"/>
/// returns a picture only when that opcode arrives.
/// <para/>
/// The public Interplay descriptions define the 8-bit block modes and compressed palette. FFmpeg's
/// LGPL-2.1-or-later decoder is used as the reference for the true-colour and legacy 0x06/0x10 rules
/// the public description leaves incomplete; attribution is in <c>Codecs/Mve/THIRD-PARTY-NOTICE.FFmpeg.txt</c>.
/// </remarks>
public sealed class MveVideoDecoder : IVideoCodecDecoder<MveVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("IMVE");
  private const int _OPCODE_HEADER_LENGTH = 4;
  private const int _BLOCK = 8;

  private readonly byte[] _palette = new byte[256 * 3];

  private int _width;
  private int _height;
  private int _bitsPerPixel = 8;
  private long _decodedPictures;

  // Normal 8-bit 0x11 uses the format's alternating decode buffers.
  private MveFrame? _bufferA;
  private MveFrame? _bufferB;
  private bool _nextTargetIsA = true;
  private bool _hasDecodedFirstPicture;

  // Immutable decode history is also retained for the older frame formats.
  private MveFrame? _last8;
  private MveFrame? _secondLast8;
  private MveFrame? _legacyCurrent;
  private MveFrame? _legacyPrevious;

  // RGB555 0x11 uses explicit immutable previous and second-previous references.
  private MveRgb555Frame? _last16;
  private MveRgb555Frame? _secondLast16;

  private byte[]? _decodingMap;
  private byte[]? _skipMap;
  private RawImage? _displayCandidate;

  private MveVideoDecoder(MediaStreamInfo stream) {
    if (stream.Width == 0 && stream.Height == 0)
      return;
    if (stream.Width <= 0 || stream.Height <= 0 || (stream.Width & 7) != 0 || (stream.Height & 7) != 0)
      throw new NotSupportedException("Interplay Video dimensions must be positive multiples of eight.");
    if (stream.BitsPerPixel is not (0 or 8 or 16))
      throw new NotSupportedException($"Interplay Video is 8-bit palettised or 16-bit RGB555, not {stream.BitsPerPixel} bpp.");

    this._Initialize(stream.Width, stream.Height, stream.BitsPerPixel == 16 ? 16 : 8);
  }

  public static string CodecName => "Interplay Video";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static MveVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return new(stream);
  }

  /// <summary>
  /// Accepts either one MVE opcode (the demuxer's normal packet shape) or a complete opcode sequence
  /// emitted by <see cref="MveVideoEncoder"/> for one picture.
  /// </summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;
    var at = 0;
    RawImage? result = null;

    while (at < data.Length) {
      if (data.Length - at < _OPCODE_HEADER_LENGTH)
        throw new InvalidDataException($"An Interplay MVE packet ends {data.Length - at} byte(s) into an opcode header.");

      var length = BinaryPrimitives.ReadUInt16LittleEndian(data[at..]);
      if (length > data.Length - at - _OPCODE_HEADER_LENGTH)
        throw new InvalidDataException(
          $"An Interplay MVE opcode at byte {at} says {length} payload bytes but only {data.Length - at - _OPCODE_HEADER_LENGTH} remain.");

      var type = data[at + 2];
      var version = data[at + 3];
      var payload = data.Slice(at + _OPCODE_HEADER_LENGTH, length);
      var produced = this._ProcessOpcode(type, version, payload);
      if (produced != null) {
        if (result != null)
          throw new InvalidDataException("One Interplay codec packet contains more than one displayed picture.");
        result = produced;
      }

      at += _OPCODE_HEADER_LENGTH + length;
    }

    frame = result!;
    return result != null;
  }

  private RawImage? _ProcessOpcode(byte type, byte version, ReadOnlySpan<byte> payload) {
    switch (type) {
      case MveOpcodeType.INIT_VIDEO_BUFFERS:
        this._ReadVideoBufferSize(version, payload);
        return null;
      case MveOpcodeType.SET_PALETTE:
        this._ReadPalette(payload);
        return null;
      case MveOpcodeType.SET_PALETTE_COMPRESSED:
        this._ReadCompressedPalette(payload);
        return null;
      case MveOpcodeType.SKIP_MAP:
        this._skipMap = payload.ToArray();
        return null;
      case MveOpcodeType.DECODING_MAP:
        this._decodingMap = payload.ToArray();
        return null;
      case MveOpcodeType.VIDEO_DATA_06:
        this._displayCandidate = this._Decode06(payload);
        return null;
      case MveOpcodeType.VIDEO_DATA_10:
        this._displayCandidate = this._Decode10(payload);
        return null;
      case MveOpcodeType.VIDEO_DATA_11:
        this._displayCandidate = this._Decode11(payload);
        return null;
      case MveOpcodeType.SEND_BUFFER:
        return this._displayCandidate;
      case MveOpcodeType.END_OF_CHUNK:
      case MveOpcodeType.END_OF_STREAM:
        return null;
      default:
        throw new NotSupportedException($"An Interplay MVE video packet is opcode type 0x{type:X2}, which is not codec state this decoder reads.");
    }
  }

  private void _ReadVideoBufferSize(byte version, ReadOnlySpan<byte> payload) {
    if (payload.Length < 4)
      throw new InvalidDataException($"An INIT_VIDEO_BUFFERS opcode is {payload.Length} bytes, short of the four a picture size needs.");
    if (version >= 2 && payload.Length < 8)
      throw new InvalidDataException($"A version-{version} INIT_VIDEO_BUFFERS opcode is {payload.Length} bytes, short of version 2's eight bytes.");

    var width = checked(BinaryPrimitives.ReadUInt16LittleEndian(payload) * _BLOCK);
    var height = checked(BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]) * _BLOCK);
    var bitsPerPixel = version >= 2 && BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]) != 0 ? 16 : 8;
    if (width == 0 || height == 0)
      throw new InvalidDataException($"INIT_VIDEO_BUFFERS states a picture of {width}x{height}, which has no pixels.");

    if (width == this._width && height == this._height && bitsPerPixel == this._bitsPerPixel)
      return;

    this._Initialize(width, height, bitsPerPixel);
  }

  private void _Initialize(int width, int height, int bitsPerPixel) {
    this._width = width;
    this._height = height;
    this._bitsPerPixel = bitsPerPixel;
    this._decodedPictures = 0;
    this._decodingMap = null;
    this._skipMap = null;
    this._displayCandidate = null;
    this._last8 = this._secondLast8 = null;

    if (bitsPerPixel == 8) {
      this._bufferA = new(width, height);
      this._bufferB = new(width, height);
      this._nextTargetIsA = true;
      this._hasDecodedFirstPicture = false;
      this._legacyCurrent = new(width, height);
      this._legacyPrevious = new(width, height);
      this._last16 = this._secondLast16 = null;
    } else {
      this._bufferA = this._bufferB = null;
      this._legacyCurrent = this._legacyPrevious = null;
      this._last16 = this._secondLast16 = null;
    }
  }

  private void _ReadPalette(ReadOnlySpan<byte> payload) {
    if (payload.Length < 4)
      throw new InvalidDataException($"A SET_PALETTE opcode is {payload.Length} bytes, short of the four its own header needs.");

    var start = BinaryPrimitives.ReadUInt16LittleEndian(payload);
    var count = BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]);
    var colours = payload[4..];
    if (start + count > 256 || colours.Length < count * 3)
      throw new InvalidDataException(
        $"A SET_PALETTE opcode names {count} colours starting at {start}, outside the 256-entry palette or beyond its payload.");

    for (var i = 0; i < count; ++i)
      this._InstallPaletteEntry(start + i, colours.Slice(i * 3, 3));
  }

  /// <summary>Opcode 0x0D: 32 masks, each followed by RGB triples for its set bits, low bit first.</summary>
  private void _ReadCompressedPalette(ReadOnlySpan<byte> payload) {
    var at = 0;
    for (var group = 0; group < 32; ++group) {
      if (at >= payload.Length)
        throw new InvalidDataException($"A compressed Interplay palette ends before mask {group + 1} of 32.");
      var mask = payload[at++];
      for (var bit = 0; bit < 8; ++bit) {
        if ((mask & (1 << bit)) == 0)
          continue;
        if (at > payload.Length - 3)
          throw new InvalidDataException("A compressed Interplay palette ends inside an RGB triple.");
        this._InstallPaletteEntry(group * 8 + bit, payload.Slice(at, 3));
        at += 3;
      }
    }

    if (at != payload.Length)
      throw new InvalidDataException($"A compressed Interplay palette has {payload.Length - at} unexplained trailing byte(s).");
  }

  private void _InstallPaletteEntry(int index, ReadOnlySpan<byte> rgb6) {
    if (rgb6[0] > 63 || rgb6[1] > 63 || rgb6[2] > 63)
      throw new InvalidDataException("Interplay VGA palette components are six-bit values in the range 0..63.");
    var entry = index * 3;
    this._palette[entry] = ChannelScaling.Expand6(rgb6[0]);
    this._palette[entry + 1] = ChannelScaling.Expand6(rgb6[1]);
    this._palette[entry + 2] = ChannelScaling.Expand6(rgb6[2]);
  }

  private RawImage _Decode11(ReadOnlySpan<byte> payload) {
    this._RequireGeometry();
    this._ValidateRepeatedSize(payload);
    if (this._decodingMap == null)
      throw new InvalidDataException("A VIDEO_DATA 0x11 opcode arrived before DECODING_MAP stated this picture's block encodings.");

    RawImage result;
    if (this._bitsPerPixel == 16) {
      var target = new MveRgb555Frame(this._width, this._height);
      MveRgb555BlockDecoder.Decode(payload, this._decodingMap, this._last16, this._secondLast16, target);
      this._secondLast16 = this._last16;
      this._last16 = target;
      result = this._Rgb555Image(target);
    } else {
      var target = this._nextTargetIsA ? this._bufferA! : this._bufferB!;
      var reference = this._nextTargetIsA ? this._bufferB! : this._bufferA!;
      MveBlockDecoder.Decode(this._decodingMap, payload, reference, target);

      if (!this._hasDecodedFirstPicture) {
        reference.CopyFrom(target);
        this._hasDecodedFirstPicture = true;
      }

      this._nextTargetIsA = !this._nextTargetIsA;
      this._Remember8(target);
      result = this._IndexedImage(target);
    }

    ++this._decodedPictures;
    this._decodingMap = null;
    return result;
  }

  /// <summary>Old format 0x06: embedded signed 16-bit map, then raw bytes and two motion passes.</summary>
  private RawImage _Decode06(ReadOnlySpan<byte> payload) {
    this._Require8Bit("VIDEO_DATA 0x06");
    this._ValidateRepeatedSize(payload);
    var blockCount = checked(this._width / 8 * (this._height / 8));
    var mapBytes = checked(blockCount * 2);
    if (payload.Length < 14 + mapBytes)
      throw new InvalidDataException($"VIDEO_DATA 0x06 is {payload.Length} bytes, short of its {mapBytes}-byte embedded map.");

    var map = payload.Slice(14, mapBytes);
    var data = new _ByteReader(payload, 14 + mapBytes);
    var target = new MveFrame(this._width, this._height);

    for (var pass = 0; pass < 2; ++pass)
      for (var index = 0; index < blockCount; ++index) {
        var op = BinaryPrimitives.ReadInt16LittleEndian(map[(index * 2)..]);
        var x = index % (this._width / 8) * 8;
        var y = index / (this._width / 8) * 8;
        if (pass == 0) {
          if (op == 0)
            _ReadRawBlock8(ref data, target, x, y);
          else if (this._decodedPictures > 2 && this._secondLast8 != null)
            _Copy8(this._secondLast8.Indices, target.Indices, this._width, this._height, x, y, 0, 0);
          continue;
        }

        if (op < 0 && this._last8 != null) {
          var offset = (ushort)op - 0xC000;
          _Copy8(this._last8.Indices, target.Indices, this._width, this._height, x, y, offset % this._width, offset / this._width);
        } else if (op > 0) {
          var offset = (ushort)op - 0x4000;
          _Copy8(target.Indices, target.Indices, this._width, this._height, x, y, offset % this._width, offset / this._width);
        }
      }

    this._Remember8(target);
    ++this._decodedPictures;
    return this._IndexedImage(target);
  }

  /// <summary>Old format 0x10: skip map selects changed blocks; signed 16-bit map reconstructs a hidden page.</summary>
  private RawImage _Decode10(ReadOnlySpan<byte> payload) {
    this._Require8Bit("VIDEO_DATA 0x10");
    this._ValidateRepeatedSize(payload);
    if (this._decodingMap == null)
      throw new InvalidDataException("VIDEO_DATA 0x10 arrived without its decoding map (opcode 0x0F).");
    if (this._skipMap == null)
      throw new InvalidDataException("VIDEO_DATA 0x10 arrived without its skip map (opcode 0x0E).");

    var current = this._legacyCurrent!;
    var previous = this._legacyPrevious!;
    var data = new _ByteReader(payload, 14);
    var blocksAcross = this._width / 8;
    var blockCount = checked(blocksAcross * (this._height / 8));

    for (var pass = 0; pass < 2; ++pass) {
      var mapAt = 0;
      var skip = new _SkipReader(this._skipMap);
      for (var index = 0; index < blockCount; ++index) {
        if (!skip.Next())
          continue;
        if (mapAt > this._decodingMap.Length - 2)
          throw new InvalidDataException("VIDEO_DATA 0x10 consumes more signed map entries than DECODING_MAP carries.");
        var op = BinaryPrimitives.ReadInt16LittleEndian(this._decodingMap.AsSpan(mapAt, 2));
        mapAt += 2;
        var x = index % blocksAcross * 8;
        var y = index / blocksAcross * 8;

        if (pass == 0) {
          if (op == 0)
            _ReadRawBlock8(ref data, current, x, y);
          continue;
        }

        if (op < 0) {
          var offset = (ushort)op - 0xC000;
          _Copy8(previous.Indices, current.Indices, this._width, this._height, x, y, offset % this._width, offset / this._width);
        } else if (op > 0) {
          var offset = (ushort)op - 0x4000;
          _Copy8(current.Indices, current.Indices, this._width, this._height, x, y, offset % this._width, offset / this._width);
        }
      }
    }

    var display = new MveFrame(this._width, this._height);
    var displaySkip = new _SkipReader(this._skipMap);
    for (var index = 0; index < blockCount; ++index) {
      var x = index % blocksAcross * 8;
      var y = index / blocksAcross * 8;
      if (displaySkip.Next())
        _Copy8(current.Indices, display.Indices, this._width, this._height, x, y, 0, 0);
      else if (this._last8 != null)
        _Copy8(this._last8.Indices, display.Indices, this._width, this._height, x, y, 0, 0);
    }

    (this._legacyCurrent, this._legacyPrevious) = (this._legacyPrevious, this._legacyCurrent);
    this._Remember8(display);
    ++this._decodedPictures;
    this._decodingMap = null;
    this._skipMap = null;
    return this._IndexedImage(display);
  }

  private void _RequireGeometry() {
    if (this._width == 0)
      throw new InvalidDataException("Interplay video data arrived before INIT_VIDEO_BUFFERS stated a picture size.");
  }

  private void _Require8Bit(string format) {
    this._RequireGeometry();
    if (this._bitsPerPixel != 8)
      throw new InvalidDataException($"{format} is defined only for the palettised 8-bit Interplay video path.");
  }

  private void _ValidateRepeatedSize(ReadOnlySpan<byte> payload) {
    if (payload.Length < 14)
      throw new InvalidDataException($"An Interplay video-data opcode is {payload.Length} bytes, short of its fourteen-byte header.");
    var width = BinaryPrimitives.ReadUInt16LittleEndian(payload[8..]) * 8;
    var height = BinaryPrimitives.ReadUInt16LittleEndian(payload[10..]) * 8;
    if (width != 0 && height != 0 && (width != this._width || height != this._height))
      throw new InvalidDataException(
        $"VIDEO_DATA repeats a {width}x{height} picture size but INIT_VIDEO_BUFFERS states {this._width}x{this._height}.");
  }

  private void _Remember8(MveFrame frame) {
    this._secondLast8 = this._last8;
    var copy = new MveFrame(this._width, this._height);
    copy.CopyFrom(frame);
    this._last8 = copy;
  }

  private RawImage _IndexedImage(MveFrame frame) {
    var palette = new byte[this._palette.Length];
    Array.Copy(this._palette, palette, palette.Length);
    return new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Indexed8,
      PixelData = (byte[])frame.Indices.Clone(),
      Palette = palette,
      PaletteCount = 256,
    };
  }

  private RawImage _Rgb555Image(MveRgb555Frame frame) {
    var rgb = new byte[checked(this._width * this._height * 3)];
    for (var i = 0; i < frame.Pixels.Length; ++i) {
      var pixel = frame.Pixels[i] & 0x7FFF;
      rgb[i * 3] = ChannelScaling.Expand5((pixel >> 10) & 31);
      rgb[i * 3 + 1] = ChannelScaling.Expand5((pixel >> 5) & 31);
      rgb[i * 3 + 2] = ChannelScaling.Expand5(pixel & 31);
    }

    return new() { Width = this._width, Height = this._height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private static void _ReadRawBlock8(ref _ByteReader reader, MveFrame target, int x, int y) {
    for (var row = 0; row < 8; ++row)
      for (var column = 0; column < 8; ++column)
        target.Indices[(y + row) * target.Width + x + column] = reader.Byte();
  }

  /// <summary>FFmpeg's copy rule permits one horizontal wrap into the adjacent scanline.</summary>
  private static void _Copy8(byte[] source, byte[] destination, int width, int height, int x, int y, int dx, int dy) {
    var sx = x + dx;
    var sy = y + dy;
    if (sx >= width) {
      sx -= width;
      ++sy;
    } else if (sx < 0) {
      sx += width;
      --sy;
    }

    if (sx < 0 || sy < 0 || sx + 8 > width || sy + 8 > height)
      throw new InvalidDataException(
        $"A motion-compensated Interplay block at ({x},{y}) points to ({sx},{sy}), outside the {width}x{height} picture.");
    for (var row = 0; row < 8; ++row)
      Array.Copy(source, (sy + row) * width + sx, destination, (y + row) * width + x, 8);
  }

  private ref struct _ByteReader {
    private readonly ReadOnlySpan<byte> _data;
    private int _at;

    internal _ByteReader(ReadOnlySpan<byte> data, int at) {
      this._data = data;
      this._at = at;
    }

    internal byte Byte() {
      if ((uint)this._at >= (uint)this._data.Length)
        throw new InvalidDataException("Interplay video block data ends in the middle of an 8x8 block.");
      return this._data[this._at++];
    }
  }

  /// <summary>Runs the signed-word skip bitmap used by format 0x10 exactly as the reference decoder does.</summary>
  private ref struct _SkipReader {
    private readonly ReadOnlySpan<byte> _data;
    private int _at;
    private short _value;
    private bool _started;

    internal _SkipReader(ReadOnlySpan<byte> data) {
      this._data = data;
      this._at = 0;
      this._value = 0;
      this._started = false;
    }

    internal bool Next() {
      if (!this._started) {
        this._value = this._Read();
        this._started = true;
      }

      while (this._value <= 0) {
        if (this._value != short.MinValue && this._value != 0) {
          this._value = unchecked((short)(this._value * 2));
          return true;
        }
        this._value = this._Read();
      }

      this._value = unchecked((short)(this._value * 2));
      return false;
    }

    private short _Read() {
      if (this._at > this._data.Length - 2)
        throw new InvalidDataException("Interplay VIDEO_DATA 0x10 consumes more skip-map words than opcode 0x0E carries.");
      var value = BinaryPrimitives.ReadInt16LittleEndian(this._data[this._at..]);
      this._at += 2;
      return value;
    }
  }
}
