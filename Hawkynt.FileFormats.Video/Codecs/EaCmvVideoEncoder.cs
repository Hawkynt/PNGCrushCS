using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.Ea;
using FileFormat.Core;
using EaChunks = FileFormat.Ea.EaChunkType;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Electronic Arts CMV losslessly from eight-bit palettised pictures.
/// </summary>
/// <remarks>
/// CMV has one intra spelling — the complete raster of palette indices — and one inter spelling made
/// from 4x4 blocks. Each inter block either copies an exact motion-compensated block from the previous
/// picture, copies one from the second-last picture through an escape, or stores sixteen literal
/// indices. This writer tries every representable motion vector in increasing Manhattan distance and
/// falls back to the literal form, then uses the inter picture only when it is smaller than the raw
/// intra picture. It therefore never changes a palette index to improve compression.
/// <para/>
/// The codec itself is eight-bit indexed. True-colour input is refused rather than quantised because
/// choosing which 256 colours survive would make a nominally lossless writer lossy. Palette changes
/// are carried by <c>MVIh</c> immediately before the affected <c>MVIf</c>; every intra packet carries a
/// complete palette header so its key-frame flag really means decoding can begin there, while inter
/// packets carry only the smallest contiguous palette span containing changed entries.
/// <para/>
/// CMV's inter syntax has only backward references to the previous two decoded pictures. It defines
/// no B-picture or forward-reference mode, so decode and presentation order are identical.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class EaCmvVideoEncoder : IVideoCodecEncoder<EaCmvVideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("cmv ");
  private static readonly byte[] _MotionSearchOrder = _BuildMotionSearchOrder();

  private const int _BLOCK = 4;
  private const int _PALETTE_ENTRIES = 256;
  private const int _PALETTE_BYTES = _PALETTE_ENTRIES * 3;
  private const int _CHUNK_HEADER_LENGTH = 8;
  private const int _PICTURE_TYPE_LENGTH = 2;
  private const int _FULL_HEADER_CHUNK_LENGTH = _CHUNK_HEADER_LENGTH + 0x10 + _PALETTE_BYTES;

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;
  private readonly ushort _frameRate;
  private readonly byte[] _previousPalette = new byte[_PALETTE_BYTES];

  private EaCmvFrame? _lastFrame;
  private EaCmvFrame? _secondLastFrame;
  private bool _headerWritten;
  private bool _finished;

  private EaCmvVideoEncoder(MediaStreamInfo stream, ushort frameRate) {
    this._requested = stream;
    this._width = stream.Width;
    this._height = stream.Height;
    this._frameRate = frameRate;
  }

  public static string CodecName => "Electronic Arts CMV";

  public static CodecTag Codec => _Tag;

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static EaCmvVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Electronic Arts CMV can only encode a video stream.");
    if (stream.Width is <= 0 or > ushort.MaxValue || stream.Height is <= 0 or > ushort.MaxValue)
      throw new NotSupportedException(
        $"Electronic Arts CMV stores width and height as unsigned 16-bit values; {stream.Width}x{stream.Height} was supplied.");

    var pixelCount = (long)stream.Width * stream.Height;
    var firstPacketLength = pixelCount + _FULL_HEADER_CHUNK_LENGTH + _CHUNK_HEADER_LENGTH + _PICTURE_TYPE_LENGTH;
    if (firstPacketLength > Array.MaxLength)
      throw new NotSupportedException(
        $"A {stream.Width}x{stream.Height} CMV key frame plus its complete state header cannot fit in one managed packet.");
    if (stream.BitsPerPixel is not (0 or 8))
      throw new NotSupportedException(
        $"Electronic Arts CMV is an eight-bit palettised codec; the requested stream states {stream.BitsPerPixel} bits per pixel.");

    return new(stream, _FrameRate(stream));
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    if (this._finished)
      throw new InvalidOperationException("Electronic Arts CMV encoder has already been flushed.");
    ArgumentNullException.ThrowIfNull(frame);

    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"Electronic Arts CMV encoder geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");
    if (frame.Format != PixelFormat.Indexed8)
      throw new NotSupportedException(
        $"Electronic Arts CMV codes palette indices and takes only Indexed8 pictures; {frame.Format} would have to be quantised first.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");
    if (frame.HasAlpha)
      throw new NotSupportedException("Electronic Arts CMV has no palette-alpha channel; an indexed picture with transparency cannot be encoded losslessly.");

    var palette = this._Palette(frame);
    var pixelCount = checked(this._width * this._height);
    var pixels = frame.PixelData.AsSpan(0, pixelCount);
    this._ValidateIndices(pixels, frame.PaletteCount);

    var current = new EaCmvFrame(this._width, this._height);
    pixels.CopyTo(current.Indices);

    var (picture, isKeyFrame) = this._Picture(current);
    var header = this._PaletteHeader(palette, forceComplete: isKeyFrame);
    var data = _Join(header, picture);

    palette.CopyTo(this._previousPalette, 0);
    this._headerWritten = true;
    this._secondLastFrame = this._lastFrame;
    this._lastFrame = current;

    packet = new(
      StreamIndex: this._requested.Index,
      Data: data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: isKeyFrame);
    return true;
  }

  public IEnumerable<CodedPacket> Flush() {
    if (this._finished)
      return [];

    this._finished = true;
    if (this._lastFrame == null)
      return [];

    return [new CodedPacket(StreamIndex: this._requested.Index, Data: _Chunk(EaChunks.MVIe, []))];
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
    BitsPerPixel = 8,
    Language = this._requested.Language,
    Name = this._requested.Name,
  };

  private static ushort _FrameRate(MediaStreamInfo stream) {
    if (stream.FrameRate.IsKnown)
      return _WholeRate(stream.FrameRate, "frame rate");

    if (!stream.TimeBase.IsKnown)
      return 0;

    if (stream.TimeBase.Numerator <= 0 || stream.TimeBase.Denominator <= 0
        || stream.TimeBase.Denominator % stream.TimeBase.Numerator != 0)
      throw new NotSupportedException(
        $"Electronic Arts CMV stores only a whole-number frame rate, but the requested time base {stream.TimeBase} does not invert to one.");

    var rate = stream.TimeBase.Denominator / stream.TimeBase.Numerator;
    if (rate is <= 0 or > ushort.MaxValue)
      throw new NotSupportedException($"Electronic Arts CMV's 16-bit frame-rate field cannot hold {rate} frames per second.");
    return (ushort)rate;
  }

  private static ushort _WholeRate(Rational rate, string field) {
    if (rate.Numerator <= 0 || rate.Denominator <= 0 || rate.Numerator % rate.Denominator != 0)
      throw new NotSupportedException(
        $"Electronic Arts CMV stores only a whole-number {field}, but {rate} was requested.");

    var value = rate.Numerator / rate.Denominator;
    if (value is <= 0 or > ushort.MaxValue)
      throw new NotSupportedException($"Electronic Arts CMV's 16-bit frame-rate field cannot hold {value} frames per second.");
    return (ushort)value;
  }

  private byte[] _Palette(RawImage frame) {
    if (frame.Palette == null || frame.PaletteCount <= 0)
      throw new InvalidDataException("An Indexed8 CMV picture needs the RGB palette its indices refer to.");
    if (frame.PaletteCount > _PALETTE_ENTRIES)
      throw new InvalidDataException($"A CMV palette holds at most {_PALETTE_ENTRIES} entries; the picture declares {frame.PaletteCount}.");

    var needed = checked(frame.PaletteCount * 3);
    if (frame.Palette.Length < needed)
      throw new InvalidDataException(
        $"The picture declares {frame.PaletteCount} palette entries but carries only {frame.Palette.Length / 3} RGB triplets.");

    var palette = new byte[_PALETTE_BYTES];
    frame.Palette.AsSpan(0, needed).CopyTo(palette);
    return palette;
  }

  private void _ValidateIndices(ReadOnlySpan<byte> pixels, int paletteCount) {
    for (var i = 0; i < pixels.Length; ++i)
      if (pixels[i] >= paletteCount)
        throw new InvalidDataException(
          $"Pixel {i % this._width},{i / this._width} is palette index {pixels[i]} while the picture declares only {paletteCount} entries.");
  }

  private byte[]? _PaletteHeader(ReadOnlySpan<byte> palette, bool forceComplete) {
    var first = 0;
    var last = _PALETTE_ENTRIES - 1;

    if (this._headerWritten && !forceComplete) {
      first = -1;
      last = -1;
      for (var entry = 0; entry < _PALETTE_ENTRIES; ++entry) {
        var offset = entry * 3;
        if (palette.Slice(offset, 3).SequenceEqual(this._previousPalette.AsSpan(offset, 3)))
          continue;

        first = first < 0 ? entry : first;
        last = entry;
      }

      if (first < 0)
        return null;
    }

    var count = last - first + 1;
    var payload = new byte[0x10 + count * 3];
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4), checked((ushort)this._width));
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6), checked((ushort)this._height));
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(10), this._frameRate);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(12), checked((ushort)first));
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(14), checked((ushort)count));
    palette.Slice(first * 3, count * 3).CopyTo(payload.AsSpan(0x10));
    return _Chunk(EaChunks.MVIh, payload);
  }

  private (byte[] Data, bool IsKeyFrame) _Picture(EaCmvFrame current) {
    var intra = this._Intra(current);
    if (this._lastFrame == null || this._width % _BLOCK != 0 || this._height % _BLOCK != 0 || !this._InterCanFit())
      return (intra, true);

    var inter = this._Inter(current);
    return inter.Length < intra.Length ? (inter, false) : (intra, true);
  }

  private bool _InterCanFit() {
    var blocks = (long)(this._width / _BLOCK) * (this._height / _BLOCK);
    var maximumChunkLength = _CHUNK_HEADER_LENGTH + _PICTURE_TYPE_LENGTH + 18L * blocks;
    return maximumChunkLength + _FULL_HEADER_CHUNK_LENGTH <= Array.MaxLength;
  }

  private static byte[] _Intra(EaCmvFrame current) {
    var payload = new byte[checked(_PICTURE_TYPE_LENGTH + current.Indices.Length)];
    current.Indices.CopyTo(payload, _PICTURE_TYPE_LENGTH);
    return _Chunk(EaChunks.MVIf, payload);
  }

  private byte[] _Inter(EaCmvFrame current) {
    var blocksWide = this._width / _BLOCK;
    var blocksHigh = this._height / _BLOCK;
    var blockCount = checked(blocksWide * blocksHigh);
    var primary = new byte[blockCount];
    using var escapes = new MemoryStream();

    for (var by = 0; by < blocksHigh; ++by) {
      for (var bx = 0; bx < blocksWide; ++bx) {
        var block = by * blocksWide + bx;
        if (_TryMotion(current, this._lastFrame!, bx, by, out var motion)) {
          primary[block] = motion;
          continue;
        }

        primary[block] = 0xFF;
        if (this._secondLastFrame != null && _TryMotion(current, this._secondLastFrame, bx, by, out motion)) {
          escapes.WriteByte(motion);
          continue;
        }

        escapes.WriteByte(0xFF);
        for (var yy = 0; yy < _BLOCK; ++yy)
          escapes.Write(current.Indices.AsSpan((by * _BLOCK + yy) * this._width + bx * _BLOCK, _BLOCK));
      }
    }

    var escapeBytes = escapes.ToArray();
    var payloadLength = checked(_PICTURE_TYPE_LENGTH + primary.Length + escapeBytes.Length);
    var payload = new byte[payloadLength];
    BinaryPrimitives.WriteUInt16LittleEndian(payload, 1);
    primary.CopyTo(payload, _PICTURE_TYPE_LENGTH);
    escapeBytes.CopyTo(payload, _PICTURE_TYPE_LENGTH + primary.Length);
    return _Chunk(EaChunks.MVIf, payload);
  }

  private static bool _TryMotion(EaCmvFrame current, EaCmvFrame source, int blockX, int blockY, out byte motion) {
    foreach (var candidate in _MotionSearchOrder) {
      var dx = (candidate & 0x0F) - 7;
      var dy = (candidate >> 4) - 7;
      if (!_BlockMatches(current, source, blockX, blockY, dx, dy))
        continue;

      motion = candidate;
      return true;
    }

    motion = 0;
    return false;
  }

  private static bool _BlockMatches(EaCmvFrame current, EaCmvFrame source, int blockX, int blockY, int dx, int dy) {
    for (var yy = 0; yy < _BLOCK; ++yy) {
      var y = blockY * _BLOCK + yy;
      var sy = y + dy;
      for (var xx = 0; xx < _BLOCK; ++xx) {
        var x = blockX * _BLOCK + xx;
        var sx = x + dx;
        var expected = sx >= 0 && sx < source.Width && sy >= 0 && sy < source.Height
          ? source.Indices[sy * source.Width + sx]
          : (byte)0;
        if (current.Indices[y * current.Width + x] != expected)
          return false;
      }
    }

    return true;
  }

  private static byte[] _BuildMotionSearchOrder() {
    var result = new byte[255];
    var at = 0;

    for (var distance = 0; distance <= 16; ++distance) {
      for (var dy = -7; dy <= 8; ++dy) {
        for (var dx = -7; dx <= 8; ++dx) {
          if (Math.Abs(dx) + Math.Abs(dy) != distance)
            continue;

          var value = (byte)(((dy + 7) << 4) | (dx + 7));
          if (value == 0xFF)
            continue;
          result[at++] = value;
        }
      }
    }

    if (at != result.Length)
      throw new InvalidOperationException($"CMV motion search generated {at} vectors instead of {result.Length}.");
    return result;
  }

  private static byte[] _Chunk(uint fourCc, ReadOnlySpan<byte> payload) {
    var length = (long)_CHUNK_HEADER_LENGTH + payload.Length;
    if (length > Array.MaxLength)
      throw new InvalidOperationException("An Electronic Arts CMV chunk is too large for one managed byte array.");

    var result = new byte[(int)length];
    BinaryPrimitives.WriteUInt32LittleEndian(result, fourCc);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), checked((uint)length));
    payload.CopyTo(result.AsSpan(_CHUNK_HEADER_LENGTH));
    return result;
  }

  private static byte[] _Join(byte[]? first, byte[] second) {
    if (first == null)
      return second;

    var length = (long)first.Length + second.Length;
    if (length > Array.MaxLength)
      throw new InvalidOperationException("An Electronic Arts CMV packet is too large for one managed byte array.");

    var result = new byte[(int)length];
    first.CopyTo(result, 0);
    second.CopyTo(result, first.Length);
    return result;
  }
}
