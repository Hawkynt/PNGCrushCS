using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Decodes VMware Screen Codec / VMware Video (<c>VMnc</c>).</summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/vmnc.c</c>, copyright (c) 2006 Konstantin Shishkov,
/// distributed there under LGPL-2.1-or-later. This adaptation is distributed with PNGCrushCS under
/// LGPL-3.0-or-later.
/// <para/>
/// VMnc is an RFB/VNC-shaped screen stream. Packets carry rectangular raw or Hextile updates and
/// optional cursor records. The RFB server-initialisation record carries the actual channel maxima
/// and bit shifts; those are retained here instead of assuming an indexed palette for 8-bit streams.
/// </remarks>
public sealed class VmncVideoDecoder : IVideoCodecDecoder<VmncVideoDecoder> {

  private const uint _CursorDefinition = 0x574D5664; // WMVd
  private const uint _UnknownE = 0x574D5665;         // WMVe
  private const uint _CursorPosition = 0x574D5666;   // WMVf
  private const uint _UnknownG = 0x574D5667;         // WMVg
  private const uint _UnknownH = 0x574D5668;         // WMVh
  private const uint _ServerInitialization = 0x574D5669; // WMVi
  private const uint _UnknownJ = 0x574D566A;         // WMVj
  private const uint _Raw = 0;
  private const uint _Hextile = 5;

  private const byte _HextileRaw = 1;
  private const byte _HextileBackground = 2;
  private const byte _HextileForeground = 4;
  private const byte _HextileSubrectangles = 8;
  private const byte _HextileColoredSubrectangles = 16;

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("VMnc");

  private readonly int _width;
  private readonly int _height;
  private readonly int _streamIndex;
  private readonly int _bytesPerPixel;
  private readonly uint[] _canvas;

  private bool _bigEndian;
  private PixelDescriptor _pixelDescriptor;
  private uint[]? _cursorBits;
  private uint[]? _cursorMask;
  private int _cursorWidth;
  private int _cursorHeight;
  private int _cursorHotX;
  private int _cursorHotY;
  private int _cursorX;
  private int _cursorY;

  private VmncVideoDecoder(int width, int height, int bitsPerPixel, int streamIndex) {
    this._width = width;
    this._height = height;
    this._streamIndex = streamIndex;
    var storageBits = bitsPerPixel == 24 ? 32 : bitsPerPixel;
    this._bytesPerPixel = storageBits >> 3;
    this._canvas = new uint[checked(width * height)];
    this._pixelDescriptor = storageBits switch {
      16 => new(true, 31, 31, 31, 10, 5, 0),
      32 => new(true, 255, 255, 255, 16, 8, 0),
      _ => default,
    };
  }

  public static string CodecName => "VMware Screen Codec / VMware Video";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static VmncVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"VMnc stream {stream.Index} states a picture of {stream.Width}x{stream.Height}, which has no pixels.");
    if (stream.BitsPerPixel is not (8 or 16 or 24 or 32))
      throw new NotSupportedException(
        $"VMnc stream {stream.Index} states {stream.BitsPerPixel} bits per pixel; defined layouts use 8, 16 or 32 stored bits (some containers mislabel 32 as 24).");
    if ((long)stream.Width * stream.Height > int.MaxValue)
      throw new InvalidDataException($"VMnc stream {stream.Index}'s frame is too large to hold in memory.");

    return new(stream.Width, stream.Height, stream.BitsPerPixel, stream.Index);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var reader = new BigEndianReader(packet.Data.Span, this._streamIndex);
    reader.Skip(2, "packet prefix");
    var chunks = reader.ReadUInt16("chunk count");

    for (var chunk = 0; chunk < chunks; ++chunk) {
      var x = reader.ReadUInt16("rectangle x");
      var y = reader.ReadUInt16("rectangle y");
      var width = reader.ReadUInt16("rectangle width");
      var height = reader.ReadUInt16("rectangle height");
      var encoding = reader.ReadUInt32("rectangle encoding");
      this._RequireRectangle(x, y, width, height);

      switch (encoding) {
        case _Raw:
          this._DecodeRaw(ref reader, x, y, width, height);
          break;
        case _Hextile:
          this._DecodeHextile(ref reader, x, y, width, height);
          break;
        case _CursorDefinition:
          this._DecodeCursor(ref reader, width, height, x, y);
          break;
        case _CursorPosition:
          this._cursorX = x - this._cursorHotX;
          this._cursorY = y - this._cursorHotY;
          break;
        case _ServerInitialization:
          this._DecodeServerInitialization(ref reader);
          break;
        case _UnknownE:
          reader.Skip(2, "WMVe payload");
          break;
        case _UnknownG:
          reader.Skip(10, "WMVg payload");
          break;
        case _UnknownH:
          reader.Skip(4, "WMVh payload");
          break;
        case _UnknownJ:
          reader.Skip(2, "WMVj payload");
          break;
        default:
          throw new NotSupportedException(
            $"VMnc stream {this._streamIndex} uses rectangle encoding 0x{encoding:X8}, which is not defined by the LGPL reference decoder.");
      }
    }

    var display = (uint[])this._canvas.Clone();
    this._ApplyCursor(display);
    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Rgb24,
      PixelData = this._ToRgb24(display),
    };
    return true;
  }

  private void _DecodeRaw(ref BigEndianReader reader, int x, int y, int width, int height) {
    for (var row = 0; row < height; ++row)
      for (var column = 0; column < width; ++column)
        this._canvas[(y + row) * this._width + x + column] = this._ReadPixel(ref reader);
  }

  private void _DecodeHextile(ref BigEndianReader reader, int x, int y, int width, int height) {
    uint background = 0;
    uint foreground = 0;

    for (var tileY = 0; tileY < height; tileY += 16) {
      var tileHeight = Math.Min(16, height - tileY);
      for (var tileX = 0; tileX < width; tileX += 16) {
        var tileWidth = Math.Min(16, width - tileX);
        var flags = reader.ReadByte("Hextile flags");
        if ((flags & ~0x1F) != 0)
          throw new InvalidDataException($"VMnc stream {this._streamIndex} sets unknown Hextile flag bits 0x{flags:X2}.");

        if ((flags & _HextileRaw) != 0) {
          for (var row = 0; row < tileHeight; ++row)
            for (var column = 0; column < tileWidth; ++column)
              this._canvas[(y + tileY + row) * this._width + x + tileX + column] = this._ReadPixel(ref reader);
          continue;
        }

        if ((flags & _HextileBackground) != 0)
          background = this._ReadPixel(ref reader);
        if ((flags & _HextileForeground) != 0)
          foreground = this._ReadPixel(ref reader);
        this._FillRectangle(x + tileX, y + tileY, tileWidth, tileHeight, background);

        var subrectangles = (flags & _HextileSubrectangles) != 0 ? reader.ReadByte("Hextile subrectangle count") : 0;
        var colored = (flags & _HextileColoredSubrectangles) != 0;
        if (colored && (flags & _HextileSubrectangles) == 0)
          throw new InvalidDataException("A VMnc Hextile tile requests per-subrectangle colours without subrectangles.");

        for (var index = 0; index < subrectangles; ++index) {
          var color = colored ? this._ReadPixel(ref reader) : foreground;
          var xy = reader.ReadByte("Hextile subrectangle position");
          var wh = reader.ReadByte("Hextile subrectangle size");
          var rectX = xy >> 4;
          var rectY = xy & 0x0F;
          var rectWidth = (wh >> 4) + 1;
          var rectHeight = (wh & 0x0F) + 1;
          if (rectX + rectWidth > tileWidth || rectY + rectHeight > tileHeight)
            throw new InvalidDataException($"VMnc stream {this._streamIndex} carries a Hextile subrectangle outside its tile.");
          this._FillRectangle(x + tileX + rectX, y + tileY + rectY, rectWidth, rectHeight, color);
        }
      }
    }
  }

  private void _DecodeCursor(ref BigEndianReader reader, int width, int height, int hotX, int hotY) {
    reader.Skip(2, "cursor prefix");
    this._cursorWidth = width;
    this._cursorHeight = height;
    this._cursorHotX = hotX < width ? hotX : 0;
    this._cursorHotY = hotY < height ? hotY : 0;
    var pixels = checked(width * height);
    this._cursorBits = new uint[pixels];
    this._cursorMask = new uint[pixels];
    for (var i = 0; i < pixels; ++i)
      this._cursorBits[i] = this._ReadPixel(ref reader);
    for (var i = 0; i < pixels; ++i)
      this._cursorMask[i] = this._ReadPixel(ref reader);
  }

  private void _DecodeServerInitialization(ref BigEndianReader reader) {
    var bitsPerPixel = reader.ReadByte("RFB bits per pixel");
    _ = reader.ReadByte("RFB depth");
    this._bigEndian = reader.ReadByte("RFB endian flag") switch {
      0 => false,
      1 => true,
      var value => throw new InvalidDataException($"VMnc stream {this._streamIndex} carries invalid RFB endian flag {value}."),
    };
    var trueColor = reader.ReadByte("RFB true-colour flag");
    var redMaximum = reader.ReadUInt16("RFB red maximum");
    var greenMaximum = reader.ReadUInt16("RFB green maximum");
    var blueMaximum = reader.ReadUInt16("RFB blue maximum");
    var redShift = reader.ReadByte("RFB red shift");
    var greenShift = reader.ReadByte("RFB green shift");
    var blueShift = reader.ReadByte("RFB blue shift");
    reader.Skip(3, "RFB pixel-format padding");

    var expectedBits = this._bytesPerPixel << 3;
    if (bitsPerPixel == 24 && expectedBits == 32)
      bitsPerPixel = 32;
    if (bitsPerPixel != expectedBits)
      throw new InvalidDataException(
        $"VMnc stream {this._streamIndex} changes its stored depth from {expectedBits} to {bitsPerPixel} bits inside the stream.");
    if (trueColor != 1)
      throw new NotSupportedException("Indexed VMnc/RFB video carries no palette in the codec packet; only the self-describing true-colour form can be decoded faithfully.");
    if (redMaximum == 0 || greenMaximum == 0 || blueMaximum == 0)
      throw new InvalidDataException("A VMnc RFB true-colour descriptor contains a zero channel maximum.");
    if (redShift >= expectedBits || greenShift >= expectedBits || blueShift >= expectedBits)
      throw new InvalidDataException("A VMnc RFB channel shift lies outside the stored pixel word.");

    this._pixelDescriptor = new(true, redMaximum, greenMaximum, blueMaximum, redShift, greenShift, blueShift);
  }

  private void _ApplyCursor(Span<uint> display) {
    if (this._cursorBits == null || this._cursorMask == null)
      return;

    for (var row = 0; row < this._cursorHeight; ++row) {
      var destinationY = this._cursorY + row;
      if ((uint)destinationY >= (uint)this._height)
        continue;
      for (var column = 0; column < this._cursorWidth; ++column) {
        var destinationX = this._cursorX + column;
        if ((uint)destinationX >= (uint)this._width)
          continue;
        var cursorIndex = row * this._cursorWidth + column;
        var frameIndex = destinationY * this._width + destinationX;
        display[frameIndex] = (display[frameIndex] & this._cursorBits[cursorIndex]) ^ this._cursorMask[cursorIndex];
      }
    }
  }

  private byte[] _ToRgb24(ReadOnlySpan<uint> pixels) {
    if (!this._pixelDescriptor.IsTrueColor)
      throw new NotSupportedException(
        $"VMnc stream {this._streamIndex} uses 8-bit pixels before supplying an RFB true-colour descriptor.");

    var descriptor = this._pixelDescriptor;
    var result = new byte[checked(pixels.Length * 3)];
    for (var i = 0; i < pixels.Length; ++i) {
      var value = pixels[i];
      result[i * 3] = _Scale((value >> descriptor.RedShift) & descriptor.RedMaximum, descriptor.RedMaximum);
      result[i * 3 + 1] = _Scale((value >> descriptor.GreenShift) & descriptor.GreenMaximum, descriptor.GreenMaximum);
      result[i * 3 + 2] = _Scale((value >> descriptor.BlueShift) & descriptor.BlueMaximum, descriptor.BlueMaximum);
    }
    return result;
  }

  private uint _ReadPixel(ref BigEndianReader reader) => this._bytesPerPixel switch {
    1 => reader.ReadByte("pixel"),
    2 => this._bigEndian ? reader.ReadUInt16("pixel") : reader.ReadUInt16LittleEndian("pixel"),
    4 => this._bigEndian ? reader.ReadUInt32("pixel") : reader.ReadUInt32LittleEndian("pixel"),
    _ => throw new InvalidOperationException(),
  };

  private void _FillRectangle(int x, int y, int width, int height, uint value) {
    for (var row = 0; row < height; ++row)
      this._canvas.AsSpan((y + row) * this._width + x, width).Fill(value);
  }

  private void _RequireRectangle(int x, int y, int width, int height) {
    if (x > this._width - width || y > this._height - height)
      throw new InvalidDataException(
        $"VMnc stream {this._streamIndex} carries rectangle ({x},{y}) {width}x{height} outside its {this._width}x{this._height} canvas.");
  }

  private static byte _Scale(uint value, uint maximum)
    => checked((byte)((value * 255u + maximum / 2u) / maximum));

  private readonly record struct PixelDescriptor(
    bool IsTrueColor,
    uint RedMaximum,
    uint GreenMaximum,
    uint BlueMaximum,
    int RedShift,
    int GreenShift,
    int BlueShift
  );

  private ref struct BigEndianReader {
    private ReadOnlySpan<byte> _data;
    private readonly int _streamIndex;

    public BigEndianReader(ReadOnlySpan<byte> data, int streamIndex) {
      this._data = data;
      this._streamIndex = streamIndex;
    }

    public byte ReadByte(string field) {
      this._Require(1, field);
      var result = this._data[0];
      this._data = this._data[1..];
      return result;
    }

    public ushort ReadUInt16(string field) {
      this._Require(2, field);
      var result = BinaryPrimitives.ReadUInt16BigEndian(this._data);
      this._data = this._data[2..];
      return result;
    }

    public ushort ReadUInt16LittleEndian(string field) {
      this._Require(2, field);
      var result = BinaryPrimitives.ReadUInt16LittleEndian(this._data);
      this._data = this._data[2..];
      return result;
    }

    public uint ReadUInt32(string field) {
      this._Require(4, field);
      var result = BinaryPrimitives.ReadUInt32BigEndian(this._data);
      this._data = this._data[4..];
      return result;
    }

    public uint ReadUInt32LittleEndian(string field) {
      this._Require(4, field);
      var result = BinaryPrimitives.ReadUInt32LittleEndian(this._data);
      this._data = this._data[4..];
      return result;
    }

    public void Skip(int count, string field) {
      this._Require(count, field);
      this._data = this._data[count..];
    }

    private void _Require(int count, string field) {
      if (count < 0 || this._data.Length < count)
        throw new InvalidDataException(
          $"VMnc stream {this._streamIndex} ends inside {field}; {count} byte(s) are required and {this._data.Length} remain.");
    }
  }
}
