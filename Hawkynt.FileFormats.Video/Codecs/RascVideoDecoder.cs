using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Decodes RemotelyAnywhere Screen Capture (<c>RASC</c>) video.</summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/rasc.c</c>, copyright (c) 2018 Paul B Mahol, distributed
/// there under LGPL-2.1-or-later. This adaptation is distributed with PNGCrushCS under
/// LGPL-3.0-or-later.
/// <para/>
/// RASC is a stateful screen codec. Packets contain tagged records which can initialize or resize
/// the canvas, replace both reference surfaces with a zlib keyframe, apply byte/word delta runs,
/// move rectangles, or update/draw a cursor. The coded canvas is PAL8, RGB555LE or BGR0; the public
/// decoder boundary converts all three to RGB24 after applying the optional cursor overlay.
/// </remarks>
public sealed class RascVideoDecoder : IVideoCodecDecoder<RascVideoDecoder> {

  private const uint _KBND = (uint)'K' | ((uint)'B' << 8) | ((uint)'N' << 16) | ((uint)'D' << 24);
  private const uint _FINT = (uint)'F' | ((uint)'I' << 8) | ((uint)'N' << 16) | ((uint)'T' << 24);
  private const uint _INIT = (uint)'I' | ((uint)'N' << 8) | ((uint)'I' << 16) | ((uint)'T' << 24);
  private const uint _BNDL = (uint)'B' | ((uint)'N' << 8) | ((uint)'D' << 16) | ((uint)'L' << 24);
  private const uint _KFRM = (uint)'K' | ((uint)'F' << 8) | ((uint)'R' << 16) | ((uint)'M' << 24);
  private const uint _DLTA = (uint)'D' | ((uint)'L' << 8) | ((uint)'T' << 16) | ((uint)'A' << 24);
  private const uint _MOUS = (uint)'M' | ((uint)'O' << 8) | ((uint)'U' << 16) | ((uint)'S' << 24);
  private const uint _MPOS = (uint)'M' | ((uint)'P' << 8) | ((uint)'O' << 16) | ((uint)'S' << 24);
  private const uint _MOVE = (uint)'M' | ((uint)'O' << 8) | ((uint)'V' << 16) | ((uint)'E' << 24);
  private const uint _EMPT = (uint)'E' | ((uint)'M' << 8) | ((uint)'P' << 16) | ((uint)'T' << 24);

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("RASC");

  private readonly int _streamIndex;
  private int _width;
  private int _height;
  private int _bytesPerPixel;
  private int _stride;
  private NativeFormat _format;
  private byte[]? _frame1;
  private byte[]? _frame2;
  private byte[]? _palette;
  private byte[]? _cursor;
  private int _cursorWidth;
  private int _cursorHeight;
  private int _cursorX;
  private int _cursorY;

  private RascVideoDecoder(int streamIndex) => this._streamIndex = streamIndex;

  public static string CodecName => "RemotelyAnywhere Screen Capture";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static RascVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return new(stream.Index);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var source = packet.Data.Span;
    if (source.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(source) == _EMPT) {
      frame = default!;
      return false;
    }

    var reader = new LittleEndianReader(source);
    while (reader.Remaining > 0) {
      if (reader.Remaining < 8)
        throw new InvalidDataException("A RASC packet ends inside a record header.");

      var type = reader.ReadUInt32();
      if (type is _KBND or _BNDL) {
        if (reader.Remaining < 8)
          throw new InvalidDataException("A RASC bundle ends before its nested record header.");
        type = reader.ReadUInt32();
      }

      var size = reader.ReadUInt32();
      if (size > int.MaxValue || reader.Remaining < size)
        throw new InvalidDataException(
          $"A RASC record states {size} payload byte(s), but only {reader.Remaining} remain.");

      var payload = reader.ReadSpan(checked((int)size));
      switch (type) {
        case _FINT:
        case _INIT:
          this._DecodeFormat(payload);
          break;
        case _KFRM:
          this._DecodeKeyframe(payload);
          break;
        case _DLTA:
          this._DecodeDelta(payload);
          break;
        case _MOVE:
          this._DecodeMove(payload);
          break;
        case _MOUS:
          this._DecodeCursor(payload);
          break;
        case _MPOS:
          this._DecodeCursorPosition(payload);
          break;
        default:
          // FFmpeg deliberately ignores unknown tagged records so newer recorder versions remain
          // forward compatible. The size field still makes them safely skippable.
          break;
      }
    }

    this._RequireCanvas();
    var display = (byte[])this._frame2!.Clone();
    this._DrawCursor(display);
    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Rgb24,
      PixelData = this._ToRgb24(display),
    };
    return true;
  }

  private int _DecodeFormat(ReadOnlySpan<byte> payload) {
    if (payload.Length < 4 || BinaryPrimitives.ReadUInt32LittleEndian(payload) != 0x65) {
      this._RequireCanvas();
      Array.Clear(this._frame1!);
      Array.Clear(this._frame2!);
      return 0;
    }

    if (payload.Length < 72)
      throw new InvalidDataException("A RASC FINT/INIT record is shorter than its 72-byte format header.");

    var width = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(payload[8..]));
    var height = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(payload[12..]));
    var bits = BinaryPrimitives.ReadUInt16LittleEndian(payload[46..]);
    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"A RASC format record states an invalid {width}x{height} canvas.");

    var format = bits switch {
      8 => NativeFormat.Indexed8,
      16 => NativeFormat.Rgb555,
      32 => NativeFormat.Bgr0,
      _ => throw new NotSupportedException($"RASC pixel depth {bits} is not defined by the LGPL reference decoder."),
    };
    var bytesPerPixel = bits >> 3;
    var stride = bits == 8 ? checked((width + 3) & ~3) : checked(width * bytesPerPixel);
    var frameBytes = checked(stride * height);

    this._width = width;
    this._height = height;
    this._format = format;
    this._bytesPerPixel = bytesPerPixel;
    this._stride = stride;
    this._frame1 = new byte[frameBytes];
    this._frame2 = new byte[frameBytes];
    this._palette = format == NativeFormat.Indexed8 ? new byte[256 * 3] : null;

    var consumed = 72;
    if (format == NativeFormat.Indexed8) {
      if (payload.Length < consumed + 256 * 4)
        throw new InvalidDataException("An 8-bit RASC format record ends inside its 256-entry palette.");
      for (var i = 0; i < 256; ++i) {
        var value = BinaryPrimitives.ReadUInt32LittleEndian(payload[(consumed + i * 4)..]);
        this._palette![i * 3] = (byte)value;
        this._palette[i * 3 + 1] = (byte)(value >> 8);
        this._palette[i * 3 + 2] = (byte)(value >> 16);
      }
      consumed += 256 * 4;
    }

    return consumed;
  }

  private void _DecodeKeyframe(ReadOnlySpan<byte> payload) {
    var at = 0;
    if (payload.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(payload) == 0x65)
      at = this._DecodeFormat(payload);
    this._RequireCanvas();
    if (at >= payload.Length)
      throw new InvalidDataException("A RASC keyframe contains a format header but no compressed surfaces.");

    var surfaceBytes = checked(this._stride * this._height);
    var inflated = _Inflate(payload[at..], checked(surfaceBytes * 2));
    var sourceAt = 0;
    sourceAt = this._CopyBottomUp(inflated, sourceAt, this._frame2!);
    this._CopyBottomUp(inflated, sourceAt, this._frame1!);
  }

  private void _DecodeDelta(ReadOnlySpan<byte> payload) {
    this._RequireCanvas();
    if (payload.Length < 40)
      throw new InvalidDataException("A RASC DLTA record is shorter than its 40-byte header.");

    var uncompressedSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(payload[12..]));
    var x = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(payload[16..]));
    var y = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(payload[20..]));
    var width = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(payload[24..]));
    var height = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(payload[28..]));
    var compression = BinaryPrimitives.ReadUInt32LittleEndian(payload[36..]);
    if (x < 0 || y < 0 || width <= 0 || height <= 0 || x > this._width - width || y > this._height - height)
      throw new InvalidDataException($"A RASC delta rectangle ({x},{y}) {width}x{height} lies outside the canvas.");
    if ((long)width * height * this._bytesPerPixel * 3 < uncompressedSize)
      throw new InvalidDataException("A RASC delta declares an implausibly large uncompressed command stream.");

    var encoded = payload[40..];
    var commands = compression switch {
      0 => encoded.Length >= uncompressedSize
        ? encoded[..uncompressedSize].ToArray()
        : throw new InvalidDataException("An uncompressed RASC delta is shorter than its declared command stream."),
      1 => _Inflate(encoded, uncompressedSize),
      2 => throw new NotSupportedException("RASC delta compression type 2 is not implemented by FFmpeg's LGPL decoder either."),
      _ => throw new InvalidDataException($"RASC delta compression type {compression} is invalid."),
    };

    var reader = new LittleEndianReader(commands);
    var cx = 0;
    var row = y + height - 1;
    var rowsRemaining = height;
    var rowBytes = checked(width * this._bytesPerPixel);
    while (reader.Remaining > 0) {
      if (reader.Remaining < 2)
        throw new InvalidDataException("A RASC delta ends inside a run header.");
      var type = reader.ReadByte();
      var length = reader.ReadByte();

      switch (type) {
        case 1:
          while (length > 0 && rowsRemaining > 0) {
            ++cx;
            _NextDeltaByte(ref cx, ref row, ref rowsRemaining, ref length, rowBytes);
          }
          break;

        case 2:
          while (length > 0 && rowsRemaining > 0) {
            var at = this._NativeOffset(x, row, cx, rowBytes, 1);
            (this._frame1![at], this._frame2![at]) = (this._frame2[at], this._frame1[at]);
            ++cx;
            _NextDeltaByte(ref cx, ref row, ref rowsRemaining, ref length, rowBytes);
          }
          break;

        case 3:
          while (length > 0 && rowsRemaining > 0) {
            var fill = reader.ReadByte();
            var at = this._NativeOffset(x, row, cx, rowBytes, 1);
            this._frame1![at] = this._frame2![at];
            this._frame2[at] = fill;
            ++cx;
            _NextDeltaByte(ref cx, ref row, ref rowsRemaining, ref length, rowBytes);
          }
          break;

        case 4: {
          var fill = reader.ReadByte();
          while (length > 0 && rowsRemaining > 0) {
            var at = this._NativeOffset(x, row, cx, rowBytes, 4);
            this._frame2!.AsSpan(at, 4).CopyTo(this._frame1!.AsSpan(at, 4));
            BinaryPrimitives.WriteUInt32LittleEndian(this._frame2.AsSpan(at, 4), fill);
            ++cx;
            _NextDeltaByte(ref cx, ref row, ref rowsRemaining, ref length, rowBytes);
          }
          break;
        }

        case 7: {
          var fill = reader.ReadUInt32();
          while (length > 0 && rowsRemaining > 0) {
            var at = this._NativeOffset(x, row, cx, rowBytes, 4);
            this._frame2!.AsSpan(at, 4).CopyTo(this._frame1!.AsSpan(at, 4));
            BinaryPrimitives.WriteUInt32LittleEndian(this._frame2.AsSpan(at, 4), fill);
            cx += 4;
            _NextDeltaByte(ref cx, ref row, ref rowsRemaining, ref length, rowBytes);
          }
          break;
        }

        case 10:
          while (length > 0 && rowsRemaining > 0) {
            if (cx > rowBytes - 4)
              throw new InvalidDataException("A RASC four-byte skip crosses the delta rectangle edge.");
            cx += 4;
            _NextDeltaByte(ref cx, ref row, ref rowsRemaining, ref length, rowBytes);
          }
          break;

        case 12:
          while (length > 0 && rowsRemaining > 0) {
            var at = this._NativeOffset(x, row, cx, rowBytes, 4);
            var a = BinaryPrimitives.ReadUInt32LittleEndian(this._frame1!.AsSpan(at, 4));
            var b = BinaryPrimitives.ReadUInt32LittleEndian(this._frame2!.AsSpan(at, 4));
            BinaryPrimitives.WriteUInt32LittleEndian(this._frame1.AsSpan(at, 4), b);
            BinaryPrimitives.WriteUInt32LittleEndian(this._frame2.AsSpan(at, 4), a);
            cx += 4;
            _NextDeltaByte(ref cx, ref row, ref rowsRemaining, ref length, rowBytes);
          }
          break;

        case 13:
          while (length > 0 && rowsRemaining > 0) {
            var fill = reader.ReadUInt32();
            var at = this._NativeOffset(x, row, cx, rowBytes, 4);
            this._frame2!.AsSpan(at, 4).CopyTo(this._frame1!.AsSpan(at, 4));
            BinaryPrimitives.WriteUInt32LittleEndian(this._frame2.AsSpan(at, 4), fill);
            cx += 4;
            _NextDeltaByte(ref cx, ref row, ref rowsRemaining, ref length, rowBytes);
          }
          break;

        default:
          throw new NotSupportedException($"RASC delta run type {type} is not defined by the LGPL reference decoder.");
      }
    }
  }

  private void _DecodeMove(ReadOnlySpan<byte> payload) {
    this._RequireCanvas();
    if (payload.Length < 24)
      throw new InvalidDataException("A RASC MOVE record is shorter than its 24-byte header.");

    var moveCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(payload[8..]));
    if (moveCount < 0 || moveCount > this._width * (long)this._height || moveCount > int.MaxValue / 16)
      throw new InvalidDataException($"A RASC MOVE record declares an invalid {moveCount} rectangle operation(s).");
    var compression = BinaryPrimitives.ReadUInt32LittleEndian(payload[20..]);
    var expected = checked(moveCount * 16);
    var encoded = payload[24..];
    var records = compression switch {
      0 => encoded.Length >= expected
        ? encoded[..expected].ToArray()
        : throw new InvalidDataException("An uncompressed RASC MOVE record is truncated."),
      1 => _Inflate(encoded, expected),
      2 => throw new NotSupportedException("RASC MOVE compression type 2 is not implemented by FFmpeg's LGPL decoder either."),
      _ => throw new InvalidDataException($"RASC MOVE compression type {compression} is invalid."),
    };

    var reader = new LittleEndianReader(records);
    for (var i = 0; i < moveCount; ++i) {
      var type = reader.ReadUInt16();
      var startX = reader.ReadUInt16();
      var startY = reader.ReadUInt16();
      var endX = reader.ReadUInt16();
      var endY = reader.ReadUInt16();
      var moveX = reader.ReadUInt16();
      var moveY = reader.ReadUInt16();
      reader.Skip(2);

      if (startX >= this._width || startY >= this._height || endX >= this._width || endY >= this._height ||
          moveX >= this._width || moveY >= this._height || startX >= endX || startY >= endY)
        continue;
      var width = endX - startX;
      var height = endY - startY;
      if (moveX + width > this._width || moveY + height > this._height)
        continue;

      var byteWidth = checked(width * this._bytesPerPixel);
      switch (type) {
        case 2:
          for (var row = 0; row < height; ++row) {
            var at = (startY + row) * this._stride + startX * this._bytesPerPixel;
            this._frame2!.AsSpan(at, byteWidth).CopyTo(this._frame1!.AsSpan(at, byteWidth));
          }
          break;

        case 1:
          for (var row = 0; row < height; ++row) {
            var at = (startY + row) * this._stride + startX * this._bytesPerPixel;
            this._frame2!.AsSpan(at, byteWidth).Clear();
          }
          break;

        case 0: {
          var scratch = new byte[checked(byteWidth * height)];
          for (var row = 0; row < height; ++row) {
            var sourceAt = (moveY + row) * this._stride + moveX * this._bytesPerPixel;
            this._frame2!.AsSpan(sourceAt, byteWidth).CopyTo(scratch.AsSpan(row * byteWidth, byteWidth));
          }
          for (var row = 0; row < height; ++row) {
            var destinationAt = (startY + row) * this._stride + startX * this._bytesPerPixel;
            scratch.AsSpan(row * byteWidth, byteWidth).CopyTo(this._frame2!.AsSpan(destinationAt, byteWidth));
          }
          break;
        }

        default:
          throw new InvalidDataException($"RASC MOVE operation type {type} is invalid.");
      }
    }
  }

  private void _DecodeCursor(ReadOnlySpan<byte> payload) {
    this._RequireCanvas();
    if (payload.Length < 32)
      throw new InvalidDataException("A RASC MOUS record is shorter than its 32-byte header.");
    var width = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(payload[8..]));
    var height = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(payload[12..]));
    var uncompressedSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(payload[28..]));
    if (width < 0 || height < 0 || width > this._width || height > this._height ||
        uncompressedSize != checked(3 * width * height))
      throw new InvalidDataException("A RASC cursor record states invalid dimensions or RGB byte count.");

    this._cursor = _Inflate(payload[32..], uncompressedSize);
    this._cursorWidth = width;
    this._cursorHeight = height;
  }

  private void _DecodeCursorPosition(ReadOnlySpan<byte> payload) {
    if (payload.Length < 16)
      throw new InvalidDataException("A RASC MPOS record is shorter than its 16-byte payload.");
    this._cursorX = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(payload[8..]));
    this._cursorY = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(payload[12..]));
  }

  private void _DrawCursor(Span<byte> display) {
    if (this._cursor == null || this._cursorWidth == 0 || this._cursorHeight == 0)
      return;
    if (this._cursorX < 0 || this._cursorY < 0 || this._cursorX > this._width - this._cursorWidth ||
        this._cursorY > this._height - this._cursorHeight)
      return;

    var keyR = this._cursor[0];
    var keyG = this._cursor[1];
    var keyB = this._cursor[2];
    for (var row = 0; row < this._cursorHeight; ++row)
      for (var column = 0; column < this._cursorWidth; ++column) {
        var cursorAt = ((this._cursorHeight - row - 1) * this._cursorWidth + column) * 3;
        var red = this._cursor[cursorAt];
        var green = this._cursor[cursorAt + 1];
        var blue = this._cursor[cursorAt + 2];
        if (red == keyR && green == keyG && blue == keyB)
          continue;

        var pixelAt = (this._cursorY + row) * this._stride + (this._cursorX + column) * this._bytesPerPixel;
        switch (this._format) {
          case NativeFormat.Indexed8:
            display[pixelAt] = this._NearestPaletteIndex(red, green, blue);
            break;
          case NativeFormat.Rgb555: {
            var value = (ushort)((red >> 3) | ((green >> 3) << 5) | ((blue >> 3) << 10));
            BinaryPrimitives.WriteUInt16LittleEndian(display[pixelAt..], value);
            break;
          }
          case NativeFormat.Bgr0:
            display[pixelAt] = blue;
            display[pixelAt + 1] = green;
            display[pixelAt + 2] = red;
            break;
        }
      }
  }

  private byte _NearestPaletteIndex(int red, int green, int blue) {
    var bestDistance = int.MaxValue;
    var bestIndex = 0;
    for (var i = 0; i < 256; ++i) {
      var at = i * 3;
      var distance = Math.Abs(red - this._palette![at]) + Math.Abs(green - this._palette[at + 1]) +
                     Math.Abs(blue - this._palette[at + 2]);
      if (distance >= bestDistance)
        continue;
      bestDistance = distance;
      bestIndex = i;
    }
    return checked((byte)bestIndex);
  }

  private byte[] _ToRgb24(ReadOnlySpan<byte> native) {
    var output = new byte[checked(this._width * this._height * 3)];
    var outAt = 0;
    for (var row = 0; row < this._height; ++row)
      for (var column = 0; column < this._width; ++column) {
        var at = row * this._stride + column * this._bytesPerPixel;
        switch (this._format) {
          case NativeFormat.Indexed8: {
            var paletteAt = native[at] * 3;
            output[outAt++] = this._palette![paletteAt];
            output[outAt++] = this._palette[paletteAt + 1];
            output[outAt++] = this._palette[paletteAt + 2];
            break;
          }
          case NativeFormat.Rgb555: {
            var value = BinaryPrimitives.ReadUInt16LittleEndian(native[at..]);
            var red = value & 31;
            var green = (value >> 5) & 31;
            var blue = (value >> 10) & 31;
            output[outAt++] = (byte)((red << 3) | (red >> 2));
            output[outAt++] = (byte)((green << 3) | (green >> 2));
            output[outAt++] = (byte)((blue << 3) | (blue >> 2));
            break;
          }
          case NativeFormat.Bgr0:
            output[outAt++] = native[at + 2];
            output[outAt++] = native[at + 1];
            output[outAt++] = native[at];
            break;
          default:
            throw new InvalidDataException("RASC canvas format changed after initialization.");
        }
      }
    return output;
  }

  private int _CopyBottomUp(ReadOnlySpan<byte> source, int sourceAt, Span<byte> target) {
    for (var codedRow = this._height - 1; codedRow >= 0; --codedRow) {
      var available = Math.Min(this._stride, source.Length - sourceAt);
      if (available <= 0)
        break;
      source.Slice(sourceAt, available).CopyTo(target.Slice(codedRow * this._stride, available));
      sourceAt += available;
    }
    return sourceAt;
  }

  private int _NativeOffset(int x, int row, int cx, int rowBytes, int need) {
    if (row < 0 || row >= this._height || cx < 0 || cx > rowBytes - need)
      throw new InvalidDataException("A RASC delta run crosses its declared rectangle boundary.");
    return checked(row * this._stride + x * this._bytesPerPixel + cx);
  }

  private static void _NextDeltaByte(ref int cx, ref int row, ref int rowsRemaining, ref byte length, int rowBytes) {
    if (cx >= rowBytes) {
      cx = 0;
      --row;
      --rowsRemaining;
    }
    --length;
  }

  private void _RequireCanvas() {
    if (this._frame1 == null || this._frame2 == null || this._width <= 0 || this._height <= 0)
      throw new InvalidDataException(
        $"RASC stream {this._streamIndex} emitted picture data before a valid FINT/INIT format record.");
  }

  private static byte[] _Inflate(ReadOnlySpan<byte> compressed, int expectedBytes) {
    if (expectedBytes < 0)
      throw new InvalidDataException("A RASC record declares a negative decompressed size.");
    var output = new byte[expectedBytes];
    if (expectedBytes == 0)
      return output;

    using var input = new MemoryStream(compressed.ToArray(), writable: false);
    using var zlib = new ZLibStream(input, CompressionMode.Decompress);
    var at = 0;
    while (at < output.Length) {
      var read = zlib.Read(output, at, output.Length - at);
      if (read == 0)
        break;
      at += read;
    }
    return output;
  }

  private enum NativeFormat : byte {
    Indexed8,
    Rgb555,
    Bgr0,
  }

  private ref struct LittleEndianReader {
    private readonly ReadOnlySpan<byte> _source;
    private int _position;

    internal LittleEndianReader(ReadOnlySpan<byte> source) {
      this._source = source;
      this._position = 0;
    }

    internal int Remaining => this._source.Length - this._position;

    internal byte ReadByte() {
      if (this.Remaining < 1)
        throw new InvalidDataException("A RASC record ends while reading one byte.");
      return this._source[this._position++];
    }

    internal ushort ReadUInt16() {
      if (this.Remaining < 2)
        throw new InvalidDataException("A RASC record ends while reading a 16-bit value.");
      var result = BinaryPrimitives.ReadUInt16LittleEndian(this._source[this._position..]);
      this._position += 2;
      return result;
    }

    internal uint ReadUInt32() {
      if (this.Remaining < 4)
        throw new InvalidDataException("A RASC record ends while reading a 32-bit value.");
      var result = BinaryPrimitives.ReadUInt32LittleEndian(this._source[this._position..]);
      this._position += 4;
      return result;
    }

    internal void Skip(int count) {
      if (count < 0 || this.Remaining < count)
        throw new InvalidDataException("A RASC record ends while skipping reserved bytes.");
      this._position += count;
    }

    internal ReadOnlySpan<byte> ReadSpan(int count) {
      if (count < 0 || this.Remaining < count)
        throw new InvalidDataException("A RASC record ends inside its declared payload.");
      var result = this._source.Slice(this._position, count);
      this._position += count;
      return result;
    }
  }
}
