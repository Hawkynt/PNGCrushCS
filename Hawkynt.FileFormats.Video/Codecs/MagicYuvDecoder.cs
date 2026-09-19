using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Codecs.MagicYuv;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes MagicYUV v7, a lossless intra-only capture codec with independent horizontal slices.
/// </summary>
/// <remarks>
/// The format has no temporal prediction: every coded packet is a complete key frame, so there are
/// no P/B pictures or forward/backward frame references to retain. Prediction is strictly spatial
/// inside one plane slice.
/// <para/>
/// The original eight-bit implementation was measured against 309 streams and 1,446 frames. The
/// higher-depth and interlaced paths implemented here are cross-checked against FFmpeg's
/// LGPL-2.1-or-later decoder and OxideAV's MIT-licensed clean-room implementation. MagicYUV's own
/// public documentation supplies the native FourCC/pixel-format set, but not the bitstream details.
/// </remarks>
public sealed class MagicYuvDecoder : IVideoCodecDecoder<MagicYuvDecoder> {
  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("M8RG"), CodecTag.FromCharacters("M8RA"),
    CodecTag.FromCharacters("M8Y0"), CodecTag.FromCharacters("M8Y2"),
    CodecTag.FromCharacters("M8Y4"), CodecTag.FromCharacters("M8YA"),
    CodecTag.FromCharacters("M8G0"), CodecTag.FromCharacters("M8GA"),
    CodecTag.FromCharacters("MAGY"), CodecTag.FromCharacters("M0RG"),
    CodecTag.FromCharacters("M0RA"), CodecTag.FromCharacters("M0Y0"),
    CodecTag.FromCharacters("M0Y2"), CodecTag.FromCharacters("M0Y4"),
    CodecTag.FromCharacters("M0G0"), CodecTag.FromCharacters("M2RG"),
    CodecTag.FromCharacters("M2RA"), CodecTag.FromCharacters("M4RG"),
    CodecTag.FromCharacters("M4RA"),
  ];

  private const byte _CODED = 0;
  private const byte _UNCOMPRESSED = 1;
  private const uint _INTERLACED = 0x0000_0002;
  private const uint _FULL_RANGE = 0x0000_0004;

  private readonly int _width;
  private readonly int _height;
  private readonly int _streamIndex;
  private readonly MagicYuvFormat _format;

  private MagicYuvDecoder(int width, int height, int streamIndex, MagicYuvFormat format) {
    this._width = width;
    this._height = height;
    this._streamIndex = streamIndex;
    this._format = format;
  }

  public static string CodecName => "MagicYUV";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      return false;

    foreach (var tag in _Tags)
      if (stream.Codec.EqualsIgnoringCase(tag))
        return true;

    return false;
  }

  public static MagicYuvDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    var format = MagicYuvFormat.Of(stream.Codec, stream.Index);
    if (format.IsHighBitDepth && stream.BitsPerPixel > 0 && stream.BitsPerPixel != format.StreamBitsPerPixel)
      throw new NotSupportedException(
        $"Video stream {stream.Index} names {stream.Codec}, one of MagicYUV's formats deeper than eight bits, but states {stream.BitsPerPixel} bits per pixel where that FourCC uses {format.StreamBitsPerPixel}. The contradictory description is refused rather than decoded under one of the two meanings.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can be decoded into.");

    return new(stream.Width, stream.Height, stream.Index, format);
  }

  /// <summary>Decodes one self-contained MagicYUV key frame.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var decoded = this._Decode(packet.Data);
    frame = this._Compose(decoded);
    return true;
  }

  /// <summary>
  /// Decodes the native planes. Eight-bit planes contain one byte per sample; 10/12/14-bit planes
  /// contain right-justified little-endian <see cref="ushort"/> samples.
  /// </summary>
  internal byte[][] DecodePlanes(ReadOnlyMemory<byte> frame) {
    var decoded = this._Decode(frame);
    var result = new byte[decoded.Planes.Length][];
    for (var plane = 0; plane < result.Length; ++plane) {
      var source = decoded.Planes[plane];
      if (!this._format.IsHighBitDepth) {
        var bytes = new byte[source.Length];
        for (var i = 0; i < source.Length; ++i)
          bytes[i] = (byte)source[i];
        result[plane] = bytes;
        continue;
      }

      var deep = new byte[source.Length * 2];
      for (var i = 0; i < source.Length; ++i)
        BinaryPrimitives.WriteUInt16LittleEndian(deep.AsSpan(i * 2, 2), source[i]);
      result[plane] = deep;
    }

    return result;
  }

  private readonly record struct _DecodedFrame(ushort[][] Planes, uint Flags);

  private _DecodedFrame _Decode(ReadOnlyMemory<byte> frame) {
    var data = frame.Span;
    var format = this._format;
    if (data.Length < MagicYuvFormat.HEADER_SIZE || !data[..4].SequenceEqual(MagicYuvFormat.Signature))
      throw new InvalidDataException(
        $"A frame of {data.Length} bytes does not begin with this codec's four-byte signature, so it is not one of its frames.");

    var headerSize = checked((int)_ReadUInt32(data, 4));
    if (headerSize != MagicYuvFormat.HEADER_SIZE)
      throw new NotSupportedException(
        $"Video stream {this._streamIndex} states a frame header of {headerSize} bytes where every v7 frame measured states {MagicYuvFormat.HEADER_SIZE}. What a header of another size carries is not published and could not be measured, so it is refused rather than read as though the fields sat where they usually do.");

    var version = data[8];
    if (version != MagicYuvFormat.VERSION_BYTE)
      throw new NotSupportedException(
        $"Video stream {this._streamIndex} has {version} in the byte that is {MagicYuvFormat.VERSION_BYTE} in every frame measured here. What that byte means is not published — its position suggests a version and nothing states one — so a frame holding another value is one nothing was measured against, and it is refused rather than read on the assumption that the rest of the header is unchanged.");

    var statedWidth = checked((int)_ReadUInt32(data, 16));
    var statedHeight = checked((int)_ReadUInt32(data, 20));
    if (statedWidth != this._width || statedHeight != this._height)
      throw new InvalidDataException(
        $"A frame states a picture of {statedWidth}x{statedHeight} where video stream {this._streamIndex} states {this._width}x{this._height}.");

    var longestCode = data[10];
    if (longestCode != format.MaxHuffmanLength)
      throw new InvalidDataException(
        $"A {format.BitDepth}-bit frame states {longestCode} as its Huffman limit where MagicYUV v7 uses {format.MaxHuffmanLength}.");

    var flags = _ReadUInt32(data, 12);
    var interlaced = (flags & _INTERLACED) != 0;
    var sliceHeight = checked((int)_ReadUInt32(data, 28));
    if (sliceHeight <= 0)
      throw new InvalidDataException($"A frame states a slice height of {sliceHeight}.");

    if ((sliceHeight & ((1 << format.ChromaVerticalShift) - 1)) != 0)
      throw new InvalidDataException(
        $"A frame states a slice height of {sliceHeight}, which does not divide by the {1 << format.ChromaVerticalShift} luminance rows its chrominance rows each cover, so its slices would not line up between the planes.");

    var slices = (this._height + sliceHeight - 1) / sliceHeight;
    var planes = format.PlaneCount;
    var pieces = checked(planes * slices);
    if (pieces > 256)
      throw new InvalidDataException($"A frame has {pieces} plane slices, but its one-byte slice map can name at most 256.");

    var at = headerSize;
    var offsetBytes = checked((pieces + 1) * 4);
    if (at + offsetBytes > data.Length)
      throw new InvalidDataException(
        $"A frame of {data.Length} bytes ends inside the {pieces + 1} offsets its {planes} planes and {slices} slices need.");

    var tablesEnd = checked(headerSize + (int)_ReadUInt32(data, at));
    at += 4;
    if (tablesEnd < at || tablesEnd > data.Length)
      throw new InvalidDataException($"A frame says its Huffman tables end at byte {tablesEnd} of {data.Length}.");

    var listedStarts = new int[pieces];
    for (var i = 0; i < pieces; ++i) {
      listedStarts[i] = checked(headerSize + (int)_ReadUInt32(data, at));
      at += 4;
    }

    if (at >= data.Length)
      throw new InvalidDataException("A frame ends before it says how many code-length tables it carries.");

    var tableCount = data[at++];
    if (tableCount != planes)
      throw new InvalidDataException(
        $"A frame carries {tableCount} code-length tables where its {planes} planes need one each.");

    if (at + pieces > data.Length)
      throw new InvalidDataException($"A frame ends inside the {pieces}-byte map of its slices.");

    var starts = new int[pieces];
    Array.Fill(starts, -1);
    for (var listed = 0; listed < pieces; ++listed) {
      var piece = data[at++];
      if (piece >= pieces)
        throw new InvalidDataException($"A frame's slice map names piece {piece} where it has {pieces} of them.");
      if (starts[piece] >= 0)
        throw new InvalidDataException($"A frame's slice map names piece {piece} twice.");
      starts[piece] = listedStarts[listed];
    }

    var tables = new MagicYuvHuffmanTable[planes];
    for (var plane = 0; plane < planes; ++plane)
      tables[plane] = MagicYuvHuffmanTable.Read(
        data, ref at, tablesEnd, plane, format.SymbolCount, longestCode);

    if (at != tablesEnd)
      throw new InvalidDataException(
        $"A frame's Huffman descriptors end at byte {at} where its first slice starts at {tablesEnd}.");

    var decodedPlanes = new ushort[planes][];
    for (var plane = 0; plane < planes; ++plane) {
      var (planeWidth, planeHeight) = format.PlaneSize(plane, this._width, this._height);
      var planeSliceHeight = format.SliceHeight(plane, sliceHeight);
      if (interlaced && planeSliceHeight < 2)
        throw new InvalidDataException(
          $"An interlaced frame has a plane slice only {planeSliceHeight} row high, so its two fields cannot both start inside the slice.");

      var samples = new ushort[checked(planeWidth * planeHeight)];
      for (var slice = 0; slice < slices; ++slice) {
        var firstRow = Math.Min(slice * planeSliceHeight, planeHeight);
        var lastRow = Math.Min(firstRow + planeSliceHeight, planeHeight);
        if (lastRow <= firstRow)
          continue;
        if (interlaced && lastRow - firstRow < 2)
          throw new InvalidDataException(
            $"Interlaced plane {plane} slice {slice} has only {lastRow - firstRow} row, which cannot hold both fields.");

        var piece = slice * planes + plane;
        var start = starts[piece];
        var end = _EndOf(starts, start, data.Length);
        if (start < tablesEnd || start + 2 > end || end > data.Length)
          throw new InvalidDataException(
            $"Plane {plane} slice {slice} runs from byte {start} to {end} of a frame of {data.Length} bytes.");

        this._DecodeSlice(
          frame, data, start, end, tables[plane], samples, planeWidth,
          firstRow, lastRow, plane, slice, interlaced);
      }

      decodedPlanes[plane] = samples;
    }

    if (format.ColourSpace == MagicYuvColourSpace.Rgb) {
      var blue = decodedPlanes[0];
      var green = decodedPlanes[1];
      var red = decodedPlanes[2];
      var mask = format.SampleMask;
      for (var i = 0; i < green.Length; ++i) {
        var g = green[i];
        blue[i] = (ushort)((blue[i] + g) & mask);
        red[i] = (ushort)((red[i] + g) & mask);
      }

      decodedPlanes = format.HasAlpha
        ? [green, blue, red, decodedPlanes[3]]
        : [green, blue, red];
    }

    return new(decodedPlanes, flags);
  }

  private void _DecodeSlice(
    ReadOnlyMemory<byte> frame,
    ReadOnlySpan<byte> data,
    int start,
    int end,
    MagicYuvHuffmanTable table,
    ushort[] samples,
    int width,
    int firstRow,
    int lastRow,
    int plane,
    int slice,
    bool interlaced
  ) {
    var flag = data[start];
    var predictor = (MagicYuvPredictor)data[start + 1];
    if (predictor is not (MagicYuvPredictor.Left or MagicYuvPredictor.Gradient or MagicYuvPredictor.Median))
      throw new InvalidDataException(
        $"Plane {plane} slice {slice} states prediction method {data[start + 1]}, which is none of the three the format has.");

    var from = firstRow * width;
    var count = checked((lastRow - firstRow) * width);
    var bits = new MagicYuvBitReader(frame.Slice(start + 2, end - start - 2));

    switch (flag) {
      case _CODED:
        for (var i = 0; i < count; ++i)
          samples[from + i] = checked((ushort)table.Read(bits));
        break;

      case _UNCOMPRESSED:
        var neededBits = checked(count * this._format.BitDepth);
        if (bits.BitsRemaining < neededBits)
          throw new InvalidDataException(
            $"Plane {plane} slice {slice} is stored uncompressed but holds {bits.BitsRemaining} bits where its {lastRow - firstRow} rows need {neededBits}.");
        for (var i = 0; i < count; ++i)
          samples[from + i] = (ushort)bits.Bits(this._format.BitDepth);
        break;

      default:
        throw new InvalidDataException(
          $"Plane {plane} slice {slice} opens with the byte {flag}, which is neither the nought that means it is coded nor the one that means it is stored plainly.");
    }

    if (this._format.BitDepth == 8) {
      var residuals = new byte[count];
      for (var i = 0; i < count; ++i)
        residuals[i] = (byte)samples[from + i];
      MagicYuvPrediction.Apply(residuals, width, 0, lastRow - firstRow, predictor, interlaced);
      for (var i = 0; i < count; ++i)
        samples[from + i] = residuals[i];
    } else {
      MagicYuvPrediction.Apply(
        samples, width, firstRow, lastRow, predictor, this._format.SampleMask, interlaced);
    }
  }

  private static int _EndOf(int[] starts, int start, int frameLength) {
    var end = frameLength;
    foreach (var candidate in starts)
      if (candidate > start && candidate < end)
        end = candidate;
    return end;
  }

  private RawImage _Compose(_DecodedFrame decoded) {
    var format = this._format;
    if (format.ColourSpace == MagicYuvColourSpace.Grey)
      return this._ComposeGrey(decoded.Planes[0]);
    if (format.ColourSpace == MagicYuvColourSpace.Rgb)
      return this._ComposeRgb(decoded.Planes);
    if (format.IsHighBitDepth && !format.HasAlpha)
      return this._ComposeDeepYuv(decoded.Planes, decoded.Flags);
    return this._ComposeEightBitYuv(decoded.Planes, decoded.Flags);
  }

  private RawImage _ComposeGrey(ushort[] plane) {
    if (!this._format.IsHighBitDepth) {
      var pixels = new byte[plane.Length];
      for (var i = 0; i < plane.Length; ++i)
        pixels[i] = (byte)plane[i];
      return new() { Width = this._width, Height = this._height, Format = PixelFormat.Gray8, PixelData = pixels };
    }

    var deep = new byte[plane.Length * 2];
    for (var i = 0; i < plane.Length; ++i)
      BinaryPrimitives.WriteUInt16LittleEndian(deep.AsSpan(i * 2, 2), plane[i]);
    return new() { Width = this._width, Height = this._height, Format = PixelFormat.Gray10, PixelData = deep };
  }

  private RawImage _ComposeRgb(ushort[][] planes) {
    var count = this._width * this._height;
    var green = planes[0];
    var blue = planes[1];
    var red = planes[2];
    var alpha = this._format.HasAlpha ? planes[3] : null;
    var channels = alpha is null ? 3 : 4;

    if (!this._format.IsHighBitDepth) {
      var pixels = new byte[count * channels];
      for (var i = 0; i < count; ++i) {
        var at = i * channels;
        pixels[at] = (byte)red[i];
        pixels[at + 1] = (byte)green[i];
        pixels[at + 2] = (byte)blue[i];
        if (alpha is not null)
          pixels[at + 3] = (byte)alpha[i];
      }
      return new() {
        Width = this._width,
        Height = this._height,
        Format = alpha is null ? PixelFormat.Rgb24 : PixelFormat.Rgba32,
        PixelData = pixels,
      };
    }

    var deep = new byte[count * channels * 2];
    var mask = this._format.SampleMask;
    for (var i = 0; i < count; ++i) {
      var at = i * channels * 2;
      _WriteExpanded(deep, at, red[i], mask);
      _WriteExpanded(deep, at + 2, green[i], mask);
      _WriteExpanded(deep, at + 4, blue[i], mask);
      if (alpha is not null)
        _WriteExpanded(deep, at + 6, alpha[i], mask);
    }
    return new() {
      Width = this._width,
      Height = this._height,
      Format = alpha is null ? PixelFormat.Rgb48 : PixelFormat.Rgba64,
      PixelData = deep,
    };
  }

  private RawImage _ComposeDeepYuv(ushort[][] planes, uint flags) {
    var totalSamples = 0;
    foreach (var plane in planes)
      totalSamples = checked(totalSamples + plane.Length);

    var data = new byte[checked(totalSamples * 2)];
    var at = 0;
    foreach (var plane in planes)
      foreach (var sample in plane) {
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at, 2), sample);
        at += 2;
      }

    return new() {
      Width = this._width,
      Height = this._height,
      Format = this._format.NativePixelFormat,
      PixelData = data,
      ColorInfo = _ColourInfo(flags),
    };
  }

  private RawImage _ComposeEightBitYuv(ushort[][] planes, uint flags) {
    var luma = planes[0];
    var cb = planes[1];
    var cr = planes[2];
    var alpha = this._format.HasAlpha ? planes[3] : null;
    var channels = alpha is null ? 3 : 4;
    var (chromaWidth, chromaHeight) = this._format.PlaneSize(1, this._width, this._height);
    var pixels = new byte[this._width * this._height * channels];
    var use709 = _ReadColourMatrix(flags) == 2;
    var fullRange = (flags & _FULL_RANGE) != 0;

    for (var y = 0; y < this._height; ++y) {
      var chromaRow = Math.Min(y >> this._format.ChromaVerticalShift, chromaHeight - 1);
      var lumaRow = y * this._width;
      var target = lumaRow * channels;
      for (var x = 0; x < this._width; ++x) {
        var chromaColumn = Math.Min(x >> this._format.ChromaHorizontalShift, chromaWidth - 1);
        var chromaAt = chromaRow * chromaWidth + chromaColumn;
        _YuvToRgb(
          (byte)luma[lumaRow + x], (byte)cb[chromaAt], (byte)cr[chromaAt],
          use709, fullRange,
          out pixels[target], out pixels[target + 1], out pixels[target + 2]);
        if (alpha is not null)
          pixels[target + 3] = (byte)alpha[lumaRow + x];
        target += channels;
      }
    }

    return new() {
      Width = this._width,
      Height = this._height,
      Format = alpha is null ? PixelFormat.Rgb24 : PixelFormat.Rgba32,
      PixelData = pixels,
      ColorInfo = _ColourInfo(flags),
    };
  }

  private static void _YuvToRgb(byte y, byte cb, byte cr, bool bt709, bool fullRange, out byte r, out byte g, out byte b) {
    if (fullRange) {
      var yy = y << 8;
      var u = cb - 128;
      var v = cr - 128;
      if (bt709) {
        r = _Clamp(yy + 403 * v);
        g = _Clamp(yy - 48 * u - 120 * v);
        b = _Clamp(yy + 475 * u);
      } else {
        r = _Clamp(yy + 359 * v);
        g = _Clamp(yy - 88 * u - 183 * v);
        b = _Clamp(yy + 454 * u);
      }
      return;
    }

    var scaled = 298 * (y - 16);
    var blueDifference = cb - 128;
    var redDifference = cr - 128;
    if (bt709) {
      r = _Clamp(scaled + 459 * redDifference + 128);
      g = _Clamp(scaled - 55 * blueDifference - 136 * redDifference + 128);
      b = _Clamp(scaled + 541 * blueDifference + 128);
    } else {
      r = _Clamp(scaled + 409 * redDifference + 128);
      g = _Clamp(scaled - 100 * blueDifference - 208 * redDifference + 128);
      b = _Clamp(scaled + 516 * blueDifference + 128);
    }
  }

  private static RawImageColorInfo _ColourInfo(uint flags) => new() {
    Range = (flags & _FULL_RANGE) != 0 ? RawColorRange.Full : RawColorRange.Limited,
    Matrix = _ReadColourMatrix(flags) == 2 ? RawMatrixCoefficients.Bt709 : RawMatrixCoefficients.Bt601,
    Primaries = _ReadColourMatrix(flags) == 2 ? RawColorPrimaries.Bt709 : RawColorPrimaries.Smpte170M,
    Transfer = RawTransferCharacteristic.Bt709,
    ChromaLocation = RawChromaLocation.Left,
  };

  private static int _ReadColourMatrix(uint flags) => (int)((flags >> 20) & 0xF);

  private static void _WriteExpanded(byte[] target, int at, ushort sample, int mask) {
    var expanded = (ushort)((sample * 65535L + mask / 2) / mask);
    BinaryPrimitives.WriteUInt16LittleEndian(target.AsSpan(at, 2), expanded);
  }

  private static byte _Clamp(int scaled) {
    var value = scaled >> 8;
    return (byte)Math.Clamp(value, 0, 255);
  }

  private static uint _ReadUInt32(ReadOnlySpan<byte> source, int offset)
    => BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(offset, 4));
}
