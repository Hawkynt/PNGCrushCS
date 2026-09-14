using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.MagicYuv;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes MagicYUV v7 losslessly in its native sample domain, including 8/10/12/14-bit formats.
/// </summary>
/// <remarks>
/// The entropy construction is adapted from FFmpeg's <c>libavcodec/magicyuvenc.c</c>, copyright
/// (c) 2017 Paul B Mahol, LGPL-2.1-or-later, and is distributed here under LGPL-3.0-or-later.
/// Higher-depth packing and interlaced field-stride behavior are cross-checked against FFmpeg's
/// decoder and OxideAV's MIT-licensed clean-room implementation.
/// <para/>
/// MagicYUV is intra-only. Every packet produced here is a key frame; the codec has no P/B picture
/// syntax and therefore no temporal forward/backward references.
/// </remarks>
public sealed class MagicYuvEncoder : IVideoCodecEncoder<MagicYuvEncoder> {
  /// <summary>The three spatial predictors a slice may use.</summary>
  public enum Predictor {
    Left = 1,
    Gradient = 2,
    Median = 3,
  }

  private const byte _CODED = 0;
  private const byte _UNCOMPRESSED = 1;
  private const byte _CODER_TYPE = 0x20;
  private const uint _INTERLACED = 0x0000_0002;
  private const uint _DEFAULT_FLAGS = 0x0020_0000;

  private static readonly CodecTag _DefaultTag = CodecTag.FromCharacters("M8RG");

  private readonly MediaStreamInfo _stream;
  private readonly MagicYuvFormat _format;
  private readonly PixelFormat _pixelFormat;
  private readonly MagicYuvPredictor _predictor;
  private readonly int _sliceHeight;
  private readonly int _sliceCount;
  private readonly bool _interlaced;

  private MagicYuvEncoder(
    MediaStreamInfo stream,
    CodecTag tag,
    MagicYuvFormat format,
    PixelFormat pixelFormat,
    Predictor predictor,
    int slices,
    bool interlaced
  ) {
    this._format = format;
    this._pixelFormat = pixelFormat;
    this._predictor = (MagicYuvPredictor)predictor;
    this._interlaced = interlaced;

    var verticalShift = format.ChromaVerticalShift;
    var align = 1 << verticalShift;
    var minimumRows = (interlaced ? 2 : 1) * align;
    if (interlaced && stream.Height < minimumRows)
      throw new NotSupportedException(
        $"An interlaced {tag} picture needs at least {minimumRows} luminance rows so each field has a row; {stream.Height} were stated.");

    var mostByRows = Math.Max(1, stream.Height / minimumRows);
    var most = Math.Min(mostByRows, 256 / format.PlaneCount);
    var wanted = Math.Min(slices, most);
    var sliceHeight = (stream.Height + wanted - 1) / wanted;
    sliceHeight = Math.Max(minimumRows, (sliceHeight + align - 1) & ~(align - 1));

    if (interlaced) {
      while (sliceHeight < stream.Height) {
        var remainder = stream.Height % sliceHeight;
        if (remainder == 0 || remainder >= minimumRows)
          break;
        sliceHeight += align;
      }
    }

    this._sliceHeight = sliceHeight;
    this._sliceCount = (stream.Height + sliceHeight - 1) / sliceHeight;

    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = tag,
      Handler = tag,
      CodecId = "magicyuv",
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = RawImage.BitsPerPixel(pixelFormat),
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "MagicYUV";
  public static CodecTag Codec => _DefaultTag;

  /// <summary>Builds a progressive encoder using median prediction and one slice.</summary>
  public static MagicYuvEncoder Create(MediaStreamInfo stream) => Create(stream, Predictor.Median, 1, false);

  /// <summary>Builds a progressive encoder with the requested predictor and slice count.</summary>
  public static MagicYuvEncoder Create(MediaStreamInfo stream, Predictor predictor, int slices)
    => Create(stream, predictor, slices, false);

  /// <summary>Builds an encoder, optionally using MagicYUV's interlaced field-stride prediction.</summary>
  public static MagicYuvEncoder Create(MediaStreamInfo stream, Predictor predictor, int slices, bool interlaced) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("MagicYUV can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"A MagicYUV encoder needs the picture size before the first frame; {stream.Width}x{stream.Height} was stated.");
    if (predictor is not (Predictor.Left or Predictor.Gradient or Predictor.Median))
      throw new ArgumentOutOfRangeException(nameof(predictor), predictor, "MagicYUV has exactly three predictors.");
    if (slices < 1)
      throw new ArgumentOutOfRangeException(nameof(slices), slices, "A frame has at least one slice.");

    var name = stream.Codec.ToString();
    string selected = name switch {
      "M8RG" or "M8RA" or "M8Y4" or "M8Y2" or "M8Y0" or "M8YA" or "M8G0"
        or "M0RG" or "M0RA" or "M0Y0" or "M0Y2" or "M0Y4" or "M0G0"
        or "M2RG" or "M2RA" or "M4RG" or "M4RA" => name,
      "M8GA" => throw new NotSupportedException(
        $"Video stream {stream.Index} asks for M8GA — grey with alpha — which is not one of MagicYUV v7's native format codes."),
      "MAGY" => throw new NotSupportedException(
        $"Video stream {stream.Index} asks for MAGY, the single code MagicYUV used before it gave each pixel format one of its own. Which layout such a frame would hold is not in the code, so it is refused rather than guessed at."),
      _ => stream.BitsPerPixel switch {
        8 => "M8G0",
        32 => "M8RA",
        _ => "M8RG",
      },
    };

    var tag = CodecTag.FromCharacters(selected);
    var format = MagicYuvFormat.Of(tag, stream.Index);
    var pixelFormat = format.NativePixelFormat;
    return new(stream, tag, format, pixelFormat, predictor, slices, interlaced);
  }

  /// <summary>Encodes one complete key frame.</summary>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException(
        $"The encoder was created for {this._stream.Width}x{this._stream.Height} pictures and was handed one of {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        $"A {frame.Width}x{frame.Height} {frame.Format} picture needs at least {frame.MinimumPixelDataLength} bytes and carries {frame.PixelData.Length}.");

    var data = this._Encode(this._Planes(frame));
    packet = new(
      StreamIndex: this._stream.Index,
      Data: data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: true);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  // ============================================================================================
  // Source picture -> native wire planes
  // ============================================================================================

  private ushort[][] _Planes(RawImage picture) {
    var format = this._format;
    if (format.ColourSpace == MagicYuvColourSpace.Yuv && format.HasAlpha)
      return this._YuvaPlanes(picture);

    var converted = picture.Format == this._pixelFormat
      ? picture
      : this._pixelFormat is PixelFormat.Rgb48 or PixelFormat.Rgba64
        ? PixelConverter.Convert(picture, this._pixelFormat)
        : FastRawImageConverter.Convert(picture, this._pixelFormat);

    if (!converted.HasEnoughPixelData)
      throw new InvalidDataException(
        $"Conversion to {this._pixelFormat} produced {converted.PixelData.Length} bytes where a {picture.Width}x{picture.Height} picture needs {converted.MinimumPixelDataLength}.");

    return format.ColourSpace switch {
      MagicYuvColourSpace.Grey => [this._ReadPlane(converted, 0)],
      MagicYuvColourSpace.Rgb => this._RgbPlanes(converted),
      _ => this._YuvPlanes(converted),
    };
  }

  private ushort[][] _RgbPlanes(RawImage picture) {
    var format = this._format;
    var count = picture.Width * picture.Height;
    var channels = format.HasAlpha ? 4 : 3;
    var blue = new ushort[count];
    var green = new ushort[count];
    var red = new ushort[count];
    var alpha = format.HasAlpha ? new ushort[count] : null;
    var pixels = picture.PixelData.AsSpan();
    var mask = format.SampleMask;

    if (format.BitDepth == 8) {
      for (var i = 0; i < count; ++i) {
        var at = i * channels;
        var g = pixels[at + 1];
        green[i] = g;
        blue[i] = (ushort)((pixels[at + 2] - g) & 0xFF);
        red[i] = (ushort)((pixels[at] - g) & 0xFF);
        if (alpha is not null)
          alpha[i] = pixels[at + 3];
      }
    } else {
      for (var i = 0; i < count; ++i) {
        var at = i * channels * 2;
        var r = _Reduce(BinaryPrimitives.ReadUInt16LittleEndian(pixels.Slice(at, 2)), mask);
        var g = _Reduce(BinaryPrimitives.ReadUInt16LittleEndian(pixels.Slice(at + 2, 2)), mask);
        var b = _Reduce(BinaryPrimitives.ReadUInt16LittleEndian(pixels.Slice(at + 4, 2)), mask);
        red[i] = (ushort)((r - g) & mask);
        green[i] = g;
        blue[i] = (ushort)((b - g) & mask);
        if (alpha is not null)
          alpha[i] = _Reduce(BinaryPrimitives.ReadUInt16LittleEndian(pixels.Slice(at + 6, 2)), mask);
      }
    }

    return alpha is null ? [blue, green, red] : [blue, green, red, alpha];
  }

  private ushort[][] _YuvPlanes(RawImage picture) {
    var planes = new ushort[this._format.PlaneCount][];
    for (var plane = 0; plane < planes.Length; ++plane)
      planes[plane] = this._ReadPlane(picture, plane);
    return planes;
  }

  private ushort[][] _YuvaPlanes(RawImage picture) {
    var rgba = picture.Format == PixelFormat.Rgba32 ? picture : PixelConverter.Convert(picture, PixelFormat.Rgba32);
    var yuv = FastRawImageConverter.Convert(rgba, PixelFormat.Yuv444P8);
    var count = picture.Width * picture.Height;
    var alpha = new ushort[count];
    for (var i = 0; i < count; ++i)
      alpha[i] = rgba.PixelData[i * 4 + 3];

    return [this._ReadPlane(yuv, 0), this._ReadPlane(yuv, 1), this._ReadPlane(yuv, 2), alpha];
  }

  private ushort[] _ReadPlane(RawImage picture, int plane) {
    var (expectedWidth, expectedHeight) = this._format.PlaneSize(plane, picture.Width, picture.Height);
    var (actualWidth, actualHeight) = picture.GetPlaneDimensions(plane);
    if (expectedWidth != actualWidth || expectedHeight != actualHeight)
      throw new InvalidDataException(
        $"Plane {plane} of a {picture.Width}x{picture.Height} {picture.Format} picture is {actualWidth}x{actualHeight} where the codec's is {expectedWidth}x{expectedHeight}.");

    var data = picture.GetPlaneData(plane);
    var count = checked(expectedWidth * expectedHeight);
    var samples = new ushort[count];
    if (this._format.BitDepth == 8) {
      for (var i = 0; i < count; ++i)
        samples[i] = data[i];
    } else {
      for (var i = 0; i < count; ++i)
        samples[i] = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(i * 2, 2)) & this._format.SampleMask);
    }
    return samples;
  }

  private static ushort _Reduce(ushort value, int mask)
    => (ushort)((value * (long)mask + 32767) / 65535);

  // ============================================================================================
  // Frame
  // ============================================================================================

  private byte[] _Encode(ushort[][] planes) {
    var format = this._format;
    var width = this._stream.Width;
    var height = this._stream.Height;
    var planeCount = format.PlaneCount;
    var slices = this._sliceCount;
    var pieceCount = planeCount * slices;
    var symbolCount = format.SymbolCount;

    var residuals = new ushort[pieceCount][];
    var counts = new long[planeCount][];
    for (var plane = 0; plane < planeCount; ++plane) {
      counts[plane] = new long[symbolCount];
      var (planeWidth, planeHeight) = format.PlaneSize(plane, width, height);
      var planeSliceHeight = format.SliceHeight(plane, this._sliceHeight);
      for (var slice = 0; slice < slices; ++slice) {
        var firstRow = Math.Min(slice * planeSliceHeight, planeHeight);
        var lastRow = Math.Min(firstRow + planeSliceHeight, planeHeight);
        var residual = _Predict(
          planes[plane], planeWidth, firstRow, lastRow, this._predictor,
          format.BitDepth, format.SampleMask, this._interlaced);
        residuals[slice * planeCount + plane] = residual;
        foreach (var value in residual)
          ++counts[plane][value];
      }
    }

    var lengths = new byte[planeCount][];
    var codes = new uint[planeCount][];
    var descriptors = new byte[planeCount][];
    var descriptorBytes = 0;
    for (var plane = 0; plane < planeCount; ++plane) {
      lengths[plane] = MagicYuvCodeLengths.Choose(counts[plane], format.MaxHuffmanLength);
      codes[plane] = MagicYuvCodeLengths.Codes(lengths[plane]);
      descriptors[plane] = _Describe(lengths[plane], format.BitDepth == 8);
      descriptorBytes = checked(descriptorBytes + descriptors[plane].Length);
    }

    var tablesEnd = checked(
      MagicYuvFormat.HEADER_SIZE + 4 * (pieceCount + 1) + 1 + pieceCount + descriptorBytes);
    var positions = new int[pieceCount];
    var sizes = new int[pieceCount];
    var raw = new bool[pieceCount];
    var total = tablesEnd;

    for (var slice = 0; slice < slices; ++slice)
      for (var plane = 0; plane < planeCount; ++plane) {
        var piece = slice * planeCount + plane;
        var residual = residuals[piece];
        var planeLengths = lengths[plane];
        var codedBits = 0L;
        foreach (var value in residual)
          codedBits += planeLengths[value];

        var codedBytes = checked((int)((codedBits + 7) >> 3));
        var rawBytes = checked((residual.Length * format.BitDepth + 7) >> 3);
        raw[piece] = codedBytes >= rawBytes;
        var payload = raw[piece] ? rawBytes : codedBytes;
        sizes[piece] = checked((2 + payload + 3) & ~3);
        positions[piece] = total;
        total = checked(total + sizes[piece]);
      }

    var frame = new byte[total];
    var at = 0;
    MagicYuvFormat.Signature.CopyTo(frame, 0);
    at += 4;
    _WriteUInt32(frame, ref at, MagicYuvFormat.HEADER_SIZE);
    frame[at++] = MagicYuvFormat.VERSION_BYTE;
    frame[at++] = format.FormatByte;
    frame[at++] = (byte)format.MaxHuffmanLength;
    frame[at++] = 0;
    _WriteUInt32(frame, ref at, _DEFAULT_FLAGS | (this._interlaced ? _INTERLACED : 0));
    _WriteUInt32(frame, ref at, (uint)width);
    _WriteUInt32(frame, ref at, (uint)height);
    _WriteUInt32(frame, ref at, (uint)width);
    _WriteUInt32(frame, ref at, (uint)this._sliceHeight);

    _WriteUInt32(frame, ref at, (uint)(tablesEnd - MagicYuvFormat.HEADER_SIZE));
    for (var plane = 0; plane < planeCount; ++plane)
      for (var slice = 0; slice < slices; ++slice)
        _WriteUInt32(frame, ref at, (uint)(positions[slice * planeCount + plane] - MagicYuvFormat.HEADER_SIZE));

    frame[at++] = (byte)planeCount;
    for (var plane = 0; plane < planeCount; ++plane)
      for (var slice = 0; slice < slices; ++slice)
        frame[at++] = (byte)(slice * planeCount + plane);

    for (var plane = 0; plane < planeCount; ++plane) {
      descriptors[plane].CopyTo(frame, at);
      at += descriptors[plane].Length;
    }

    if (at != tablesEnd)
      throw new InvalidOperationException($"The tables end at byte {at} where the offsets say {tablesEnd}.");

    for (var piece = 0; piece < pieceCount; ++piece) {
      var start = positions[piece];
      var residual = residuals[piece];
      frame[start] = raw[piece] ? _UNCOMPRESSED : _CODED;
      frame[start + 1] = (byte)this._predictor;
      var bits = new _BitWriter(frame, start + 2);
      if (raw[piece]) {
        foreach (var value in residual)
          bits.Write(value, format.BitDepth);
      } else {
        var planeCodes = codes[piece % planeCount];
        var planeLengths = lengths[piece % planeCount];
        foreach (var value in residual)
          bits.Write(planeCodes[value], planeLengths[value]);
      }
      bits.Flush();
    }

    return frame;
  }

  /// <summary>Writes length descriptors, preserving the old literal 256-byte form at eight bits.</summary>
  private static byte[] _Describe(byte[] lengths, bool literal) {
    if (literal)
      return lengths.AsSpan().ToArray();

    var descriptor = new List<byte>();
    for (var at = 0; at < lengths.Length;) {
      var length = lengths[at];
      var run = 1;
      while (run < 256 && at + run < lengths.Length && lengths[at + run] == length)
        ++run;

      if (run > 1) {
        descriptor.Add((byte)(0x80 | length));
        descriptor.Add((byte)(run - 1));
      } else
        descriptor.Add(length);

      at += run;
    }
    return descriptor.ToArray();
  }

  private static ushort[] _Predict(
    ushort[] plane,
    int width,
    int firstRow,
    int lastRow,
    MagicYuvPredictor predictor,
    int bitDepth,
    int mask,
    bool interlaced
  ) {
    var residual = new ushort[(lastRow - firstRow) * width];
    var to = 0;
    var fieldStride = interlaced ? 2 : 1;
    for (var y = firstRow; y < lastRow; ++y) {
      var row = y * width;
      var hasTop = y - firstRow >= fieldStride;
      for (var x = 0; x < width; ++x) {
        var at = row + x;
        int predicted;
        if (!hasTop)
          predicted = x == 0 ? 0 : plane[at - 1];
        else if (x == 0)
          predicted = plane[at - fieldStride * width];
        else {
          var left = (int)plane[at - 1];
          var above = (int)plane[at - fieldStride * width];
          var aboveLeft = (int)plane[at - fieldStride * width - 1];
          if (predictor == MagicYuvPredictor.Left)
            predicted = left;
          else if (predictor == MagicYuvPredictor.Gradient)
            predicted = bitDepth == 8
              ? (byte)(left + above - aboveLeft)
              : left + above - aboveLeft;
          else if (bitDepth == 8) {
            var gradient = (byte)(left + above - aboveLeft);
            predicted = _Median8((byte)left, (byte)above, gradient);
          } else
            predicted = _MedianDeep(left, above, aboveLeft);
        }

        residual[to++] = (ushort)((plane[at] - predicted) & mask);
      }
    }
    return residual;
  }

  private static byte _Median8(byte a, byte b, byte c) {
    if (a > b)
      (a, b) = (b, a);
    return c < a ? a : c > b ? b : c;
  }

  private static int _MedianDeep(int left, int above, int aboveLeft) {
    var low = Math.Min(left, above);
    var high = Math.Max(left, above);
    if (aboveLeft >= high)
      return low;
    if (aboveLeft <= low)
      return high;
    return left + above - aboveLeft;
  }

  private static void _WriteUInt32(byte[] target, ref int at, uint value) {
    BinaryPrimitives.WriteUInt32LittleEndian(target.AsSpan(at, 4), value);
    at += 4;
  }

  /// <summary>Writes slice bits most-significant first.</summary>
  private struct _BitWriter(byte[] target, int at) {
    private ulong _held;
    private int _heldBits;

    internal void Write(uint code, int length) {
      this._held = (this._held << length) | code;
      this._heldBits += length;
      while (this._heldBits >= 8) {
        this._heldBits -= 8;
        target[at++] = (byte)(this._held >> this._heldBits);
      }
    }

    internal void Write(ushort value, int length) => this.Write(value, length);

    internal void Flush() {
      if (this._heldBits == 0)
        return;
      target[at++] = (byte)(this._held << (8 - this._heldBits));
      this._heldBits = 0;
    }
  }
}
