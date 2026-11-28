using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using FileFormat.Codecs.Mpeg;

namespace FileFormat.Codecs.Ea;

/// <summary>
/// The forward transform the three EA DCT encoders share: a projection onto the basis
/// <see cref="EaIdct"/> reconstructs from, so a coefficient here is the coefficient the decoder needs.
/// </summary>
/// <remarks>
/// The basis is taken from the inverse transform's own arithmetic run at full precision: each of the
/// sixty-four coefficients alone, its response at the sixty-four samples. The AAN-derived inverse is a
/// scaled DCT, so the basis is orthogonal to within its integer constants and a projection is its inverse.
/// </remarks>
internal static class EaForward {

  private static readonly double[] _Basis = _Build();
  private static readonly double[] _Norm = _Norms();

  private static double[] _Build() {
    var basis = new double[64 * 64];
    Span<double> block = stackalloc double[64];
    Span<double> temporary = stackalloc double[64];
    for (var k = 0; k < 64; ++k) {
      block.Clear();
      block[k] = 1;
      for (var i = 0; i < 8; ++i)
        _Transform(block, i, 8, temporary, i, 8);
      Span<double> row = stackalloc double[8];
      for (var i = 0; i < 8; ++i) {
        _Transform(temporary, i * 8, 1, row, 0, 1);
        for (var x = 0; x < 8; ++x)
          basis[k * 64 + i * 8 + x] = row[x] / 16;
      }
    }
    return basis;
  }

  private static double[] _Norms() {
    var norms = new double[64];
    for (var k = 0; k < 64; ++k)
      for (var p = 0; p < 64; ++p)
        norms[k] += _Basis[k * 64 + p] * _Basis[k * 64 + p];
    return norms;
  }

  private static void _Transform(ReadOnlySpan<double> s, int at, int step, Span<double> d, int dat, int dstep) {
    double s0 = s[at], s1 = s[at + step], s2 = s[at + 2 * step], s3 = s[at + 3 * step];
    double s4 = s[at + 4 * step], s5 = s[at + 5 * step], s6 = s[at + 6 * step], s7 = s[at + 7 * step];
    double a1 = s1 + s7, a7 = s1 - s7, a5 = s5 + s3, a3 = s5 - s3, a2 = s2 + s6;
    var a6 = 181.0 / 256 * (s2 - s6);
    double a0 = s0 + s4, a4 = s0 - s4;
    var c = ((669.0 - 196) * a7 - 196 * a3) / 512;
    var e = 181.0 / 256 * (a1 - a5);
    var f = ((277.0 + 196) * a3 + 196 * a7) / 512;
    var b0 = c + a1 + a5;
    var b1 = c + e;
    var b2 = f + e;
    var b3 = f;
    d[dat] = a0 + a2 + a6 + b0;
    d[dat + dstep] = a4 + a6 + b1;
    d[dat + 2 * dstep] = a4 - a6 + b2;
    d[dat + 3 * dstep] = a0 - a2 - a6 + b3;
    d[dat + 4 * dstep] = a0 - a2 - a6 - b3;
    d[dat + 5 * dstep] = a4 - a6 - b2;
    d[dat + 6 * dstep] = a4 + a6 - b1;
    d[dat + 7 * dstep] = a0 + a2 + a6 - b0;
  }

  /// <summary>The coefficients, in raster order, that reconstruct <paramref name="samples"/>.</summary>
  internal static void Transform(ReadOnlySpan<int> samples, Span<double> coefficients) {
    for (var k = 0; k < 64; ++k) {
      var sum = 0.0;
      for (var p = 0; p < 64; ++p)
        sum += samples[p] * _Basis[k * 64 + p];
      coefficients[k] = sum / _Norm[k];
    }
  }

  /// <summary>Reads an 8x8 block of a plane, clamping at its edges.</summary>
  internal static void Read(byte[] plane, int stride, int rows, int x, int y, Span<int> samples) {
    for (var r = 0; r < 8; ++r)
      for (var c = 0; c < 8; ++c)
        samples[r * 8 + c] = plane[Math.Min(y + r, rows - 1) * stride + Math.Min(x + c, stride - 1)];
  }
}

/// <summary>Planes padded to whole macroblocks, as the encoders read a picture.</summary>
internal sealed record EaSource(byte[] Y, byte[] U, byte[] V, int LumaStride, int LumaRows, int ChromaStride, int ChromaRows) {
  internal static EaSource From(ReadOnlySpan<byte> y, ReadOnlySpan<byte> u, ReadOnlySpan<byte> v, int width, int height) {
    var picture = new EaPicture(width, height);
    int cw = (width + 1) / 2, ch = (height + 1) / 2;
    for (var r = 0; r < picture.LumaRows; ++r)
      for (var c = 0; c < picture.LumaStride; ++c)
        picture.Y[r * picture.LumaStride + c] = y[Math.Min(r, height - 1) * width + Math.Min(c, width - 1)];
    for (var r = 0; r < picture.ChromaRows; ++r)
      for (var c = 0; c < picture.ChromaStride; ++c) {
        picture.U[r * picture.ChromaStride + c] = u[Math.Min(r, ch - 1) * cw + Math.Min(c, cw - 1)];
        picture.V[r * picture.ChromaStride + c] = v[Math.Min(r, ch - 1) * cw + Math.Min(c, cw - 1)];
      }
    return new(picture.Y, picture.U, picture.V, picture.LumaStride, picture.LumaRows, picture.ChromaStride, picture.ChromaRows);
  }

  /// <summary>Block <paramref name="n"/> of macroblock (<paramref name="column"/>, <paramref name="row"/>), 0-3 luma, 4 U, 5 V.</summary>
  internal void ReadBlock(int column, int row, int n, Span<int> samples) {
    if (n < 4)
      EaForward.Read(this.Y, this.LumaStride, this.LumaRows, column * 16 + (n & 1) * 8, row * 16 + (n >> 1) * 8, samples);
    else
      EaForward.Read(n == 4 ? this.U : this.V, this.ChromaStride, this.ChromaRows, column * 8, row * 8, samples);
  }
}

/// <summary>Writes TQI pictures: MPEG-1 intra blocks at one quality.</summary>
internal static class TqiEncoder {

  internal static byte[] Encode(EaSource source, int width, int height, int quality) {
    var matrix = TqiCodec.Matrix(quality);
    var writer = new MpegBitWriter();
    Span<int> lastDc = stackalloc int[3];
    Span<int> samples = stackalloc int[64];
    Span<double> coefficients = stackalloc double[64];
    var zigzag = EaTables.ZigZag;

    for (var row = 0; row < (height + 15) / 16; ++row)
      for (var column = 0; column < (width + 15) / 16; ++column)
        for (var n = 0; n < 6; ++n) {
          source.ReadBlock(column, row, n, samples);
          EaForward.Transform(samples, coefficients);
          var component = n <= 3 ? 0 : n - 3;
          var dc = (int)Math.Round(coefficients[0] / matrix[0]);
          _WriteDc(writer, dc - lastDc[component], component == 0);
          lastDc[component] = dc;

          var run = 0;
          for (var i = 1; i < 64; ++i) {
            var j = zigzag[i];
            var level = matrix[j] == 0 ? 0 : (int)Math.Round(coefficients[j] * 16 / matrix[j]);
            level = Math.Clamp(level, -255, 255);
            if (level == 0) {
              ++run;
              continue;
            }
            _WriteCoefficient(writer, run, level);
            run = 0;
          }
          MpegVlcTables.Coefficient.Write(writer, MpegVlcTables.EndOfBlock);
        }

    var bits = writer.ToArray();
    var length = (bits.Length + 3) & ~3;
    var packet = new byte[8 + length + 4];
    BinaryPrimitives.WriteUInt16LittleEndian(packet, (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), (ushort)height);
    packet[4] = (byte)quality;
    bits.CopyTo(packet, 8);
    for (var w = 0; w < (packet.Length - 8) / 4; ++w)
      Array.Reverse(packet, 8 + w * 4, 4);
    return packet;
  }

  private static void _WriteDc(MpegBitWriter writer, int difference, bool luma) {
    var magnitude = Math.Abs(difference);
    var size = 0;
    while ((1 << size) <= magnitude)
      ++size;
    (luma ? MpegVlcTables.Mpeg2LuminanceDcSize : MpegVlcTables.Mpeg2ChrominanceDcSize).Write(writer, size);
    if (size != 0)
      writer.WriteBits(difference >= 0 ? difference : difference + (1 << size) - 1, size);
  }

  /// <summary>A run and level through Table B.14, or MPEG-1's escape as FFmpeg reads it.</summary>
  internal static void _WriteCoefficient(MpegBitWriter writer, int run, int level) {
    var magnitude = Math.Abs(level);
    if (magnitude < 256 && MpegVlcTables.Coefficient.TryWrite(writer, (run << 8) | magnitude)) {
      writer.WriteBit(level < 0 ? 1 : 0);
      return;
    }

    MpegVlcTables.Coefficient.Write(writer, MpegVlcTables.CoefficientEscape);
    writer.WriteBits(run, 6);
    if (level is > -128 and < 128)
      writer.WriteBits(level & 0xFF, 8);
    else if (level > 0) {
      writer.WriteBits(0, 8);
      writer.WriteBits(level, 8);
    } else {
      writer.WriteBits(0x80, 8);
      writer.WriteBits(level + 256, 8);
    }
  }
}

/// <summary>A writer for TGQ's and TGV's least-significant-bit-first fields.</summary>
internal sealed class EaLsbWriter {
  private readonly List<byte> _bytes = [];
  private int _current;
  private int _filled;

  internal int BitCount => this._bytes.Count * 8 + this._filled;

  internal void Write(int value, int count) {
    for (var i = 0; i < count; ++i) {
      this._current |= ((value >> i) & 1) << this._filled;
      if (++this._filled == 8) {
        this._bytes.Add((byte)this._current);
        this._current = 0;
        this._filled = 0;
      }
    }
  }

  internal byte[] ToArray() {
    var result = new List<byte>(this._bytes);
    if (this._filled != 0)
      result.Add((byte)this._current);
    return result.ToArray();
  }
}

/// <summary>Writes TGQ pictures: coded macroblocks, and macroblocks copied from the last picture where they match it.</summary>
internal sealed class TgqEncoder(int width, int height, int quality) {

  private const int _COPY_THRESHOLD = 3 * 256;
  private EaPicture? _last;

  internal byte[] Encode(EaSource source, bool keyFrame) {
    var table = TgqCodec.Table(quality);
    var output = new List<byte>();
    Span<byte> header = stackalloc byte[16];
    BinaryPrimitives.WriteUInt32LittleEndian(header, 0x73514754); // TGQs
    BinaryPrimitives.WriteUInt16LittleEndian(header[8..], (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(header[10..], (ushort)height);
    header[12] = (byte)quality;
    output.AddRange(header.ToArray());

    Span<int> samples = stackalloc int[64];
    Span<double> coefficients = stackalloc double[64];
    var zigzag = EaTables.ZigZag;
    for (var row = 0; row < (height + 15) / 16; ++row)
      for (var column = 0; column < (width + 15) / 16; ++column) {
        if (!keyFrame && this._last != null && column * 16 + 16 <= width && row * 16 + 16 <= height
            && _Difference(source, this._last, column, row) < _COPY_THRESHOLD) {
          output.Add(1);
          output.Add(0); // no motion
          continue;
        }

        var writer = new EaLsbWriter();
        for (var n = 0; n < 6; ++n) {
          source.ReadBlock(column, row, n, samples);
          EaForward.Transform(samples, coefficients);
          writer.Write(Math.Clamp((int)Math.Round((coefficients[0] - 2048 - 4) / table[0]), -128, 127), 8);
          var zeros = 0;
          for (var i = 1; i < 64; ++i) {
            var j = zigzag[i];
            var level = (int)Math.Round(coefficients[j] / table[j]);
            if (level == 0) {
              ++zeros;
              continue;
            }
            _Zeros(writer, zeros);
            zeros = 0;
            if (level == 1)
              writer.Write(2, 3);
            else if (level == -1)
              writer.Write(6, 3);
            else {
              writer.Write(3, 2);
              level = Math.Clamp(level, -128, 127);
              if (level is >= -32 and < 32 && (level & 0x3F) != 0x3F)
                writer.Write(level & 0x3F, 6);
              else {
                writer.Write(0x3F, 6);
                writer.Write(level & 0xFF, 8);
              }
            }
          }
          _Zeros(writer, zeros);
        }

        var bits = writer.ToArray();
        if (bits.Length > 255) {
          // Too busy for one mode byte: the DC values alone.
          output.Add(6);
          for (var n = 0; n < 6; ++n) {
            source.ReadBlock(column, row, n, samples);
            var mean = 0;
            foreach (var s in samples)
              mean += s;
            output.Add((byte)(sbyte)Math.Clamp((int)Math.Round((mean / 64.0 * 16 - 2056) / table[0]), -128, 127));
          }
          continue;
        }

        var length = Math.Max(13, bits.Length);
        output.Add((byte)length);
        output.AddRange(bits);
        for (var i = bits.Length; i < length; ++i)
          output.Add(0);
      }

    var packet = output.ToArray();
    BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), (uint)packet.Length);
    this._last = TgqCodec.Decode(packet, this._last);
    return packet;
  }

  private static void _Zeros(EaLsbWriter writer, int count) {
    while (count > 0) {
      if (count == 1) {
        writer.Write(0, 3);
        return;
      }
      var run = Math.Min(count, 63);
      writer.Write(1, 2);
      writer.Write(run, 6);
      count -= run;
    }
  }

  private static int _Difference(EaSource source, EaPicture last, int column, int row) {
    var sum = 0;
    for (var y = 0; y < 16; ++y)
      for (var x = 0; x < 16; ++x) {
        var at = (row * 16 + y) * source.LumaStride + column * 16 + x;
        sum += Math.Abs(source.Y[at] - last.Y[at]);
      }
    for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x) {
        var at = (row * 8 + y) * source.ChromaStride + column * 8 + x;
        sum += Math.Abs(source.U[at] - last.U[at]) + Math.Abs(source.V[at] - last.V[at]);
      }
    return sum;
  }
}

/// <summary>Writes MAD pictures: key frames of intra blocks, and predicted ones whose macroblocks are copied with a DC offset where that serves.</summary>
internal sealed class MadEncoder(int width, int height, int scale, int frameMilliseconds) {

  private const int _COPY_THRESHOLD = 4 * 256;
  private EaPicture? _reference;

  internal byte[] Encode(EaSource source, bool keyFrame) {
    if (this._reference == null)
      keyFrame = true;
    var matrix = MadCodec.Matrix(scale);
    var writer = new MpegBitWriter();
    Span<int> samples = stackalloc int[64];
    Span<double> coefficients = stackalloc double[64];
    var zigzag = EaTables.ZigZag;

    for (var row = 0; row < (height + 15) / 16; ++row)
      for (var column = 0; column < (width + 15) / 16; ++column) {
        if (!keyFrame) {
          Span<int> offsets = stackalloc int[6];
          if (_Copyable(source, this._reference!, column, row, offsets)) {
            writer.WriteBit(1); // every block copied
            writer.WriteBit(0); // no motion, either way
            writer.WriteBit(0);
            for (var n = 0; n < 6; ++n)
              _WriteMotion(writer, offsets[n]);
            continue;
          }
          writer.WriteBit(0);
          writer.WriteBit(0); // every block coded
        }

        for (var n = 0; n < 6; ++n) {
          source.ReadBlock(column, row, n, samples);
          EaForward.Transform(samples, coefficients);
          var m0 = (short)matrix[0];
          writer.WriteBits(Math.Clamp((int)Math.Round((coefficients[0] - 4) / m0) - 128, -128, 127) & 0xFF, 8);
          var run = 0;
          for (var i = 1; i < 64; ++i) {
            var j = zigzag[i];
            var m = (short)matrix[j];
            var level = m == 0 ? 0 : (int)Math.Round(coefficients[j] * 16 / m);
            level = Math.Clamp(level, -511, 511);
            if (level == 0) {
              ++run;
              continue;
            }
            var magnitude = Math.Abs(level);
            if (magnitude < 41 && MpegVlcTables.Coefficient.TryWrite(writer, (run << 8) | magnitude))
              writer.WriteBit(level < 0 ? 1 : 0);
            else {
              MpegVlcTables.Coefficient.Write(writer, MpegVlcTables.CoefficientEscape);
              writer.WriteBits(level & 0x3FF, 10);
              writer.WriteBits(run, 6);
            }
            run = 0;
          }
          MpegVlcTables.Coefficient.Write(writer, MpegVlcTables.EndOfBlock);
        }
      }

    var bits = writer.ToArray();
    var body = (bits.Length + 1) & ~1;
    var packet = new byte[24 + body + 2];
    BinaryPrimitives.WriteUInt32LittleEndian(packet, keyFrame ? MadCodec.KeyTag : MadCodec.PredictedTag);
    BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), (uint)packet.Length);
    BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(14), (ushort)frameMilliseconds);
    BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(16), (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(18), (ushort)height);
    packet[21] = (byte)scale;
    bits.CopyTo(packet, 24);
    for (var w = 0; w < (packet.Length - 24) / 2; ++w)
      (packet[24 + 2 * w], packet[25 + 2 * w]) = (packet[25 + 2 * w], packet[24 + 2 * w]);

    this._reference = MadCodec.Decode(packet, this._reference, out _);
    return packet;
  }

  /// <summary>Whether every block of the macroblock is near enough the reference's with an even DC offset; the offsets in halves.</summary>
  private bool _Copyable(EaSource source, EaPicture reference, int column, int row, Span<int> offsets) {
    if (column * 16 + 16 > width || row * 16 + 16 > height)
      return false;
    var total = 0;
    for (var n = 0; n < 6; ++n) {
      byte[] s, r;
      int stride, x, y;
      if (n < 4)
        (s, r, stride, x, y) = (source.Y, reference.Y, source.LumaStride, column * 16 + (n & 1) * 8, row * 16 + (n >> 1) * 8);
      else
        (s, r, stride, x, y) = (n == 4 ? source.U : source.V, n == 4 ? reference.U : reference.V, source.ChromaStride, column * 8, row * 8);
      var difference = 0;
      for (var j = 0; j < 8; ++j)
        for (var i = 0; i < 8; ++i)
          difference += s[(y + j) * stride + x + i] - r[(y + j) * stride + x + i];
      var half = Math.Clamp((int)Math.Round(difference / 128.0), -16, 16);
      offsets[n] = half;
      for (var j = 0; j < 8; ++j)
        for (var i = 0; i < 8; ++i)
          total += Math.Abs(s[(y + j) * stride + x + i] - Math.Clamp(r[(y + j) * stride + x + i] + 2 * half, 0, 255));
    }
    return total < _COPY_THRESHOLD;
  }

  private static void _WriteMotion(MpegBitWriter writer, int value) {
    if (value == 0) {
      writer.WriteBit(0);
      return;
    }
    writer.WriteBit(1);
    if (value > 0) {
      writer.WriteBit(0);
      writer.WriteBits(value - 1, 4);
    } else {
      writer.WriteBit(1);
      writer.WriteBits(value + 16, 4);
    }
  }
}

/// <summary>Writes TGV pictures: LZ77-packed key frames and predicted frames of copied and raw cells.</summary>
internal sealed class TgvEncoder(int width, int height) {

  private byte[]? _last;

  internal byte[] Encode(byte[] indices, byte[] palette, int paletteCount, bool keyFrame) {
    if (this._last == null)
      keyFrame = true;
    var output = new List<byte>();
    output.AddRange(new byte[8]);
    if (keyFrame) {
      Span<byte> header = stackalloc byte[12];
      BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)width);
      BinaryPrimitives.WriteUInt16LittleEndian(header[2..], (ushort)height);
      BinaryPrimitives.WriteUInt16LittleEndian(header[6..], (ushort)paletteCount);
      output.AddRange(header.ToArray());
      output.AddRange(palette.AsSpan(0, paletteCount * 3).ToArray());
      output.Add(0x10);
      output.Add(0xFB);
      output.Add((byte)(indices.Length >> 16));
      output.Add((byte)(indices.Length >> 8));
      output.Add((byte)indices.Length);
      _Pack(indices, output);
    } else
      this._Inter(indices, output);

    var packet = output.ToArray();
    BinaryPrimitives.WriteUInt32LittleEndian(packet, keyFrame ? TgvCodec.KeyTag : 0x54475666u);
    BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), (uint)packet.Length);
    this._last = (byte[])indices.Clone();
    return packet;
  }

  /// <summary>Greedy LZ77 in the decoder's four token forms.</summary>
  private static void _Pack(byte[] data, List<byte> output) {
    var heads = new Dictionary<int, List<int>>();
    var literalStart = 0;
    var at = 0;
    while (at < data.Length) {
      int bestLength = 0, bestOffset = 0;
      if (at + 3 <= data.Length) {
        var key = data[at] | (data[at + 1] << 8) | (data[at + 2] << 16);
        if (heads.TryGetValue(key, out var positions))
          for (var p = positions.Count - 1; p >= 0 && positions.Count - p <= 32; --p) {
            var candidate = positions[p];
            var offset = at - candidate;
            if (offset > 131072)
              break;
            var length = 0;
            while (at + length < data.Length && length < 1028 && data[candidate + length] == data[at + length])
              ++length;
            var limit = offset <= 1024 ? 10 : offset <= 16384 ? 67 : 1028;
            var minimum = offset <= 1024 ? 3 : offset <= 16384 ? 4 : 5;
            length = Math.Min(length, Math.Max(limit, 1028));
            if (length >= minimum && length > bestLength) {
              bestLength = length;
              bestOffset = offset;
            }
          }
      }

      if (bestLength == 0) {
        _Remember(heads, data, at);
        ++at;
        continue;
      }

      // Literals before the copy: whole fours as their own tokens, the rest carried by the copy.
      var literals = at - literalStart;
      while (literals >= 4) {
        var run = Math.Min(literals & ~3, 112);
        output.Add((byte)(0xE0 | (run / 4 - 1)));
        for (var i = 0; i < run; ++i)
          output.Add(data[literalStart + i]);
        literalStart += run;
        literals -= run;
      }

      var copy = bestLength;
      var o = bestOffset - 1;
      if (bestOffset <= 1024 && copy <= 10) {
        output.Add((byte)(((o >> 8) << 5) & 0x60 | (copy - 3) << 2 | literals));
        output.Add((byte)o);
      } else if (bestOffset <= 16384 && copy <= 67) {
        output.Add((byte)(0x80 | (copy - 4)));
        output.Add((byte)((literals << 6) | (o >> 8)));
        output.Add((byte)o);
      } else {
        copy = Math.Min(copy, 1028);
        var c = copy - 5;
        output.Add((byte)(0xC0 | ((o >> 16) & 1) << 4 | ((c >> 8) & 3) << 2 | literals));
        output.Add((byte)(o >> 8));
        output.Add((byte)o);
        output.Add((byte)c);
      }
      for (var i = 0; i < literals; ++i)
        output.Add(data[literalStart + i]);

      for (var i = 0; i < copy; ++i)
        _Remember(heads, data, at + i);
      at += copy;
      literalStart = at;
    }

    var remaining = data.Length - literalStart;
    while (remaining >= 4) {
      var run = Math.Min(remaining & ~3, 112);
      output.Add((byte)(0xE0 | (run / 4 - 1)));
      for (var i = 0; i < run; ++i)
        output.Add(data[literalStart + i]);
      literalStart += run;
      remaining -= run;
    }
    output.Add((byte)(0xFC | remaining));
    for (var i = 0; i < remaining; ++i)
      output.Add(data[literalStart + i]);
  }

  private static void _Remember(Dictionary<int, List<int>> heads, byte[] data, int at) {
    if (at + 3 > data.Length)
      return;
    var key = data[at] | (data[at + 1] << 8) | (data[at + 2] << 16);
    if (!heads.TryGetValue(key, out var positions))
      heads[key] = positions = [];
    positions.Add(at);
  }

  /// <summary>A predicted frame: cells unchanged from the last picture copied, the rest raw, repeats shared.</summary>
  private void _Inter(byte[] indices, List<byte> output) {
    var cellsAcross = width / 4;
    var cellsDown = height / 4;
    var raw = new List<byte[]>();
    var seen = new Dictionary<string, int>();
    var choice = new int[cellsAcross * cellsDown];
    var last = this._last!;
    Span<byte> cell = stackalloc byte[16];
    for (var y = 0; y < cellsDown; ++y)
      for (var x = 0; x < cellsAcross; ++x) {
        var same = true;
        for (var j = 0; j < 4; ++j)
          for (var i = 0; i < 4; ++i) {
            var at = (y * 4 + j) * width + x * 4 + i;
            cell[j * 4 + i] = indices[at];
            same &= indices[at] == last[at];
          }
        if (same) {
          choice[y * cellsAcross + x] = 0;
          continue;
        }
        var key = Convert.ToBase64String(cell);
        if (!seen.TryGetValue(key, out var index)) {
          index = raw.Count;
          seen[key] = index;
          raw.Add(cell.ToArray());
        }
        choice[y * cellsAcross + x] = 1 + index;
      }

    var symbols = 1 + raw.Count;
    var bits = 1;
    while ((1 << bits) < symbols)
      ++bits;

    Span<byte> header = stackalloc byte[12];
    BinaryPrimitives.WriteUInt16LittleEndian(header, 1);
    BinaryPrimitives.WriteUInt16LittleEndian(header[2..], (ushort)raw.Count);
    BinaryPrimitives.WriteUInt16LittleEndian(header[4..], 0);
    BinaryPrimitives.WriteUInt16LittleEndian(header[6..], (ushort)bits);
    output.AddRange(header.ToArray());

    // One vector, no motion, in a 32-bit-padded field.
    output.AddRange(new byte[4]);
    foreach (var block in raw)
      output.AddRange(block);
    var writer = new EaLsbWriter();
    foreach (var c in choice)
      writer.Write(c, bits);
    output.AddRange(writer.ToArray());
    output.AddRange(new byte[4]);
  }
}
