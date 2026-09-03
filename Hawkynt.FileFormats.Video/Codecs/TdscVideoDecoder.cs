using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.Codecs;

/// <summary>Decodes TDSC (<c>TSDC</c>) screen video.</summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/tdsc.c</c>, copyright (C) 2015 Vittorio Giovara,
/// distributed there under LGPL-2.1-or-later. This adaptation is distributed with PNGCrushCS under
/// LGPL-3.0-or-later.
/// <para/>
/// TDSC stores a persistent BGR24 canvas as raw or JPEG-compressed rectangular tiles inside a zlib
/// packet. Cursor updates are separate tagged records and are composited only onto the returned
/// picture, never into the reference canvas. JPEG tiles reuse this repository's JPEG decoder.
/// </remarks>
public sealed class TdscVideoDecoder : IVideoCodecDecoder<TdscVideoDecoder> {

  private const uint _Tdsf = 0x46534454; // TDSF
  private const uint _Dtsm = 0x4D535444; // DTSM
  private const uint _Tdsb = 0x42534454; // TDSB
  private const uint _Jpeg = 0x4A504547; // GEPJ
  private const uint _Raw = 0x57415220;  // " RAW"

  private const uint _CursorMono = 0x01010004;
  private const uint _CursorBgra = 0x20010004;
  private const uint _CursorRgba = 0x20010008;

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("TSDC");

  private readonly int _streamIndex;
  private int _width;
  private int _height;
  private byte[] _canvas;
  private byte[]? _cursor;
  private int _cursorStride;
  private int _cursorWidth;
  private int _cursorHeight;
  private int _cursorX;
  private int _cursorY;
  private int _cursorHotX;
  private int _cursorHotY;

  private TdscVideoDecoder(int width, int height, int streamIndex) {
    this._width = width;
    this._height = height;
    this._streamIndex = streamIndex;
    this._canvas = new byte[checked(width * height * 3)];
  }

  public static string CodecName => "TDSC";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static TdscVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    _ValidateDimensions(stream.Width, stream.Height, stream.Index);
    return new(stream.Width, stream.Height, stream.Index);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var inflated = this._Inflate(packet.Data.Span);
    var reader = new LittleEndianReader(inflated, this._streamIndex);
    var tag = reader.ReadUInt32("packet tag");

    if (tag == _Tdsf) {
      var tiles = reader.ReadUInt32("tile count");
      if (tiles > int.MaxValue)
        throw new InvalidDataException($"TDSC stream {this._streamIndex} declares too many tiles.");
      reader.Skip(4, "TDSF timestamp/version");
      _ = reader.ReadUInt32("TDSF frame marker");
      this._DecodeBitmapHeader(ref reader);
      for (var i = 0; i < (int)tiles; ++i)
        this._DecodeTile(ref reader);

      if (reader.Remaining >= 8 && reader.PeekUInt32() == _Dtsm) {
        _ = reader.ReadUInt32("DTSM tag");
        this._DecodeCursorRecord(ref reader);
      }
    } else if (tag == _Dtsm) {
      this._DecodeCursorRecord(ref reader);
    } else {
      throw new InvalidDataException($"TDSC stream {this._streamIndex} starts a packet with unknown tag 0x{tag:X8}.");
    }

    var display = (byte[])this._canvas.Clone();
    this._PaintCursor(display);
    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Bgr24,
      PixelData = display,
    };
    return true;
  }

  private void _DecodeBitmapHeader(ref LittleEndianReader reader) {
    if (reader.ReadUInt32("BITMAPINFOHEADER size") != 40)
      throw new InvalidDataException("A TDSC TDSF block does not carry a 40-byte BITMAPINFOHEADER.");

    var width = reader.ReadInt32("picture width");
    var storedHeight = reader.ReadInt32("picture height");
    if (storedHeight == int.MinValue)
      throw new InvalidDataException("A TDSC BITMAPINFOHEADER carries an invalid height.");
    var height = -storedHeight;
    if (reader.ReadUInt16("planes") != 1 || reader.ReadUInt16("bits per pixel") != 24)
      throw new NotSupportedException("TDSC defines a top-down, one-plane BGR24 canvas; this packet states another bitmap layout.");
    reader.Skip(24, "unused BITMAPINFOHEADER fields");

    _ValidateDimensions(width, height, this._streamIndex);
    if (width == this._width && height == this._height)
      return;

    this._width = width;
    this._height = height;
    this._canvas = new byte[checked(width * height * 3)];
  }

  private void _DecodeTile(ref LittleEndianReader reader) {
    if (reader.ReadUInt32("tile tag") != _Tdsb)
      throw new InvalidDataException("A TDSC tile does not start with TDSB.");
    var tileSize = reader.ReadUInt32("tile size");
    if (tileSize > int.MaxValue)
      throw new InvalidDataException("A TDSC tile is too large to hold in memory.");
    var mode = reader.ReadUInt32("tile mode");
    reader.Skip(4, "tile reserved field");
    var x = reader.ReadInt32("tile x");
    var y = reader.ReadInt32("tile y");
    var x2 = reader.ReadInt32("tile x2");
    var y2 = reader.ReadInt32("tile y2");
    if (x < 0 || y < 0 || x2 <= x || y2 <= y || x2 > this._width || y2 > this._height)
      throw new InvalidDataException(
        $"TDSC stream {this._streamIndex} carries tile ({x},{y})..({x2},{y2}) outside its {this._width}x{this._height} canvas.");

    var encoded = reader.ReadSpan((int)tileSize, "tile payload");
    var width = x2 - x;
    var height = y2 - y;
    switch (mode) {
      case _Raw:
        var required = checked(width * height * 3);
        if (encoded.Length < required)
          throw new InvalidDataException($"A TDSC raw tile needs {required} bytes but carries {encoded.Length}.");
        for (var row = 0; row < height; ++row)
          encoded.Slice(row * width * 3, width * 3).CopyTo(this._canvas.AsSpan(((y + row) * this._width + x) * 3, width * 3));
        break;

      case _Jpeg:
        this._DecodeJpegTile(encoded, x, y, width, height);
        break;

      default:
        throw new NotSupportedException($"TDSC stream {this._streamIndex} uses unknown tile mode 0x{mode:X8}.");
    }
  }

  private void _DecodeJpegTile(ReadOnlySpan<byte> encoded, int x, int y, int width, int height) {
    RawImage decoded;
    try {
      decoded = JpegFile.ToRawImage(JpegReader.FromSpan(encoded));
    } catch (Exception ex) when (ex is InvalidDataException or ArgumentException) {
      throw new InvalidDataException("A TDSC JPEG tile contains invalid JPEG data.", ex);
    }

    if (decoded.Width < width || decoded.Height < height)
      throw new InvalidDataException(
        $"A TDSC JPEG tile decodes to {decoded.Width}x{decoded.Height}, smaller than its declared {width}x{height} rectangle.");
    var bgr = PixelConverter.Convert(decoded, PixelFormat.Bgr24);
    for (var row = 0; row < height; ++row)
      bgr.PixelData.AsSpan(row * bgr.Width * 3, width * 3).CopyTo(this._canvas.AsSpan(((y + row) * this._width + x) * 3, width * 3));
  }

  private void _DecodeCursorRecord(ref LittleEndianReader reader) {
    var size = reader.ReadUInt32("DTSM size");
    if (size > int.MaxValue || size > reader.Remaining)
      throw new InvalidDataException($"A TDSC DTSM record declares {size} payload bytes but only {reader.Remaining} remain.");
    var payload = reader.ReadSpan((int)size, "DTSM payload");
    var cursor = new LittleEndianReader(payload, this._streamIndex);
    var action = cursor.ReadUInt32("cursor action");
    cursor.Skip(4, "cursor version/id");

    switch (action) {
      case 2:
      case 3:
        this._cursorX = cursor.ReadInt32("cursor x");
        this._cursorY = cursor.ReadInt32("cursor y");
        if (action == 3)
          this._LoadCursor(ref cursor);
        break;
      default:
        throw new NotSupportedException($"TDSC stream {this._streamIndex} uses cursor action {action}, which is not defined by the LGPL reference decoder.");
    }
  }

  private void _LoadCursor(ref LittleEndianReader reader) {
    this._cursorHotX = reader.ReadUInt16("cursor hotspot x");
    this._cursorHotY = reader.ReadUInt16("cursor hotspot y");
    this._cursorWidth = reader.ReadUInt16("cursor width");
    this._cursorHeight = reader.ReadUInt16("cursor height");
    var format = reader.ReadUInt32("cursor format");
    if (this._cursorWidth is < 1 or > 256 || this._cursorHeight is < 1 or > 256)
      throw new InvalidDataException($"TDSC stream {this._streamIndex} carries invalid cursor size {this._cursorWidth}x{this._cursorHeight}.");
    if (this._cursorHotX >= this._cursorWidth || this._cursorHotY >= this._cursorHeight)
      throw new InvalidDataException($"TDSC stream {this._streamIndex} carries a cursor hotspot outside its sprite.");

    var stridePixels = (this._cursorWidth + 31) & ~31;
    this._cursorStride = checked(stridePixels * 4);
    this._cursor = new byte[checked(this._cursorStride * this._cursorHeight)];

    switch (format) {
      case _CursorMono:
        this._LoadMonochromeCursor(ref reader, stridePixels);
        break;
      case _CursorBgra:
      case _CursorRgba:
        reader.Skip(checked(this._cursorHeight * (stridePixels >> 3)), "cursor monochrome fallback");
        this._LoadColorCursor(ref reader, format == _CursorRgba);
        break;
      default:
        throw new NotSupportedException($"TDSC stream {this._streamIndex} uses cursor format 0x{format:X8}, which the LGPL reference decoder does not define.");
    }
  }

  private void _LoadMonochromeCursor(ref LittleEndianReader reader, int stridePixels) {
    var bits = new byte[checked(stridePixels * this._cursorHeight)];
    for (var row = 0; row < this._cursorHeight; ++row)
      for (var x = 0; x < stridePixels; x += 32) {
        var word = reader.ReadUInt32BigEndian("monochrome cursor bits");
        for (var bit = 0; bit < 32; ++bit)
          bits[row * stridePixels + x + bit] = (byte)((word >> (31 - bit)) & 1);
      }

    for (var row = 0; row < this._cursorHeight; ++row)
      for (var x = 0; x < stridePixels; x += 32) {
        var word = reader.ReadUInt32BigEndian("monochrome cursor mask");
        for (var bit = 0; bit < 32; ++bit) {
          var mask = (int)((word >> (31 - bit)) & 1);
          var at = row * this._cursorStride + (x + bit) * 4;
          switch (bits[row * stridePixels + x + bit] * 2 + mask) {
            case 0:
              this._cursor![at] = 0xFF;
              break;
            case 1:
              this._cursor![at] = 0xFF;
              this._cursor[at + 1] = 0xFF;
              this._cursor[at + 2] = 0xFF;
              this._cursor[at + 3] = 0xFF;
              break;
          }
        }
      }
  }

  private void _LoadColorCursor(ref LittleEndianReader reader, bool rgbaVariant) {
    for (var row = 0; row < this._cursorHeight; ++row)
      for (var x = 0; x < this._cursorWidth; ++x) {
        var source = reader.ReadSpan(4, "colour cursor pixel");
        var at = row * this._cursorStride + x * 4;
        if (rgbaVariant) {
          // This deliberately follows the reference decoder's byte mapping for CUR_FMT_RGBA.
          source.CopyTo(this._cursor!.AsSpan(at, 4));
        } else {
          this._cursor![at] = source[3];
          this._cursor[at + 1] = source[0];
          this._cursor[at + 2] = source[1];
          this._cursor[at + 3] = source[2];
        }
      }
  }

  private void _PaintCursor(Span<byte> destination) {
    if (this._cursor == null)
      return;
    if ((uint)this._cursorX >= (uint)this._width || (uint)this._cursorY >= (uint)this._height)
      return;

    var originX = this._cursorX - this._cursorHotX;
    var originY = this._cursorY - this._cursorHotY;
    for (var row = 0; row < this._cursorHeight; ++row) {
      var y = originY + row;
      if ((uint)y >= (uint)this._height)
        continue;
      for (var column = 0; column < this._cursorWidth; ++column) {
        var x = originX + column;
        if ((uint)x >= (uint)this._width)
          continue;
        var cursorAt = row * this._cursorStride + column * 4;
        var alpha = this._cursor[cursorAt];
        var destinationAt = (y * this._width + x) * 3;
        destination[destinationAt] = _Blend(destination[destinationAt], this._cursor[cursorAt + 1], alpha);
        destination[destinationAt + 1] = _Blend(destination[destinationAt + 1], this._cursor[cursorAt + 2], alpha);
        destination[destinationAt + 2] = _Blend(destination[destinationAt + 2], this._cursor[cursorAt + 3], alpha);
      }
    }
  }

  private byte[] _Inflate(ReadOnlySpan<byte> compressed) {
    try {
      using var input = new MemoryStream(compressed.ToArray(), writable: false);
      using var zlib = new ZLibStream(input, CompressionMode.Decompress);
      using var output = new MemoryStream();
      var buffer = new byte[8192];
      var maximum = Math.Max(65_536L, (long)this._width * this._height * 64 + 65_536);
      var dimensionsChecked = false;

      while (true) {
        var read = zlib.Read(buffer, 0, buffer.Length);
        if (read == 0)
          break;
        output.Write(buffer, 0, read);

        if (!dimensionsChecked && output.Length >= 28) {
          var prefix = output.GetBuffer().AsSpan(0, checked((int)output.Length));
          if (BinaryPrimitives.ReadUInt32LittleEndian(prefix) == _Tdsf
              && BinaryPrimitives.ReadUInt32LittleEndian(prefix[16..]) == 40) {
            var width = BinaryPrimitives.ReadInt32LittleEndian(prefix[20..]);
            var storedHeight = BinaryPrimitives.ReadInt32LittleEndian(prefix[24..]);
            if (storedHeight == int.MinValue)
              throw new InvalidDataException("A TDSC BITMAPINFOHEADER carries an invalid height.");
            var height = -storedHeight;
            _ValidateDimensions(width, height, this._streamIndex);
            maximum = Math.Max(65_536L, (long)width * height * 64 + 65_536);
          }
          dimensionsChecked = true;
        }

        if (output.Length > maximum || output.Length > int.MaxValue)
          throw new InvalidDataException($"TDSC stream {this._streamIndex} inflates beyond its frame-derived safety bound.");
      }
      return output.ToArray();
    } catch (InvalidDataException) {
      throw;
    } catch (Exception ex) when (ex is IOException or NotSupportedException) {
      throw new InvalidDataException($"TDSC stream {this._streamIndex} carries invalid zlib data.", ex);
    }
  }

  private static void _ValidateDimensions(int width, int height, int streamIndex) {
    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"TDSC stream {streamIndex} states invalid dimensions {width}x{height}.");
    if ((long)width * height > 100_000_000)
      throw new InvalidDataException($"TDSC stream {streamIndex} states an implausibly large {width}x{height} canvas.");
    _ = checked(width * height * 3);
  }

  private static byte _Blend(byte original, byte replacement, byte alpha)
    => (byte)((original * (256 - alpha) + replacement * alpha) >> 8);

  private ref struct LittleEndianReader {
    private ReadOnlySpan<byte> _data;
    private readonly int _streamIndex;

    public LittleEndianReader(ReadOnlySpan<byte> data, int streamIndex) {
      this._data = data;
      this._streamIndex = streamIndex;
    }

    public int Remaining => this._data.Length;
    public uint PeekUInt32() {
      this._Require(4, "tag");
      return BinaryPrimitives.ReadUInt32LittleEndian(this._data);
    }

    public ushort ReadUInt16(string field) {
      this._Require(2, field);
      var result = BinaryPrimitives.ReadUInt16LittleEndian(this._data);
      this._data = this._data[2..];
      return result;
    }

    public uint ReadUInt32(string field) {
      this._Require(4, field);
      var result = BinaryPrimitives.ReadUInt32LittleEndian(this._data);
      this._data = this._data[4..];
      return result;
    }

    public uint ReadUInt32BigEndian(string field) {
      this._Require(4, field);
      var result = BinaryPrimitives.ReadUInt32BigEndian(this._data);
      this._data = this._data[4..];
      return result;
    }

    public int ReadInt32(string field) => unchecked((int)this.ReadUInt32(field));

    public ReadOnlySpan<byte> ReadSpan(int count, string field) {
      this._Require(count, field);
      var result = this._data[..count];
      this._data = this._data[count..];
      return result;
    }

    public void Skip(int count, string field) => _ = this.ReadSpan(count, field);

    private void _Require(int count, string field) {
      if (count < 0 || this._data.Length < count)
        throw new InvalidDataException(
          $"TDSC stream {this._streamIndex} ends inside {field}; {count} byte(s) are required and {this._data.Length} remain.");
    }
  }
}
