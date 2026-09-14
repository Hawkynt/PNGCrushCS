using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.FlicVideo;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Autodesk/DTA FLIC: palettised eight-bit FLI/FLC/FLX plus RGB555, RGB565 and BGR24
/// extended FLIC, including whole pictures and previous-frame delta updates.
/// </summary>
/// <remarks>
/// FLIC has I-like whole-picture chunks and P-like conditional-replenishment chunks only. Delta
/// chunks paint a persistent canvas left by the immediately preceding packet; the format has no
/// future reference and no B-frame equivalent.
/// <para/>
/// Fifteen-bit pixels have no native <see cref="PixelFormat"/> in the shared raw-image contract, so
/// they are widened losslessly to their exact RGB24 display values by bit replication. RGB565 and
/// BGR24 remain in their native packed forms. The internal reference canvas always keeps the coded
/// bytes, so delta arithmetic never depends on that presentation conversion.
/// </remarks>
public sealed class FlicVideoDecoder : IVideoCodecDecoder<FlicVideoDecoder> {

  private static readonly CodecTag _FLIC = CodecTag.FromCharacters("FLIC");
  private const int _PALETTE_BYTES = 256 * 3;

  private readonly int _width;
  private readonly int _height;
  private readonly int _depth;
  private readonly int _bytesPerPixel;
  private readonly byte[] _canvas;
  private readonly byte[] _palette = new byte[_PALETTE_BYTES];

  private FlicVideoDecoder(int width, int height, int depth) {
    this._width = width;
    this._height = height;
    this._depth = depth;
    this._bytesPerPixel = depth switch { 8 => 1, 15 or 16 => 2, 24 => 3, _ => throw new ArgumentOutOfRangeException(nameof(depth)) };
    this._canvas = new byte[checked(width * height * this._bytesPerPixel)];
  }

  public static string CodecName => "FLIC";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_FLIC);
  }

  public static FlicVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    var width = stream.Width;
    var height = stream.Height;
    if (width <= 0 || height <= 0)
      throw new InvalidOperationException(
        $"FLIC video stream {stream.Index} states a picture of {width}x{height}, which has no pixels.");

    var depth = stream.BitsPerPixel == 0 ? 8 : stream.BitsPerPixel;
    if (depth is not (8 or 15 or 16 or 24))
      throw new NotSupportedException(
        $"FLIC video stream {stream.Index} states {stream.BitsPerPixel} bits per pixel. This codec reads "
        + "the renderable 8-, 15-, 16- and 24-bit FLIC pixel formats.");

    var bytesPerPixel = depth switch { 8 => 1, 15 or 16 => 2, 24 => 3, _ => 0 };
    if ((long)width * height * bytesPerPixel > int.MaxValue)
      throw new InvalidOperationException(
        $"FLIC video stream {stream.Index} states a {width}x{height} picture at {depth} bits per pixel, "
        + "which is more coded image data than can be held.");

    return new(width, height, depth);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    this._DecodeSubChunks(packet.Data.Span);

    frame = this._depth switch {
      8 => new() {
        Width = this._width,
        Height = this._height,
        Format = PixelFormat.Indexed8,
        PixelData = (byte[])this._canvas.Clone(),
        Palette = (byte[])this._palette.Clone(),
        PaletteCount = 256,
      },
      15 => new() {
        Width = this._width,
        Height = this._height,
        Format = PixelFormat.Rgb24,
        PixelData = this._ExpandRgb555(),
      },
      16 => new() {
        Width = this._width,
        Height = this._height,
        Format = PixelFormat.Rgb565,
        PixelData = (byte[])this._canvas.Clone(),
      },
      24 => new() {
        Width = this._width,
        Height = this._height,
        Format = PixelFormat.Bgr24,
        PixelData = (byte[])this._canvas.Clone(),
      },
      _ => throw new InvalidOperationException(),
    };
    return true;
  }

  private byte[] _ExpandRgb555() {
    var result = new byte[this._width * this._height * 3];
    for (var pixel = 0; pixel < this._width * this._height; ++pixel) {
      var packed = BinaryPrimitives.ReadUInt16LittleEndian(this._canvas.AsSpan(pixel * 2));
      result[pixel * 3] = ChannelScaling.Expand5((packed >> 10) & 31);
      result[pixel * 3 + 1] = ChannelScaling.Expand5((packed >> 5) & 31);
      result[pixel * 3 + 2] = ChannelScaling.Expand5(packed & 31);
    }
    return result;
  }

  // ============================================================================================
  // Sub-chunk walk
  // ============================================================================================

  private void _DecodeSubChunks(ReadOnlySpan<byte> data) {
    var at = 0;
    while (at < data.Length) {
      if (at + 6 > data.Length)
        throw new InvalidDataException(
          $"A FLIC frame ends {data.Length - at} byte(s) into a sub-chunk header, which is six bytes.");

      var size = BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);
      var type = BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 4)..]);
      if (size < 6 || size > data.Length - at)
        throw new InvalidDataException(
          $"A FLIC sub-chunk of type {type} at byte {at} states a size of {size}, which "
          + (size < 6 ? "is shorter than its own six-byte header." : $"runs past the frame's {data.Length} bytes."));

      var payload = data.Slice(at + 6, checked((int)size) - 6);
      switch (type) {
        case FliChunkType.COLOR256:
          if (this._depth == 8) this._DecodeColor(payload, sixBit: false);
          break;
        case FliChunkType.COLOR64:
          if (this._depth == 8) this._DecodeColor(payload, sixBit: true);
          break;
        case FliChunkType.SS2:
          if (this._depth == 8) this._DecodeSs2(payload);
          else this._DecodePixelDelta(payload, "FLI_SS2");
          break;
        case FliChunkType.LC:
          if (this._depth != 8)
            throw new NotSupportedException($"FLI_LC is byte-oriented and is not defined for {this._depth}-bit FLIC pixels.");
          this._DecodeLc(payload);
          break;
        case FliChunkType.BLACK:
          Array.Clear(this._canvas);
          break;
        case FliChunkType.BRUN:
          this._DecodeByteBrun(payload);
          break;
        case FliChunkType.COPY:
          this._DecodeCopy(payload, "FLI_COPY");
          break;
        case FliChunkType.PSTAMP:
          break;
        case FliChunkType.DTA_BRUN:
          this._RequireTrueColour(type);
          this._DecodePixelBrun(payload);
          break;
        case FliChunkType.DTA_COPY:
          this._RequireTrueColour(type);
          this._DecodeCopy(payload, "DTA_COPY");
          break;
        case FliChunkType.DTA_LC:
          this._RequireTrueColour(type);
          this._DecodePixelDelta(payload, "DTA_LC");
          break;
        default:
          throw new NotSupportedException(
            $"A FLIC frame carries a sub-chunk of type {type} at byte {at}, which this decoder does not render. "
            + "Supported image chunks are {4, 7, 11, 12, 13, 15, 16, 18, 25, 26, 27}.");
      }

      at += checked((int)size);
    }
  }

  private void _RequireTrueColour(ushort type) {
    if (this._depth == 8)
      throw new NotSupportedException($"DTA FLIC chunk type {type} codes whole pixels and is not valid for an 8-bit palettised stream.");
  }

  // ============================================================================================
  // Palette chunks
  // ============================================================================================

  private void _DecodeColor(ReadOnlySpan<byte> payload, bool sixBit) {
    var at = 0;
    var packetCount = _ReadU16(payload, ref at, "a palette chunk's packet count");
    var index = 0;

    for (var packet = 0; packet < packetCount; ++packet) {
      index += _ReadU8(payload, ref at, "a palette packet's skip count");
      var changeByte = _ReadU8(payload, ref at, "a palette packet's change count");
      var change = changeByte == 0 ? 256 : changeByte;
      if (index + change > 256)
        throw new InvalidDataException(
          $"A FLIC palette chunk writes {change} colour(s) starting at index {index}, which reaches past the 256 entries a palette holds.");

      for (var entry = 0; entry < change; ++entry, ++index) {
        var r = _ReadU8(payload, ref at, "a palette entry's red component");
        var g = _ReadU8(payload, ref at, "a palette entry's green component");
        var b = _ReadU8(payload, ref at, "a palette entry's blue component");
        this._palette[index * 3] = sixBit ? ChannelScaling.Expand6(r) : r;
        this._palette[index * 3 + 1] = sixBit ? ChannelScaling.Expand6(g) : g;
        this._palette[index * 3 + 2] = sixBit ? ChannelScaling.Expand6(b) : b;
      }
    }
  }

  // ============================================================================================
  // Whole pictures
  // ============================================================================================

  /// <summary>Standard BRUN is byte-oriented even when an FLX pixel occupies two bytes.</summary>
  private void _DecodeByteBrun(ReadOnlySpan<byte> payload) {
    var at = 0;
    var rowBytes = checked(this._width * this._bytesPerPixel);
    for (var row = 0; row < this._height; ++row) {
      _ReadU8(payload, ref at, "a byte-run row's packet count");
      var rowStart = row * rowBytes;
      var x = 0;
      while (x < rowBytes) {
        var count = unchecked((sbyte)_ReadU8(payload, ref at, "a byte-run packet's count"));
        if (count > 0) {
          var value = _ReadU8(payload, ref at, "a byte-run packet's replicated byte");
          _RefuseRunPastRow(x, count, row, rowBytes, "byte");
          this._canvas.AsSpan(rowStart + x, count).Fill(value);
          x += count;
        } else if (count < 0) {
          var n = -count;
          _RefuseRunPastRow(x, n, row, rowBytes, "byte");
          _ReadBytes(payload, ref at, this._canvas.AsSpan(rowStart + x, n), "a byte-run packet's literal bytes");
          x += n;
        } else
          throw new NotSupportedException($"A FLI_BRUN packet on row {row} states a count of zero, whose byte-run meaning is ambiguous.");
      }
    }
  }

  /// <summary>DTA BRUN is identical in sign convention but every count measures complete pixels.</summary>
  private void _DecodePixelBrun(ReadOnlySpan<byte> payload) {
    var at = 0;
    for (var row = 0; row < this._height; ++row) {
      _ReadU8(payload, ref at, "a DTA byte-run row's packet count");
      var x = 0;
      while (x < this._width) {
        var count = unchecked((sbyte)_ReadU8(payload, ref at, "a DTA byte-run packet's count"));
        if (count > 0) {
          _RefuseRunPastRow(x, count, row, this._width, "pixel");
          var pixel = _ReadPixel(payload, ref at, "a DTA byte-run packet's replicated pixel");
          for (var i = 0; i < count; ++i)
            pixel.CopyTo(this._canvas.AsSpan(((row * this._width) + x++) * this._bytesPerPixel, this._bytesPerPixel));
        } else if (count < 0) {
          var n = -count;
          _RefuseRunPastRow(x, n, row, this._width, "pixel");
          var bytes = checked(n * this._bytesPerPixel);
          _ReadBytes(payload, ref at, this._canvas.AsSpan(((row * this._width) + x) * this._bytesPerPixel, bytes),
            "a DTA byte-run packet's literal pixels");
          x += n;
        } else
          throw new NotSupportedException($"A DTA_BRUN packet on row {row} states a count of zero, whose run meaning is ambiguous.");
      }
    }
  }

  private void _DecodeCopy(ReadOnlySpan<byte> payload, string name) {
    var packedStride = checked(this._width * this._bytesPerPixel);
    var packedLength = checked(packedStride * this._height);
    if (payload.Length == packedLength) {
      payload.CopyTo(this._canvas);
      return;
    }

    // FFmpeg accepts the historical padding emitted by real files: 8-bit COPY rows align to a
    // dword, while high/true-colour variants may pad an odd pixel count to the next even pixel.
    var paddedStride = this._depth == 8
      ? (packedStride + 3) & ~3
      : checked(((this._width + 1) & ~1) * this._bytesPerPixel);
    var paddedLength = checked(paddedStride * this._height);
    if (payload.Length != paddedLength)
      throw new InvalidDataException(
        $"A {name} chunk carries {payload.Length} byte(s) for a {this._width}x{this._height} {this._depth}-bit picture, "
        + $"which needs either {packedLength} packed byte(s) or {paddedLength} byte(s) in the tolerated padded layout.");

    for (var row = 0; row < this._height; ++row)
      payload.Slice(row * paddedStride, packedStride).CopyTo(this._canvas.AsSpan(row * packedStride, packedStride));
  }

  // ============================================================================================
  // Eight-bit delta chunks
  // ============================================================================================

  private void _DecodeLc(ReadOnlySpan<byte> payload) {
    var at = 0;
    var firstLine = _ReadU16(payload, ref at, "a delta chunk's first changed line");
    var lineCount = _ReadU16(payload, ref at, "a delta chunk's line count");
    _RefuseLinesPastPicture(firstLine, lineCount, this._height, "FLI_LC");

    for (var line = 0; line < lineCount; ++line) {
      var row = firstLine + line;
      var rowStart = row * this._width;
      var packetCount = _ReadU8(payload, ref at, "a delta line's packet count");
      var x = 0;
      for (var packet = 0; packet < packetCount; ++packet) {
        x += _ReadU8(payload, ref at, "a delta packet's skip count");
        var size = unchecked((sbyte)_ReadU8(payload, ref at, "a delta packet's size"));
        if (size > 0) {
          _RefuseRunPastRow(x, size, row, this._width, "pixel");
          _ReadBytes(payload, ref at, this._canvas.AsSpan(rowStart + x, size), "a delta packet's literal pixels");
          x += size;
        } else if (size < 0) {
          var n = -size;
          var value = _ReadU8(payload, ref at, "a delta packet's replicated pixel");
          _RefuseRunPastRow(x, n, row, this._width, "pixel");
          this._canvas.AsSpan(rowStart + x, n).Fill(value);
          x += n;
        }
      }
    }
  }

  private void _DecodeSs2(ReadOnlySpan<byte> payload) {
    var at = 0;
    var lineCount = _ReadU16(payload, ref at, "a word-delta chunk's line count");
    var y = 0;

    for (var line = 0; line < lineCount; ++line) {
      var word = _ReadU16(payload, ref at, "a word-delta opcode");
      while ((word & 0xC000) == 0xC000) {
        y += -unchecked((short)word);
        word = _ReadU16(payload, ref at, "a word-delta opcode");
      }

      if (y >= this._height)
        throw new InvalidDataException($"A FLI_SS2 chunk's line skips reach row {y} of a {this._height}-row picture.");
      var rowStart = y * this._width;

      if ((word & 0xC000) == 0x8000) {
        this._canvas[rowStart + this._width - 1] = unchecked((byte)word);
        word = _ReadU16(payload, ref at, "a word-delta packet count");
      }
      if ((word & 0xC000) != 0)
        throw new InvalidDataException($"A FLI_SS2 packet-count opcode has unsupported high bits 0x{word & 0xC000:X4}.");

      var packetCount = word;
      var x = 0;
      for (var packet = 0; packet < packetCount; ++packet) {
        x += _ReadU8(payload, ref at, "a word-delta packet's skip count");
        var size = unchecked((sbyte)_ReadU8(payload, ref at, "a word-delta packet's size"));
        if (size >= 0) {
          var pixels = size * 2;
          _RefuseRunPastRow(x, pixels, y, this._width, "pixel");
          _ReadBytes(payload, ref at, this._canvas.AsSpan(rowStart + x, pixels), "a word-delta packet's literal pixel pairs");
          x += pixels;
        } else {
          var n = -size;
          _RefuseRunPastRow(x, n * 2, y, this._width, "pixel");
          var low = _ReadU8(payload, ref at, "a word-delta packet's replicated low pixel");
          var high = _ReadU8(payload, ref at, "a word-delta packet's replicated high pixel");
          for (var i = 0; i < n; ++i) {
            this._canvas[rowStart + x++] = low;
            this._canvas[rowStart + x++] = high;
          }
        }
      }
      ++y;
    }
  }

  // ============================================================================================
  // DTA/true-colour delta chunks
  // ============================================================================================

  private void _DecodePixelDelta(ReadOnlySpan<byte> payload, string name) {
    var at = 0;
    var lineCount = _ReadU16(payload, ref at, $"a {name} chunk's changed-line count");
    var y = 0;

    for (var line = 0; line < lineCount; ++line) {
      var opcode = unchecked((short)_ReadU16(payload, ref at, $"a {name} line opcode"));
      while (opcode < 0) {
        y += -opcode;
        opcode = unchecked((short)_ReadU16(payload, ref at, $"a {name} line opcode"));
      }
      if (y >= this._height)
        throw new InvalidDataException($"A {name} chunk's line skips reach row {y} of a {this._height}-row picture.");

      var packetCount = opcode;
      var x = 0;
      for (var packet = 0; packet < packetCount; ++packet) {
        x += _ReadU8(payload, ref at, $"a {name} packet's pixel skip count");
        var count = unchecked((sbyte)_ReadU8(payload, ref at, $"a {name} packet's pixel count"));
        if (count > 0) {
          _RefuseRunPastRow(x, count, y, this._width, "pixel");
          var bytes = checked(count * this._bytesPerPixel);
          _ReadBytes(payload, ref at,
            this._canvas.AsSpan(((y * this._width) + x) * this._bytesPerPixel, bytes),
            $"a {name} packet's literal pixels");
          x += count;
        } else if (count < 0) {
          var n = -count;
          _RefuseRunPastRow(x, n, y, this._width, "pixel");
          var pixel = _ReadPixel(payload, ref at, $"a {name} packet's replicated pixel");
          for (var i = 0; i < n; ++i)
            pixel.CopyTo(this._canvas.AsSpan(((y * this._width) + x++) * this._bytesPerPixel, this._bytesPerPixel));
        }
      }
      ++y;
    }
  }

  // ============================================================================================
  // Reading and bounds
  // ============================================================================================

  private ReadOnlySpan<byte> _ReadPixel(ReadOnlySpan<byte> data, ref int at, string what) {
    if (at + this._bytesPerPixel > data.Length)
      throw new InvalidDataException($"A FLIC chunk ends before {what}.");
    var result = data.Slice(at, this._bytesPerPixel);
    at += this._bytesPerPixel;
    return result;
  }

  private static byte _ReadU8(ReadOnlySpan<byte> data, ref int at, string what) {
    if (at >= data.Length)
      throw new InvalidDataException($"A FLIC chunk ends before {what}, 1 byte short.");
    return data[at++];
  }

  private static ushort _ReadU16(ReadOnlySpan<byte> data, ref int at, string what) {
    if (at + 2 > data.Length)
      throw new InvalidDataException($"A FLIC chunk ends before {what}, {at + 2 - data.Length} byte(s) short.");
    var value = BinaryPrimitives.ReadUInt16LittleEndian(data[at..]);
    at += 2;
    return value;
  }

  private static void _ReadBytes(ReadOnlySpan<byte> data, ref int at, Span<byte> destination, string what) {
    if (at + destination.Length > data.Length)
      throw new InvalidDataException($"A FLIC chunk ends before {what}, {at + destination.Length - data.Length} byte(s) short.");
    data.Slice(at, destination.Length).CopyTo(destination);
    at += destination.Length;
  }

  private static void _RefuseRunPastRow(int x, int count, int row, int width, string unit) {
    if (x < 0 || count < 0 || x + (long)count > width)
      throw new InvalidDataException(
        $"A FLIC packet on row {row} writes {count} {unit}(s) starting at column {x}, which reaches past the row width of {width}.");
  }

  private static void _RefuseLinesPastPicture(int firstLine, int lineCount, int height, string chunkName) {
    if (firstLine + (long)lineCount > height)
      throw new InvalidDataException(
        $"A {chunkName} chunk states {lineCount} line(s) starting at row {firstLine}, which reaches past the picture's height of {height}.");
  }
}
