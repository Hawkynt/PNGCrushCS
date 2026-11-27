using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.Hqx;

namespace FileFormat.Codecs.HqHqa;

/// <summary>The transform, the code, and the quantisers HQ and HQA share.</summary>
internal static class HqShared {

  internal static readonly HqxVlc Ac = HqxVlc.FromLengths(HqHqaTables.AcLengths, HqHqaTables.AcSymbols);

  /// <summary>The matrix a block of <paramref name="group"/> chooses with <paramref name="select"/>, in zigzag order.</summary>
  internal static ReadOnlySpan<int> Matrix(int group, bool chroma, int select)
    => HqHqaTables.Matrices.AsSpan(HqHqaTables.QuantMap[group * 8 + (chroma ? 4 : 0) + select] * 64, 64);

  private static int _Mul(int a, int b) => unchecked((int)((uint)a * (uint)b)) >> 16;

  /// <summary>FFmpeg's <c>hq_idct_put</c>: rows then columns, sixteen-bit intermediates, eight-bit results clamped.</summary>
  internal static void InverseTransform(short[] block, byte[] plane, int at, int stride) {
    const int fix1082 = 17734, fix1847 = 30274, fix1414 = 23170, fix2613 = 21407;
    for (var i = 0; i < 8; ++i) {
      var r = i * 8;
      int b0 = block[r], b1 = block[r + 1], b2 = block[r + 2], b3 = block[r + 3], b4 = block[r + 4], b5 = block[r + 5], b6 = block[r + 6], b7 = block[r + 7];
      var t0 = b5 - b3;
      var t1 = b5 + b3;
      var t2 = b1 - b7;
      var t3 = b1 + b7;
      var t4 = t3 - t1;
      var t5 = _Mul(t0 + t2, fix1847);
      var t6 = _Mul(t2, fix1082) - t5;
      var t7 = t5 - _Mul(t0, fix2613) * 2;
      var t8 = t3 + t1;
      var t9 = t7 * 4 - t8;
      var tA = _Mul(t4, fix1414) * 4 - t9;
      var tB = t6 * 4 + tA;
      var tC = b2 + b6;
      var tD = b2 - b6;
      var tE = b0 - b4;
      var tF = b0 + b4;
      var t10 = _Mul(tD, fix1414) * 4 - tC;
      var t11 = tE - t10;
      var t12 = tF - tC;
      var t13 = tE + t10;
      var t14 = tF + tC;
      block[r] = unchecked((short)(t14 + t8));
      block[r + 1] = unchecked((short)(t13 + t9));
      block[r + 2] = unchecked((short)(t11 + tA));
      block[r + 3] = unchecked((short)(t12 - tB));
      block[r + 4] = unchecked((short)(t12 + tB));
      block[r + 5] = unchecked((short)(t11 - tA));
      block[r + 6] = unchecked((short)(t13 - t9));
      block[r + 7] = unchecked((short)(t14 - t8));
    }
    for (var i = 0; i < 8; ++i) {
      int b0 = block[i], b1 = block[8 + i], b2 = block[16 + i], b3 = block[24 + i], b4 = block[32 + i], b5 = block[40 + i], b6 = block[48 + i], b7 = block[56 + i];
      var t0 = b5 - b3;
      var t1 = b5 + b3;
      var t2 = b1 * 2 - (b7 >> 2);
      var t3 = b1 * 2 + (b7 >> 2);
      var t4 = t3 - t1;
      var t5 = _Mul(t0 + t2, fix1847);
      var t6 = _Mul(t2, fix1082) - t5;
      var t7 = t5 - _Mul(t0, fix2613) * 2;
      var t8 = (t3 + t1) >> 1;
      var t9 = t7 * 2 - t8;
      var tA = _Mul(t4, fix1414) * 2 - t9;
      var tB = t6 * 2 + tA;
      var tC = (b2 + (b6 >> 1)) >> 1;
      var tD = b2 - (b6 >> 1);
      var tE = (b0 >> 1) - (b4 >> 1) + 0x2020;
      var tF = (b0 >> 1) + (b4 >> 1) + 0x2020;
      var t10 = _Mul(tD, fix1414) * 2 - tC;
      var t11 = tE - t10;
      var t12 = tF - tC;
      var t13 = tE + t10;
      var t14 = tF + tC;
      block[i] = unchecked((short)((t14 + t8) >> 6));
      block[8 + i] = unchecked((short)((t13 + t9) >> 6));
      block[16 + i] = unchecked((short)((t11 + tA) >> 6));
      block[24 + i] = unchecked((short)((t12 - tB) >> 6));
      block[32 + i] = unchecked((short)((t12 + tB) >> 6));
      block[40 + i] = unchecked((short)((t11 - tA) >> 6));
      block[48 + i] = unchecked((short)((t13 - t9) >> 6));
      block[56 + i] = unchecked((short)((t14 - t8) >> 6));
    }
    for (var i = 0; i < 8; ++i)
      for (var j = 0; j < 8; ++j)
        plane[at + i * stride + j] = (byte)Math.Clamp((int)block[i * 8 + j], 0, 255);
  }

  /// <summary>
  /// FFmpeg's <c>hq_decode_block</c>: a nine-bit DC and a matrix choice — HQA reads the choice first —
  /// then run-and-level codes, each level scaled by its zigzag position's matrix entry.
  /// </summary>
  internal static void DecodeBlock(ref HqxBitReader reader, short[] block, int group, bool chroma, bool hqa) {
    ReadOnlySpan<int> q;
    if (!hqa) {
      block[0] = (short)(_SignedBits(ref reader, 9) * 64);
      q = Matrix(group, chroma, reader.Read(2));
    } else {
      q = Matrix(group, chroma, reader.Read(2));
      block[0] = (short)(_SignedBits(ref reader, 9) * 64);
    }
    for (var position = 0; ;) {
      var symbol = Ac.Read(ref reader);
      var run = symbol & 0x7F;
      if (run == 0)
        throw new InvalidDataException("An HQ block holds the code its table marks invalid.");
      position += run;
      if (position >= 64)
        break;
      block[HqxShared.ZigZag[position]] = unchecked((short)(unchecked((int)((uint)(symbol >> 7) * (uint)q[position])) >> 12));
    }
  }

  private static int _SignedBits(ref HqxBitReader reader, int count) => reader.Read(count) << (32 - count) >> (32 - count);

  /// <summary>Where HQA's slice <paramref name="slice"/> places its macroblocks: every eighth along a diagonal.</summary>
  internal static List<(int X, int Y)> HqaSliceOrder(int width, int height, int slice) {
    var order = new List<(int, int)>();
    for (var y = 0; y < height; y += 16)
      for (var x = (slice * 16 + y * 3) & 0x70; x < width; x += 128)
        order.Add((x, y));
    return order;
  }
}

/// <summary>A decoded HQ or HQA picture: 8-bit 4:2:2 planes at the coded size, and an alpha plane for HQA.</summary>
internal sealed class HqFrame {

  internal int Width;
  internal int Height;
  internal bool Alpha;
  internal byte[][] Planes = [];
  internal int[] Strides = [];
  private readonly short[][] _blocks = _NewBlocks();

  private static short[][] _NewBlocks() {
    var blocks = new short[12][];
    for (var i = 0; i < 12; ++i)
      blocks[i] = new short[64];
    return blocks;
  }

  private void _Allocate(int width, int height, bool alpha) {
    this.Width = width;
    this.Height = height;
    this.Alpha = alpha;
    var codedWidth = (width + 15) & ~15;
    var codedHeight = (height + 15) & ~15;
    this.Strides = alpha ? [codedWidth, codedWidth / 2, codedWidth / 2, codedWidth] : [codedWidth, codedWidth / 2, codedWidth / 2];
    this.Planes = new byte[this.Strides.Length][];
    for (var p = 0; p < this.Planes.Length; ++p)
      this.Planes[p] = new byte[this.Strides[p] * codedHeight];
  }

  internal static HqFrame Decode(ReadOnlySpan<byte> packet) {
    if (packet.Length < 8)
      throw new InvalidDataException($"An HQ frame is at least eight bytes; this one is {packet.Length}.");
    var data = packet;
    if (BinaryPrimitives.ReadUInt32LittleEndian(data) == 0x4F464E49) { // "INFO"
      var infoLength = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
      if (infoLength < 0 || data.Length - 8 < infoLength)
        throw new InvalidDataException("This HQ frame's INFO header runs past its end.");
      data = data[(8 + infoLength)..];
    }
    if (data.Length < 4)
      throw new InvalidDataException("This HQ frame is too short for its tag.");
    var tag = BinaryPrimitives.ReadUInt32LittleEndian(data);
    var frame = new HqFrame();
    if ((tag & 0xFFFFFF) == 0x435655) // "UVC" and a profile number
      frame._DecodeHq(data, (int)(tag >> 24));
    else if (tag == 0x31415148) // "HQA1"
      frame._DecodeHqa(data);
    else
      throw new InvalidDataException("This frame opens with neither UVC nor HQA1; it is not HQ or HQA.");
    return frame;
  }

  private void _DecodeHq(ReadOnlySpan<byte> data, int profileNumber) {
    // FFmpeg falls back to profile 0 for a number past the table, and asks for a sample.
    var profile = HqHqaTables.Profiles[(uint)profileNumber < (uint)HqHqaTables.Profiles.Length ? profileNumber : 0];
    var src = data[4..];
    if (src.Length < 3 * (profile.Slices + 1))
      throw new InvalidDataException("This HQ frame ends inside its slice table.");
    this._Allocate(profile.Width, profile.Height, false);

    // Offsets count from the tag.
    var offsets = new int[profile.Slices + 1];
    for (var i = 0; i <= profile.Slices; ++i)
      offsets[i] = (src[i * 3] << 16 | src[i * 3 + 1] << 8 | src[i * 3 + 2]) - 4;
    var permutation = profile.Offset;
    var next = 0;
    for (var slice = 0; slice < profile.Slices; ++slice) {
      var start = next;
      next = profile.TableHeight * (slice + 1) / profile.Slices;
      if (offsets[slice] < (profile.Slices + 1) * 3 || offsets[slice] >= offsets[slice + 1] || offsets[slice + 1] > data.Length)
        break; // FFmpeg stops at a slice it cannot place, and shows what it has
      var reader = new HqxBitReader(src[offsets[slice]..offsets[slice + 1]]);
      for (var i = 0; i < (next - start) * profile.TableWidth; ++i) {
        this._HqMacroblock(ref reader, HqHqaTables.Permutations[permutation] * 16, HqHqaTables.Permutations[permutation + 1] * 16);
        permutation += 2;
      }
    }
  }

  private void _HqMacroblock(ref HqxBitReader reader, int x, int y) {
    for (var i = 0; i < 8; ++i)
      Array.Clear(this._blocks[i]);
    var group = reader.Read(4);
    var interlaced = reader.Read(1) != 0;
    for (var i = 0; i < 8; ++i)
      HqShared.DecodeBlock(ref reader, this._blocks[i], group, i >= 4, false);
    this._Put(0, x, y, interlaced, 0, 2);
    this._Put(0, x + 8, y, interlaced, 1, 3);
    this._Put(2, x >> 1, y, interlaced, 4, 5);
    this._Put(1, x >> 1, y, interlaced, 6, 7);
  }

  private void _DecodeHqa(ReadOnlySpan<byte> data) {
    const int slices = 8;
    var src = data[4..];
    if (src.Length < 8 + 4 * (slices + 1))
      throw new InvalidDataException("This HQA frame ends inside its header.");
    var width = BinaryPrimitives.ReadUInt16BigEndian(src);
    var height = BinaryPrimitives.ReadUInt16BigEndian(src[2..]);
    if (width == 0 || height == 0)
      throw new InvalidDataException("This HQA frame states a picture with no pixels.");
    var group = src[4];
    if (group >= 16)
      throw new InvalidDataException($"HQA quantiser group {group} is not one of the sixteen.");
    this._Allocate(width, height, true);
    var offsets = new int[slices + 1];
    for (var i = 0; i <= slices; ++i)
      offsets[i] = (int)BinaryPrimitives.ReadUInt32BigEndian(src[(8 + i * 4)..]) - 4;
    for (var slice = 0; slice < slices; ++slice) {
      if (offsets[slice] < (slices + 1) * 3 || offsets[slice] >= offsets[slice + 1] || offsets[slice + 1] > data.Length)
        break;
      var reader = new HqxBitReader(src[offsets[slice]..offsets[slice + 1]]);
      foreach (var (x, y) in HqShared.HqaSliceOrder(width, height, slice))
        this._HqaMacroblock(ref reader, group, x, y);
    }
  }

  private void _HqaMacroblock(ref HqxBitReader reader, int group, int x, int y) {
    for (var i = 0; i < 12; ++i) {
      Array.Clear(this._blocks[i]);
      this._blocks[i][0] = -128 * 64;
    }
    var interlaced = false;
    var cbp = HqxShared.ReadCbp(ref reader);
    if (cbp != 0) {
      interlaced = reader.Read(1) != 0;
      if ((cbp & 0x3) != 0)
        cbp |= 0x500;
      if ((cbp & 0xC) != 0)
        cbp |= 0xA00;
      for (var i = 0; i < 12; ++i)
        if ((cbp & (1 << i)) != 0)
          HqShared.DecodeBlock(ref reader, this._blocks[i], group, i >= 8, true);
    }
    this._Put(3, x, y, interlaced, 0, 2);
    this._Put(3, x + 8, y, interlaced, 1, 3);
    this._Put(0, x, y, interlaced, 4, 6);
    this._Put(0, x + 8, y, interlaced, 5, 7);
    this._Put(2, x >> 1, y, interlaced, 8, 9);
    this._Put(1, x >> 1, y, interlaced, 10, 11);
  }

  private void _Put(int plane, int x, int y, bool interlaced, int top, int bottom) {
    var stride = this.Strides[plane];
    var step = interlaced ? stride * 2 : stride;
    HqShared.InverseTransform(this._blocks[top], this.Planes[plane], y * stride + x, step);
    HqShared.InverseTransform(this._blocks[bottom], this.Planes[plane], (y + (interlaced ? 1 : 8)) * stride + x, step);
  }
}

/// <summary>
/// Writes HQ frames for a picture of one of the profiles' sizes, and HQA frames, with alpha, for any other:
/// progressive, quantiser group 0, each block at the finest of its four matrices that holds it.
/// </summary>
internal sealed class HqEncoder {

  private const int _GROUP = 0;
  private const int _END = 65; // a level of zero and a run past the block

  private static readonly double[][] _Basis = _BuildBasis();

  private readonly int _width;
  private readonly int _height;
  private readonly int _profile;

  /// <summary>An encoder for an HQ profile's size, or HQA when <paramref name="profile"/> is negative.</summary>
  internal HqEncoder(int width, int height, int profile) {
    this._width = width;
    this._height = height;
    this._profile = profile;
  }

  internal bool Hqa => this._profile < 0;

  /// <summary>The HQ profile of a size, or -1 where none has it.</summary>
  internal static int ProfileOf(int width, int height) {
    for (var p = 1; p < HqHqaTables.Profiles.Length; ++p)
      if (HqHqaTables.Profiles[p].Width == width && HqHqaTables.Profiles[p].Height == height)
        return p;
    return -1;
  }

  private static double[][] _BuildBasis() {
    const int probe = 512;
    var basis = new double[64][];
    var block = new short[64];
    var output = new byte[64];
    for (var k = 0; k < 64; ++k) {
      Array.Clear(block);
      block[k] = probe;
      HqShared.InverseTransform(block, output, 0, 8);
      basis[k] = new double[64];
      for (var p = 0; p < 64; ++p)
        basis[k][p] = (output[p] - 128) / (double)probe;
    }
    return basis;
  }

  /// <summary>One frame from eight-bit planes at the picture size: luma, Cb and Cr at half width, and alpha for HQA.</summary>
  internal byte[] Encode(byte[][] planes) {
    var codedWidth = (this._width + 15) & ~15;
    var codedHeight = (this._height + 15) & ~15;
    var count = this.Hqa ? 4 : 3;
    var padded = new byte[count][];
    var strides = new int[count];
    for (var p = 0; p < count; ++p) {
      var w = p is 1 or 2 ? (this._width + 1) / 2 : this._width;
      strides[p] = p is 1 or 2 ? codedWidth / 2 : codedWidth;
      padded[p] = new byte[strides[p] * codedHeight];
      for (var y = 0; y < codedHeight; ++y)
        for (var x = 0; x < strides[p]; ++x)
          padded[p][y * strides[p] + x] = planes[p][Math.Min(y, this._height - 1) * w + Math.Min(x, w - 1)];
    }
    return this.Hqa ? this._EncodeHqa(padded, strides) : this._EncodeHq(padded, strides);
  }

  private byte[] _EncodeHq(byte[][] planes, int[] strides) {
    var profile = HqHqaTables.Profiles[this._profile];
    var slices = new List<byte[]>();
    var permutation = profile.Offset;
    var next = 0;
    for (var slice = 0; slice < profile.Slices; ++slice) {
      var start = next;
      next = profile.TableHeight * (slice + 1) / profile.Slices;
      var writer = new HqxBitWriter();
      for (var i = 0; i < (next - start) * profile.TableWidth; ++i) {
        var x = HqHqaTables.Permutations[permutation] * 16;
        var y = HqHqaTables.Permutations[permutation + 1] * 16;
        permutation += 2;
        writer.Write(_GROUP, 4);
        writer.Write(0, 1); // progressive
        (int Plane, int X, int Y)[] layout = [(0, x, y), (0, x + 8, y), (0, x, y + 8), (0, x + 8, y + 8), (2, x >> 1, y), (2, x >> 1, y + 8), (1, x >> 1, y), (1, x >> 1, y + 8)];
        for (var b = 0; b < 8; ++b)
          _Block(writer, planes[layout[b].Plane], layout[b].Y * strides[layout[b].Plane] + layout[b].X, strides[layout[b].Plane], b >= 4, false);
      }
      slices.Add(writer.ToArray());
    }

    var table = 3 * (profile.Slices + 1);
    var packet = new List<byte> { (byte)'U', (byte)'V', (byte)'C', (byte)this._profile };
    var offset = 4 + table;
    for (var s = 0; s <= profile.Slices; ++s) {
      packet.AddRange([(byte)(offset >> 16), (byte)(offset >> 8), (byte)offset]);
      if (s < profile.Slices)
        offset += slices[s].Length;
    }
    foreach (var slice in slices)
      packet.AddRange(slice);
    return [.. packet];
  }

  private byte[] _EncodeHqa(byte[][] planes, int[] strides) {
    const int count = 8;
    var slices = new List<byte[]>();
    for (var slice = 0; slice < count; ++slice) {
      var writer = new HqxBitWriter();
      foreach (var (x, y) in HqShared.HqaSliceOrder(this._width, this._height, slice)) {
        HqxShared.WriteCbp(writer, 0xF); // every block coded
        writer.Write(0, 1); // progressive
        (int Plane, int X, int Y)[] layout = [
          (3, x, y), (3, x + 8, y), (3, x, y + 8), (3, x + 8, y + 8), (0, x, y), (0, x + 8, y), (0, x, y + 8), (0, x + 8, y + 8),
          (2, x >> 1, y), (2, x >> 1, y + 8), (1, x >> 1, y), (1, x >> 1, y + 8),
        ];
        for (var b = 0; b < 12; ++b)
          _Block(writer, planes[layout[b].Plane], layout[b].Y * strides[layout[b].Plane] + layout[b].X, strides[layout[b].Plane], b >= 8, true);
      }
      // FFmpeg stops at a slice of no bytes, so a slice with no macroblock still gets one.
      slices.Add(writer.ToArray() is { Length: > 0 } data ? data : [0]);
    }

    var packet = new List<byte> { (byte)'H', (byte)'Q', (byte)'A', (byte)'1' };
    packet.AddRange([(byte)(this._width >> 8), (byte)this._width, (byte)(this._height >> 8), (byte)this._height, _GROUP, 0, 0, 0]);
    var offset = 4 + 8 + 4 * (count + 1);
    for (var s = 0; s <= count; ++s) {
      packet.AddRange([(byte)(offset >> 24), (byte)(offset >> 16), (byte)(offset >> 8), (byte)offset]);
      if (s < count)
        offset += slices[s].Length;
    }
    foreach (var slice in slices)
      packet.AddRange(slice);
    return [.. packet];
  }

  /// <summary>One block: its DC and matrix choice in the order HQ or HQA reads them, then its levels and the end.</summary>
  private static void _Block(HqxBitWriter writer, byte[] plane, int at, int stride, bool chroma, bool hqa) {
    var coefficients = new double[64];
    for (var k = 0; k < 64; ++k) {
      double dot = 0, norm = 0;
      for (var r = 0; r < 8; ++r)
        for (var c = 0; c < 8; ++c) {
          var b = _Basis[k][r * 8 + c];
          dot += (plane[at + r * stride + c] - 128) * b;
          norm += b * b;
        }
      coefficients[k] = norm > 0 ? dot / norm : 0;
    }

    var dc = Math.Clamp((int)Math.Round(coefficients[0] / 64), -256, 255);
    var select = 0;
    for (; select < 3; ++select) {
      var q = HqShared.Matrix(_GROUP, chroma, select);
      var fits = true;
      for (var z = 1; z < 64 && fits; ++z)
        fits = Math.Abs(coefficients[HqxShared.ZigZag[z]] * 4096 / q[z]) <= 255.5;
      if (fits)
        break;
    }
    if (hqa)
      writer.Write(select, 2);
    writer.Write(dc & 0x1FF, 9);
    if (!hqa)
      writer.Write(select, 2);

    var matrix = HqShared.Matrix(_GROUP, chroma, select);
    var position = 0;
    for (var z = 1; z < 64; ++z) {
      var level = Math.Clamp((int)Math.Round(coefficients[HqxShared.ZigZag[z]] * 4096 / matrix[z]), -255, 255);
      if (level == 0)
        continue;
      var gap = z - position;
      if (!HqShared.Ac.TryWrite(writer, level * 128 + gap)) {
        var zeros = gap - 1;
        while (zeros > 0) {
          var step = zeros;
          while (step > 0 && !HqShared.Ac.TryWrite(writer, step))
            --step;
          zeros -= step;
        }
        while (level != 0 && !HqShared.Ac.TryWrite(writer, level * 128 + 1))
          level -= Math.Sign(level);
        if (level == 0)
          HqShared.Ac.TryWrite(writer, 1);
      }
      position = z;
    }
    HqShared.Ac.TryWrite(writer, _END);
  }
}
