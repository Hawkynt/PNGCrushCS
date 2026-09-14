using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.FlicVideo;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Autodesk/DTA FLIC at 8, 15, 16 and 24 bits per pixel, using whole-image key frames and
/// previous-frame delta updates.
/// </summary>
/// <remarks>
/// The prediction graph is deliberately simple because the format is: an SS2/DTA_LC frame references
/// only the canvas produced immediately before it. There are no future references and therefore no
/// B-frame equivalent or reordering delay.
/// <para/>
/// Eight-bit input remains exact <see cref="PixelFormat.Indexed8"/> with palette changes carried as
/// COLOR256. RGB565 and BGR24 are coded in their native layouts. The shared raw-image contract has no
/// RGB555 type, so 15-bit encoding accepts RGB24 only when every channel is exactly representable by
/// five bits; arbitrary RGB24 is refused rather than silently quantised.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class FlicVideoEncoder : IVideoCodecEncoder<FlicVideoEncoder> {

  private static readonly CodecTag _FLIC = CodecTag.FromCharacters("FLIC");
  private const int _PALETTE_ENTRIES = 256;
  private const int _PALETTE_BYTES = _PALETTE_ENTRIES * 3;
  private const int _MAX_RUN = 127;

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;
  private readonly int _depth;
  private readonly int _bytesPerPixel;
  private readonly byte[] _previousPalette = new byte[_PALETTE_BYTES];
  private byte[]? _previousPixels;

  private FlicVideoEncoder(MediaStreamInfo stream, int depth) {
    this._requested = stream;
    this._width = stream.Width;
    this._height = stream.Height;
    this._depth = depth;
    this._bytesPerPixel = depth switch { 8 => 1, 15 or 16 => 2, 24 => 3, _ => throw new ArgumentOutOfRangeException(nameof(depth)) };
  }

  public static string CodecName => "FLIC";
  public static CodecTag Codec => _FLIC;

  public static FlicVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("FLIC can only encode a video stream.");
    if (stream.Width is <= 0 or > ushort.MaxValue || stream.Height is <= 0 or > ushort.MaxValue)
      throw new NotSupportedException(
        $"A FLIC encoder needs a picture size fitting its unsigned 16-bit header fields; {stream.Width}x{stream.Height} was supplied.");

    var depth = stream.BitsPerPixel == 0 ? 8 : stream.BitsPerPixel;
    if (depth is not (8 or 15 or 16 or 24))
      throw new NotSupportedException($"FLIC encoding supports 8, 15, 16 and 24 bits per pixel; {depth} was requested.");
    var bytesPerPixel = depth switch { 8 => 1, 15 or 16 => 2, 24 => 3, _ => 0 };
    if ((long)stream.Width * stream.Height * bytesPerPixel > int.MaxValue)
      throw new NotSupportedException(
        $"A picture of {stream.Width}x{stream.Height} at {depth} bits per pixel is more coded image data than a FLIC frame can hold.");

    return new(stream, depth);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException($"FLIC geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var palette = this._depth == 8 ? this._Palette(frame) : null;
    var pixels = this._Pixels(frame);
    if (this._depth == 8)
      this._ValidateIndices(pixels, frame.PaletteCount);

    var paletteChunk = palette == null ? null : this._PaletteChunk(palette);
    var (pictureChunk, isKeyFrame) = this._PictureChunk(pixels);
    var data = _Join(paletteChunk, pictureChunk);

    if (palette != null)
      palette.CopyTo(this._previousPalette, 0);
    this._previousPixels = pixels;

    packet = new(
      StreamIndex: this._requested.Index,
      Data: data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: isKeyFrame);
    return true;
  }

  public MediaStreamInfo DescribeStream() => new() {
    Index = this._requested.Index,
    Kind = MediaStreamKind.Video,
    Codec = _FLIC,
    Handler = _FLIC,
    TimeBase = this._requested.TimeBase,
    FrameRate = this._requested.FrameRate,
    DeclaredFrameCount = this._requested.DeclaredFrameCount,
    Width = this._width,
    Height = this._height,
    BitsPerPixel = this._depth,
    Language = this._requested.Language,
    Name = this._requested.Name,
  };

  // ============================================================================================
  // Input normalization
  // ============================================================================================

  private byte[] _Pixels(RawImage frame) => this._depth switch {
    8 => this._ExactBytes(frame, PixelFormat.Indexed8, 1),
    15 => this._PackRgb555(frame),
    16 => this._ExactBytes(frame, PixelFormat.Rgb565, 2),
    24 => this._ExactBytes(frame, PixelFormat.Bgr24, 3),
    _ => throw new InvalidOperationException(),
  };

  private byte[] _ExactBytes(RawImage frame, PixelFormat expected, int bytesPerPixel) {
    if (frame.Format != expected)
      throw new NotSupportedException(
        $"A {this._depth}-bit FLIC stream takes {expected} pictures; {frame.Format} would require a pixel conversion outside the codec.");
    return frame.PixelData.AsSpan(0, checked(this._width * this._height * bytesPerPixel)).ToArray();
  }

  private byte[] _PackRgb555(RawImage frame) {
    if (frame.Format != PixelFormat.Rgb24)
      throw new NotSupportedException(
        $"A 15-bit FLIC stream takes exact RGB555 display values supplied as {PixelFormat.Rgb24}; {frame.Format} was supplied.");

    var source = frame.PixelData.AsSpan(0, checked(this._width * this._height * 3));
    var result = new byte[checked(this._width * this._height * 2)];
    for (var pixel = 0; pixel < this._width * this._height; ++pixel) {
      var r = source[pixel * 3];
      var g = source[pixel * 3 + 1];
      var b = source[pixel * 3 + 2];
      var r5 = r >> 3;
      var g5 = g >> 3;
      var b5 = b >> 3;
      if (ChannelScaling.Expand5(r5) != r || ChannelScaling.Expand5(g5) != g || ChannelScaling.Expand5(b5) != b)
        throw new NotSupportedException(
          $"Pixel {pixel % this._width},{pixel / this._width} is RGB({r},{g},{b}), which is not exactly representable in RGB555. "
          + "Quantising true colour is outside this lossless codec writer.");
      BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(pixel * 2), checked((ushort)((r5 << 10) | (g5 << 5) | b5)));
    }
    return result;
  }

  private byte[] _Palette(RawImage frame) {
    if (frame.Format != PixelFormat.Indexed8)
      throw new NotSupportedException(
        $"An 8-bit FLIC stream takes only {PixelFormat.Indexed8} pictures; {frame.Format} would have to be quantised first.");
    var sourcePalette = frame.Palette;
    if (sourcePalette == null || frame.PaletteCount <= 0)
      throw new InvalidDataException("A palettised picture without a palette cannot be coded as FLIC.");
    if (frame.PaletteCount > _PALETTE_ENTRIES)
      throw new InvalidDataException($"The picture states {frame.PaletteCount} palette entries, but FLIC holds at most 256.");
    var needed = frame.PaletteCount * 3;
    if (sourcePalette.Length < needed)
      throw new InvalidDataException($"The picture states {frame.PaletteCount} palette entries but does not carry all of them.");
    var result = new byte[_PALETTE_BYTES];
    sourcePalette.AsSpan(0, needed).CopyTo(result);
    return result;
  }

  private void _ValidateIndices(ReadOnlySpan<byte> pixels, int paletteCount) {
    for (var i = 0; i < pixels.Length; ++i)
      if (pixels[i] >= paletteCount)
        throw new InvalidDataException(
          $"Pixel {i % this._width},{i / this._width} is palette index {pixels[i]} and the picture declares {paletteCount} palette entries.");
  }

  // ============================================================================================
  // Palette updates
  // ============================================================================================

  private byte[]? _PaletteChunk(ReadOnlySpan<byte> palette) {
    if (this._previousPixels == null)
      return _Color256Chunk(palette, 0, _PALETTE_ENTRIES);

    var first = -1;
    var last = -1;
    for (var entry = 0; entry < _PALETTE_ENTRIES; ++entry) {
      var at = entry * 3;
      if (palette.Slice(at, 3).SequenceEqual(this._previousPalette.AsSpan(at, 3)))
        continue;
      first = first < 0 ? entry : first;
      last = entry;
    }
    return first < 0 ? null : _Color256Chunk(palette, first, last - first + 1);
  }

  private static byte[] _Color256Chunk(ReadOnlySpan<byte> palette, int first, int count) {
    var payload = new byte[4 + count * 3];
    BinaryPrimitives.WriteUInt16LittleEndian(payload, 1);
    payload[2] = checked((byte)first);
    payload[3] = count == _PALETTE_ENTRIES ? (byte)0 : checked((byte)count);
    palette.Slice(first * 3, count * 3).CopyTo(payload.AsSpan(4));
    return _Chunk(FliChunkType.COLOR256, payload);
  }

  // ============================================================================================
  // Whole pictures and P-like deltas
  // ============================================================================================

  private (byte[]? Chunk, bool IsKeyFrame) _PictureChunk(ReadOnlySpan<byte> pixels) {
    if (this._previousPixels != null && pixels.SequenceEqual(this._previousPixels))
      return (null, false);
    if (_IsAllZero(pixels))
      return (_Chunk(FliChunkType.BLACK, []), true);

    var whole = this._depth == 8
      ? _Chunk(FliChunkType.BRUN, this._ByteBrun(pixels))
      : _Chunk(FliChunkType.DTA_BRUN, this._PixelBrun(pixels));

    if (this._previousPixels == null)
      return (whole, true);

    var delta = this._depth == 8
      ? _Chunk(FliChunkType.SS2, this._Ss2(this._previousPixels, pixels))
      : _Chunk(FliChunkType.DTA_LC, this._PixelDelta(this._previousPixels, pixels));
    return delta.Length < whole.Length ? (delta, false) : (whole, true);
  }

  private byte[] _ByteBrun(ReadOnlySpan<byte> pixels) {
    using var output = new MemoryStream();
    var rowBytes = checked(this._width * this._bytesPerPixel);
    for (var y = 0; y < this._height; ++y)
      _WriteByteRuns(output, pixels.Slice(y * rowBytes, rowBytes));
    return output.ToArray();
  }

  private static void _WriteByteRuns(Stream output, ReadOnlySpan<byte> row) {
    using var packets = new MemoryStream();
    var packetCount = 0;
    var x = 0;
    while (x < row.Length) {
      var repeated = 1;
      while (repeated < _MAX_RUN && x + repeated < row.Length && row[x + repeated] == row[x]) ++repeated;
      if (repeated >= 2) {
        packets.WriteByte((byte)repeated);
        packets.WriteByte(row[x]);
        x += repeated;
      } else {
        var start = x++;
        while (x < row.Length && x - start < _MAX_RUN) {
          if (x + 1 < row.Length && row[x] == row[x + 1]) break;
          ++x;
        }
        var count = x - start;
        packets.WriteByte(unchecked((byte)-count));
        packets.Write(row.Slice(start, count));
      }
      ++packetCount;
    }
    output.WriteByte(packetCount <= byte.MaxValue ? (byte)packetCount : (byte)0);
    packets.Position = 0;
    packets.CopyTo(output);
  }

  private byte[] _PixelBrun(ReadOnlySpan<byte> pixels) {
    using var output = new MemoryStream();
    for (var y = 0; y < this._height; ++y) {
      using var packets = new MemoryStream();
      var packetCount = 0;
      var x = 0;
      while (x < this._width) {
        var repeated = 1;
        while (repeated < _MAX_RUN && x + repeated < this._width
               && this._PixelEquals(pixels, y, x, x + repeated)) ++repeated;
        if (repeated >= 2) {
          packets.WriteByte((byte)repeated);
          packets.Write(pixels.Slice(((y * this._width) + x) * this._bytesPerPixel, this._bytesPerPixel));
          x += repeated;
        } else {
          var start = x++;
          while (x < this._width && x - start < _MAX_RUN) {
            if (x + 1 < this._width && this._PixelEquals(pixels, y, x, x + 1)) break;
            ++x;
          }
          var count = x - start;
          packets.WriteByte(unchecked((byte)-count));
          packets.Write(pixels.Slice(((y * this._width) + start) * this._bytesPerPixel, count * this._bytesPerPixel));
        }
        ++packetCount;
      }
      output.WriteByte(packetCount <= byte.MaxValue ? (byte)packetCount : (byte)0);
      packets.Position = 0;
      packets.CopyTo(output);
    }
    return output.ToArray();
  }

  private bool _PixelEquals(ReadOnlySpan<byte> pixels, int y, int a, int b) {
    var row = y * this._width;
    return pixels.Slice((row + a) * this._bytesPerPixel, this._bytesPerPixel)
      .SequenceEqual(pixels.Slice((row + b) * this._bytesPerPixel, this._bytesPerPixel));
  }

  /// <summary>Writes FLC SS2 using literal word packets; unchanged gaps remain references.</summary>
  private byte[] _Ss2(ReadOnlySpan<byte> previous, ReadOnlySpan<byte> current) {
    using var output = new MemoryStream();
    var changedLines = _CountChangedLines(previous, current, this._width, this._height, 1);
    _WriteU16(output, checked((ushort)changedLines));
    var yCursor = 0;

    for (var y = 0; y < this._height; ++y) {
      var oldRow = previous.Slice(y * this._width, this._width);
      var newRow = current.Slice(y * this._width, this._width);
      if (oldRow.SequenceEqual(newRow)) continue;

      _WriteLineSkips(output, y - yCursor, 0x4000);
      var lastOpcode = false;
      var scanWidth = this._width;
      if ((this._width & 1) != 0 && oldRow[^1] != newRow[^1]) {
        _WriteU16(output, (ushort)(0x8000 | newRow[^1]));
        lastOpcode = true;
        --scanWidth;
      }

      var first = -1;
      var last = -1;
      for (var x = 0; x < scanWidth; ++x)
        if (oldRow[x] != newRow[x]) { first = first < 0 ? x : first; last = x; }

      if (first < 0) {
        _WriteU16(output, 0);
        yCursor = y + 1;
        continue;
      }

      if (((last - first + 1) & 1) != 0) {
        if (first > 0) --first;
        else ++last;
      }

      using var packets = new MemoryStream();
      var packetCount = 0;
      var cursor = 0;
      var skip = first;
      while (skip > byte.MaxValue) {
        packets.WriteByte(byte.MaxValue);
        packets.WriteByte(0);
        skip -= byte.MaxValue;
        cursor += byte.MaxValue;
        ++packetCount;
      }

      var xPos = first;
      var firstPacket = true;
      while (xPos <= last) {
        var words = Math.Min(_MAX_RUN, (last - xPos + 1) / 2);
        packets.WriteByte(firstPacket ? checked((byte)skip) : (byte)0);
        packets.WriteByte(checked((byte)words));
        packets.Write(newRow.Slice(xPos, words * 2));
        xPos += words * 2;
        cursor = xPos;
        firstPacket = false;
        ++packetCount;
      }

      if (packetCount > 0x3FFF)
        throw new NotSupportedException($"A FLIC SS2 line needs {packetCount} packets, exceeding its 14-bit packet count.");
      _WriteU16(output, checked((ushort)packetCount));
      packets.Position = 0;
      packets.CopyTo(output);
      yCursor = y + 1;
      _ = lastOpcode;
      _ = cursor;
    }
    return output.ToArray();
  }

  private byte[] _PixelDelta(ReadOnlySpan<byte> previous, ReadOnlySpan<byte> current) {
    using var output = new MemoryStream();
    var changedLines = _CountChangedLines(previous, current, this._width, this._height, this._bytesPerPixel);
    _WriteU16(output, checked((ushort)changedLines));
    var yCursor = 0;

    for (var y = 0; y < this._height; ++y) {
      var rowBytes = this._width * this._bytesPerPixel;
      var oldRow = previous.Slice(y * rowBytes, rowBytes);
      var newRow = current.Slice(y * rowBytes, rowBytes);
      if (oldRow.SequenceEqual(newRow)) continue;

      _WriteLineSkips(output, y - yCursor, short.MaxValue);
      var first = 0;
      while (first < this._width && this._SamePixel(oldRow, newRow, first)) ++first;
      var last = this._width - 1;
      while (last > first && this._SamePixel(oldRow, newRow, last)) --last;

      using var packets = new MemoryStream();
      var packetCount = 0;
      var skip = first;
      while (skip > byte.MaxValue) {
        packets.WriteByte(byte.MaxValue);
        packets.WriteByte(0);
        skip -= byte.MaxValue;
        ++packetCount;
      }

      var x = first;
      var firstPacket = true;
      while (x <= last) {
        var count = Math.Min(_MAX_RUN, last - x + 1);
        packets.WriteByte(firstPacket ? checked((byte)skip) : (byte)0);
        packets.WriteByte(checked((byte)count));
        packets.Write(newRow.Slice(x * this._bytesPerPixel, count * this._bytesPerPixel));
        x += count;
        firstPacket = false;
        ++packetCount;
      }

      if (packetCount > short.MaxValue)
        throw new NotSupportedException($"A DTA_LC line needs {packetCount} packets, exceeding its signed 16-bit packet count.");
      _WriteU16(output, checked((ushort)packetCount));
      packets.Position = 0;
      packets.CopyTo(output);
      yCursor = y + 1;
    }
    return output.ToArray();
  }

  private bool _SamePixel(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, int x)
    => a.Slice(x * this._bytesPerPixel, this._bytesPerPixel).SequenceEqual(b.Slice(x * this._bytesPerPixel, this._bytesPerPixel));

  private static int _CountChangedLines(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, int width, int height, int bytesPerPixel) {
    var rowBytes = checked(width * bytesPerPixel);
    var result = 0;
    for (var y = 0; y < height; ++y)
      if (!a.Slice(y * rowBytes, rowBytes).SequenceEqual(b.Slice(y * rowBytes, rowBytes))) ++result;
    return result;
  }

  private static void _WriteLineSkips(Stream output, int count, int maximum) {
    while (count > 0) {
      var skip = Math.Min(count, maximum);
      _WriteU16(output, unchecked((ushort)-skip));
      count -= skip;
    }
  }

  private static bool _IsAllZero(ReadOnlySpan<byte> values) {
    foreach (var value in values)
      if (value != 0) return false;
    return true;
  }

  // ============================================================================================
  // Framing
  // ============================================================================================

  private static byte[] _Chunk(ushort type, ReadOnlySpan<byte> payload) {
    var result = new byte[6 + payload.Length];
    BinaryPrimitives.WriteUInt32LittleEndian(result, checked((uint)result.Length));
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), type);
    payload.CopyTo(result.AsSpan(6));
    return result;
  }

  private static byte[] _Join(byte[]? first, byte[]? second) {
    if (first == null) return second ?? [];
    if (second == null) return first;
    var result = new byte[first.Length + second.Length];
    first.CopyTo(result, 0);
    second.CopyTo(result, first.Length);
    return result;
  }

  private static void _WriteU16(Stream output, ushort value) {
    Span<byte> bytes = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
    output.Write(bytes);
  }
}
