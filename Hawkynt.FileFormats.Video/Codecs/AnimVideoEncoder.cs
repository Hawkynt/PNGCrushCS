using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes IFF ANIM video as ILBM key pictures and Jim Kent byte-vertical delta frames (ANIM method 5).
/// </summary>
/// <remarks>
/// ANIM is a backwards-predicted format, not a codec with MPEG-style B pictures: method 5 normally
/// modifies the bitmap from two displayed pictures ago. The encoder therefore keeps the two hardware
/// buffers the original format assumes, seeds both from the first ILBM picture, and alternates the
/// target for every following frame. A later direct picture replaces only the buffer it is displayed
/// from; it deliberately does not reset both references.
/// <para/>
/// The coded source is indexed colour. That is the native ILBM representation and keeps every source
/// sample and palette entry exact. RGB input is refused rather than quantised behind the caller's back.
/// The first frame fixes the bitplane count for the stream; later palettes may change, but cannot require
/// more indices than that depth can represent.
/// <para/>
/// The first frame and direct refresh pictures use an uncompressed ILBM BODY. Predicted pictures use
/// method 5's per-plane, per-byte-column skip/unique/repeat instruction stream and the complete sixteen
/// longword pointer table specified for that method. If a delta would be larger than a direct picture,
/// or a pathological column would need more than 255 operations, a direct picture is emitted instead.
/// </remarks>
public sealed class AnimVideoEncoder : IVideoCodecEncoder<AnimVideoEncoder> {

  private const byte _OP_DIRECT = 0;
  private const byte _OP_BYTE_VERTICAL_DELTA = 5;
  private static readonly CodecTag _Tag = CodecTag.FromCharacters("ANIM");
  private static readonly Rational _TimeBase = new(1, 60);

  private readonly MediaStreamInfo _stream;
  private readonly Rational _sourceTimeBase;
  private readonly byte[]?[] _buffers = new byte[2][];

  private int _planes;
  private int _bytesPerRow;
  private int _planeSize;
  private int _current;
  private long _frameNumber;
  private long? _lastOutputTimestamp;
  private byte[] _palette = [];

  private AnimVideoEncoder(MediaStreamInfo stream) {
    this._sourceTimeBase = stream.TimeBase;
    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      TimeBase = _TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = stream.BitsPerPixel,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "IFF ANIM Video";

  public static CodecTag Codec => _Tag;

  public static AnimVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video || !stream.Codec.EqualsIgnoringCase(_Tag))
      throw new NotSupportedException("IFF ANIM encodes only an ANIM video stream.");
    if (stream.Width is <= 0 or > ushort.MaxValue || stream.Height is <= 0 or > ushort.MaxValue)
      throw new NotSupportedException(
        $"IFF ANIM requires a positive picture size that fits its 16-bit BMHD fields; received {stream.Width}x{stream.Height}.");

    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException(
        $"IFF ANIM geometry is fixed at {this._stream.Width}x{this._stream.Height}; received {frame.Width}x{frame.Height}.");
    if (frame.Format != PixelFormat.Indexed8)
      throw new NotSupportedException(
        $"IFF ANIM encoding currently preserves indexed ILBM pictures exactly; received {frame.Format}. Quantise explicitly before encoding.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared dimensions.");
    if (frame.HasAlpha)
      throw new NotSupportedException("IFF ANIM masking/transparency encoding is not implemented; an indexed frame with alpha cannot be written losslessly.");
    if (frame.Palette is null || frame.PaletteCount is <= 0 or > 256 || frame.Palette.Length < frame.PaletteCount * 3)
      throw new InvalidDataException("IFF ANIM indexed input needs between one and 256 complete RGB palette entries.");

    var pixelCount = checked(frame.Width * frame.Height);
    var pixels = frame.PixelData.AsSpan(0, pixelCount);
    var highestIndex = 0;
    foreach (var index in pixels) {
      if (index >= frame.PaletteCount)
        throw new InvalidDataException(
          $"IFF ANIM pixel index {index} is outside the frame's {frame.PaletteCount}-entry palette.");
      highestIndex = Math.Max(highestIndex, index);
    }

    var requiredPlanes = _BitsNeeded(Math.Max(highestIndex, frame.PaletteCount - 1));
    if (this._planes == 0) {
      this._planes = requiredPlanes;
      this._bytesPerRow = checked((frame.Width + 15) / 16 * 2);
      this._planeSize = checked(this._bytesPerRow * frame.Height);
    } else if (requiredPlanes > this._planes)
      throw new InvalidDataException(
        $"IFF ANIM's first picture fixed the stream at {this._planes} bitplane(s), but this frame needs {requiredPlanes}.");

    var planeMajor = _ChunkyToPlaneMajor(pixels, frame.Width, frame.Height, this._planes, this._bytesPerRow);
    var palette = frame.Palette.AsSpan(0, frame.PaletteCount * 3).ToArray();
    var paletteChanged = !palette.AsSpan().SequenceEqual(this._palette);
    var outputTimestamp = this._ConvertTimestamp(presentationTimestamp);
    var relativeTime = this._RelativeTime(outputTimestamp);

    byte[] coded;
    var isKeyFrame = this._buffers[0] is null;
    if (isKeyFrame) {
      coded = this._BuildDirectFrame(planeMajor, palette, relativeTime, includeAnimationHeader: false);
      this._buffers[0] = (byte[])planeMajor.Clone();
      this._buffers[1] = (byte[])planeMajor.Clone();
      this._current = 0;
    } else {
      var target = 1 - this._current;
      var reference = this._buffers[target] ?? throw new InvalidOperationException("IFF ANIM reference state is incomplete.");
      var delta = _EncodeByteVerticalDelta(reference, planeMajor, this._planes, this._bytesPerRow, this._planeSize);
      var direct = this._BuildDirectFrame(planeMajor, palette, relativeTime, includeAnimationHeader: true);
      var predicted = delta is null ? null : this._BuildDeltaFrame(delta, paletteChanged ? palette : null, relativeTime);

      if (predicted is null || predicted.Length >= direct.Length) {
        coded = direct;
        isKeyFrame = true;
      } else
        coded = predicted;

      this._buffers[target] = planeMajor;
      this._current = target;
    }

    this._palette = palette;
    this._lastOutputTimestamp = outputTimestamp;
    ++this._frameNumber;

    packet = new(
      this._stream.Index,
      coded,
      PresentationTimestamp: outputTimestamp,
      DecodeTimestamp: outputTimestamp,
      Duration: relativeTime,
      IsKeyFrame: isKeyFrame);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  private long _ConvertTimestamp(long? timestamp) {
    if (timestamp is long value) {
      if (!this._sourceTimeBase.IsKnown)
        return value;

      var numerator = (Int128)value * this._sourceTimeBase.Numerator * 60;
      var denominator = this._sourceTimeBase.Denominator;
      if (denominator <= 0)
        return value;

      var rounded = numerator >= 0
        ? (numerator + denominator / 2) / denominator
        : (numerator - denominator / 2) / denominator;
      return checked((long)rounded);
    }

    if (this._stream.FrameRate.IsKnown) {
      var numerator = (Int128)this._frameNumber * 60 * this._stream.FrameRate.Denominator;
      return checked((long)(numerator / this._stream.FrameRate.Numerator));
    }

    return this._frameNumber;
  }

  private uint _RelativeTime(long outputTimestamp) {
    if (this._lastOutputTimestamp is not long previous)
      return 1;

    var difference = outputTimestamp - previous;
    if (difference <= 0)
      return 1;

    return difference >= uint.MaxValue ? uint.MaxValue : (uint)difference;
  }

  private byte[] _BuildDirectFrame(byte[] planeMajor, byte[] palette, uint relativeTime, bool includeAnimationHeader) {
    var body = _PlaneMajorToInterleaved(planeMajor, this._planes, this._bytesPerRow, this._stream.Height);
    var chunks = new List<(string Id, byte[] Data)>(includeAnimationHeader ? 4 : 3) {
      ("BMHD", this._BuildBitmapHeader()),
    };
    if (includeAnimationHeader)
      chunks.Add(("ANHD", this._BuildAnimationHeader(_OP_DIRECT, relativeTime, 0, 0)));
    chunks.Add(("CMAP", palette));
    chunks.Add(("BODY", body));
    return _BuildForm(chunks);
  }

  private byte[] _BuildDeltaFrame(byte[] delta, byte[]? palette, uint relativeTime) {
    var chunks = new List<(string Id, byte[] Data)>(palette is null ? 2 : 3) {
      ("ANHD", this._BuildAnimationHeader(_OP_BYTE_VERTICAL_DELTA, relativeTime, 0, 0)),
    };
    if (palette is not null)
      chunks.Add(("CMAP", palette));
    chunks.Add(("DLTA", delta));
    return _BuildForm(chunks);
  }

  private byte[] _BuildBitmapHeader() {
    var result = new byte[20];
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(0, 2), checked((ushort)this._stream.Width));
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(2, 2), checked((ushort)this._stream.Height));
    result[8] = checked((byte)this._planes);
    result[9] = 0; // no mask plane
    result[10] = 0; // uncompressed BODY
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(16, 2), checked((ushort)this._stream.Width));
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(18, 2), checked((ushort)this._stream.Height));
    return result;
  }

  private byte[] _BuildAnimationHeader(byte operation, uint relativeTime, byte interleave, uint bits) {
    var result = new byte[40];
    result[0] = operation;
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(2, 2), checked((ushort)this._stream.Width));
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(4, 2), checked((ushort)this._stream.Height));
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(14, 4), relativeTime);
    result[18] = interleave;
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(20, 4), bits);
    return result;
  }

  private static byte[]? _EncodeByteVerticalDelta(
    ReadOnlySpan<byte> reference,
    ReadOnlySpan<byte> current,
    int planes,
    int bytesPerRow,
    int planeSize) {
    var table = new byte[64]; // sixteen 32-bit pointers; the second eight are reserved by method 5.
    using var payload = new MemoryStream();

    for (var plane = 0; plane < planes; ++plane) {
      var planeOffset = plane * planeSize;
      if (reference.Slice(planeOffset, planeSize).SequenceEqual(current.Slice(planeOffset, planeSize)))
        continue;

      var pointer = checked(64L + payload.Length);
      if (pointer > uint.MaxValue)
        return null;
      BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(plane * 4, 4), (uint)pointer);

      for (var column = 0; column < bytesPerRow; ++column) {
        var encoded = _EncodeColumn(reference, current, planeOffset, column, bytesPerRow, planeSize / bytesPerRow);
        if (encoded is null)
          return null;
        payload.Write(encoded);
      }
    }

    var result = new byte[checked(64 + (int)payload.Length)];
    table.CopyTo(result, 0);
    payload.Position = 0;
    _ = payload.Read(result, 64, (int)payload.Length);
    return result;
  }

  private static byte[]? _EncodeColumn(
    ReadOnlySpan<byte> reference,
    ReadOnlySpan<byte> current,
    int planeOffset,
    int column,
    int stride,
    int height) {
    var lastChanged = -1;
    for (var row = height - 1; row >= 0; --row)
      if (_At(reference, planeOffset, column, stride, row) != _At(current, planeOffset, column, stride, row)) {
        lastChanged = row;
        break;
      }

    if (lastChanged < 0)
      return [0];

    var operations = new List<byte>();
    var operationCount = 0;
    var y = 0;
    while (y <= lastChanged) {
      if (_At(reference, planeOffset, column, stride, y) == _At(current, planeOffset, column, stride, y)) {
        var count = 1;
        while (count < 127 && y + count <= lastChanged
               && _At(reference, planeOffset, column, stride, y + count) == _At(current, planeOffset, column, stride, y + count))
          ++count;

        operations.Add((byte)count);
        y += count;
      } else {
        var same = _ChangedSameRun(reference, current, planeOffset, column, stride, y, lastChanged);
        if (same >= 3) {
          var count = Math.Min(same, 255);
          operations.Add(0);
          operations.Add((byte)count);
          operations.Add(_At(current, planeOffset, column, stride, y));
          y += count;
        } else {
          var start = y;
          var count = 0;
          while (count < 127 && y <= lastChanged) {
            if (_At(reference, planeOffset, column, stride, y) == _At(current, planeOffset, column, stride, y))
              break;
            if (count > 0 && _ChangedSameRun(reference, current, planeOffset, column, stride, y, lastChanged) >= 3)
              break;
            ++count;
            ++y;
          }

          operations.Add((byte)(0x80 | count));
          for (var i = 0; i < count; ++i)
            operations.Add(_At(current, planeOffset, column, stride, start + i));
        }
      }

      if (++operationCount > byte.MaxValue)
        return null;
    }

    var result = new byte[operations.Count + 1];
    result[0] = (byte)operationCount;
    operations.CopyTo(result, 1);
    return result;
  }

  private static int _ChangedSameRun(
    ReadOnlySpan<byte> reference,
    ReadOnlySpan<byte> current,
    int planeOffset,
    int column,
    int stride,
    int row,
    int lastChanged) {
    var value = _At(current, planeOffset, column, stride, row);
    var count = 0;
    while (row + count <= lastChanged
           && _At(reference, planeOffset, column, stride, row + count) != _At(current, planeOffset, column, stride, row + count)
           && _At(current, planeOffset, column, stride, row + count) == value)
      ++count;
    return count;
  }

  private static byte _At(ReadOnlySpan<byte> data, int planeOffset, int column, int stride, int row)
    => data[planeOffset + row * stride + column];

  private static byte[] _ChunkyToPlaneMajor(ReadOnlySpan<byte> indices, int width, int height, int planes, int bytesPerRow) {
    var planeSize = checked(bytesPerRow * height);
    var result = new byte[checked(planeSize * planes)];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var value = indices[y * width + x];
        var byteOffset = y * bytesPerRow + (x >> 3);
        var mask = (byte)(1 << (7 - (x & 7)));
        for (var plane = 0; plane < planes; ++plane)
          if ((value & (1 << plane)) != 0)
            result[plane * planeSize + byteOffset] |= mask;
      }

    return result;
  }

  private static byte[] _PlaneMajorToInterleaved(byte[] planeMajor, int planes, int bytesPerRow, int height) {
    var planeSize = checked(bytesPerRow * height);
    var result = new byte[checked(planeSize * planes)];
    var scanlineBytes = checked(bytesPerRow * planes);

    for (var y = 0; y < height; ++y)
      for (var plane = 0; plane < planes; ++plane)
        Array.Copy(
          planeMajor,
          plane * planeSize + y * bytesPerRow,
          result,
          y * scanlineBytes + plane * bytesPerRow,
          bytesPerRow);

    return result;
  }

  private static byte[] _BuildForm(IReadOnlyList<(string Id, byte[] Data)> chunks) {
    using var output = new MemoryStream();
    output.Write("FORM"u8);
    output.Write(new byte[4]);
    output.Write("ILBM"u8);

    foreach (var (id, data) in chunks) {
      if (id.Length != 4)
        throw new InvalidOperationException("IFF chunk identifiers are exactly four characters.");
      for (var i = 0; i < 4; ++i)
        output.WriteByte((byte)id[i]);
      Span<byte> size = stackalloc byte[4];
      BinaryPrimitives.WriteUInt32BigEndian(size, checked((uint)data.Length));
      output.Write(size);
      output.Write(data);
      if ((data.Length & 1) != 0)
        output.WriteByte(0);
    }

    var result = output.ToArray();
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4, 4), checked((uint)(result.Length - 8)));
    return result;
  }

  private static int _BitsNeeded(int maximumValue) {
    var bits = 1;
    while ((1 << bits) <= maximumValue && bits < 8)
      ++bits;
    return bits;
  }
}
