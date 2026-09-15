using System;

namespace FileFormat.Codecs.Dv;

/// <summary>Encodes one SMPTE 370M DV100 video segment.</summary>
/// <remarks>
/// The five macroblocks share 2560 AC bits. DV100 weighs every coefficient, chooses QNO/CNO pairs to
/// make that fixed budget fit, then uses the same three spill passes as standard-definition DV. This
/// is a managed C# implementation of the public SMPTE 370M block syntax, cross-checked against
/// FFmpeg's LGPL-2.1-or-later <c>dvenc.c</c>.
/// </remarks>
internal static class Dv100SegmentEncoder {

  private const int _BlocksPerMacroblock = 8;
  private const int _BlocksPerSegment = DvProfile.MacroblocksPerSegment * _BlocksPerMacroblock;
  private const int _SegmentAcBits = (68 * 6 + 52 * 2) * DvProfile.MacroblocksPerSegment;
  private const int _EndOfBlockBits = 4;
  private const uint _EndOfBlock = 6;
  private const int _QLevelIncrement = 4;

  internal sealed class Block {
    internal readonly short[] Coefficients = new short[64];
    internal readonly short[] Saved = new short[64];
    internal readonly byte[] Next = new byte[64];
    internal readonly byte[] Sign = new byte[64];
    internal int Current;
    internal int CNo;
    internal int MinQLevel;
    internal int BitSize;
    internal int PartialBitCount;
    internal uint PartialBitBuffer;

    internal void Reset() {
      Array.Clear(this.Coefficients);
      Array.Clear(this.Saved);
      Array.Clear(this.Next);
      Array.Clear(this.Sign);
      this.Current = 0;
      this.CNo = 0;
      this.MinQLevel = 1;
      this.BitSize = _EndOfBlockBits;
      this.PartialBitCount = 0;
      this.PartialBitBuffer = 0;
    }
  }

  internal sealed class Scratch {
    internal readonly Block[] Blocks = _NewBlocks();
    internal readonly short[] Transformed = new short[64];
    internal readonly DvBitWriter[] Writers = new DvBitWriter[_BlocksPerSegment];
    internal readonly int[] QLevels = new int[DvProfile.MacroblocksPerSegment];
    internal readonly int[] QNos = new int[DvProfile.MacroblocksPerSegment];
    internal readonly int[] Sizes = new int[DvProfile.MacroblocksPerSegment];

    private static Block[] _NewBlocks() {
      var result = new Block[_BlocksPerSegment];
      for (var i = 0; i < result.Length; ++i)
        result[i] = new();
      return result;
    }
  }

  internal static void Encode(
    byte[] frame, DvProfile profile, in DvGeometry.Segment segment, DvPlanes planes, Scratch scratch) {

    if (!profile.IsDv100)
      throw new ArgumentException("The DV100 segment encoder only accepts SMPTE 370M profiles.", nameof(profile));

    var lumaWeights = profile.Height == 720 ? Dv100Tables.Forward720Luma : Dv100Tables.Forward1080Luma;
    var chromaWeights = profile.Height == 720 ? Dv100Tables.Forward720Chroma : Dv100Tables.Forward1080Chroma;

    for (var macroblock = 0; macroblock < DvProfile.MacroblocksPerSegment; ++macroblock)
      _MeasureMacroblock(frame, profile, segment, macroblock, planes, lumaWeights, chromaWeights, scratch);

    _ChooseQuantisers(scratch);
    _Write(frame, segment, scratch);
  }

  private static void _MeasureMacroblock(
    ReadOnlySpan<byte> frame, DvProfile profile, in DvGeometry.Segment segment, int macroblock,
    DvPlanes planes, int[] lumaWeights, int[] chromaWeights, Scratch scratch) {

    var first = macroblock * _BlocksPerMacroblock;
    var (mbX, mbY) = DvGeometry.MacroblockForFrame(profile, segment, macroblock, frame);
    var lumaOffset = mbY * 8 * planes.Width + mbX * 8;

    if (mbY == 134) {
      for (var b = 0; b < 4; ++b)
        _Measure(scratch.Blocks[first + b], planes.Luma, lumaOffset + b * 8, planes.Width, lumaWeights, scratch.Transformed);

      var chromaOffset = mbY * 8 * planes.ChromaWidth + (mbX >> 1) * 8;
      _Measure(scratch.Blocks[first + 4], planes.Cr, chromaOffset, planes.ChromaWidth, chromaWeights, scratch.Transformed);
      _Measure(scratch.Blocks[first + 5], planes.Cr, chromaOffset + 8, planes.ChromaWidth, chromaWeights, scratch.Transformed);
      _Measure(scratch.Blocks[first + 6], planes.Cb, chromaOffset, planes.ChromaWidth, chromaWeights, scratch.Transformed);
      _Measure(scratch.Blocks[first + 7], planes.Cb, chromaOffset + 8, planes.ChromaWidth, chromaWeights, scratch.Transformed);
      return;
    }

    var yStride = planes.Width * 8;
    _Measure(scratch.Blocks[first + 0], planes.Luma, lumaOffset, planes.Width, lumaWeights, scratch.Transformed);
    _Measure(scratch.Blocks[first + 1], planes.Luma, lumaOffset + 8, planes.Width, lumaWeights, scratch.Transformed);
    _Measure(scratch.Blocks[first + 2], planes.Luma, lumaOffset + yStride, planes.Width, lumaWeights, scratch.Transformed);
    _Measure(scratch.Blocks[first + 3], planes.Luma, lumaOffset + yStride + 8, planes.Width, lumaWeights, scratch.Transformed);

    var chromaOffsetNormal = mbY * 8 * planes.ChromaWidth + (mbX >> 1) * 8;
    var chromaYStride = planes.ChromaWidth * 8;
    _Measure(scratch.Blocks[first + 4], planes.Cr, chromaOffsetNormal, planes.ChromaWidth, chromaWeights, scratch.Transformed);
    _Measure(scratch.Blocks[first + 5], planes.Cr, chromaOffsetNormal + chromaYStride, planes.ChromaWidth, chromaWeights, scratch.Transformed);
    _Measure(scratch.Blocks[first + 6], planes.Cb, chromaOffsetNormal, planes.ChromaWidth, chromaWeights, scratch.Transformed);
    _Measure(scratch.Blocks[first + 7], planes.Cb, chromaOffsetNormal + chromaYStride, planes.ChromaWidth, chromaWeights, scratch.Transformed);
  }

  private static void _Measure(
    Block block, byte[] plane, int offset, int stride, int[] weights, short[] transformed) {

    block.Reset();
    for (var row = 0; row < 8; ++row)
      for (var column = 0; column < 8; ++column)
        transformed[row * 8 + column] = plane[offset + row * stride + column];

    DvForwardDct.Transform(transformed);
    block.Coefficients[0] = transformed[0];

    var largest = 0;
    for (var i = 0; i < 64; ++i) {
      var value = transformed[DvTables.Zigzag[i]];
      block.Sign[i] = (byte)(value < 0 ? 1 : 0);
      var magnitude = Math.Abs((int)value);
      var weighted = (magnitude * weights[i] + 4096 + (1 << 17)) >> 18;
      block.Saved[i] = (short)weighted;
      largest = Math.Max(largest, weighted);
    }

    block.MinQLevel = Math.Max(1, (largest + 256) >> 8);
  }

  private static void _ChooseQuantisers(Scratch scratch) {
    var qlevels = scratch.QLevels;
    var qnos = scratch.QNos;
    var sizes = scratch.Sizes;
    var blocks = scratch.Blocks;

    for (var macroblock = 0; macroblock < DvProfile.MacroblocksPerSegment; ++macroblock) {
      var minimum = 1;
      for (var b = 0; b < _BlocksPerMacroblock; ++b)
        minimum = Math.Max(minimum, blocks[macroblock * _BlocksPerMacroblock + b].MinQLevel);

      qlevels[macroblock] = Math.Min(minimum, Dv100Tables.QuantisationLevels.Length - 1);
      sizes[macroblock] = _QuantiseMacroblock(blocks, macroblock, qlevels[macroblock]);
      qnos[macroblock] = Dv100Tables.QuantisationLevels[qlevels[macroblock]].QNo;
    }

    if (_Total(sizes) <= _SegmentAcBits)
      return;

    var candidate = sizes[0] % DvProfile.MacroblocksPerSegment;
    var exhausted = 0;
    while (_Total(sizes) > _SegmentAcBits && exhausted < DvProfile.MacroblocksPerSegment) {
      for (var i = 0; i < DvProfile.MacroblocksPerSegment; ++i)
        if (qlevels[i] < qlevels[candidate])
          candidate = i;

      var selected = candidate;
      candidate = (candidate + 1) % DvProfile.MacroblocksPerSegment;
      var next = qlevels[selected] + _QLevelIncrement;
      if (next >= Dv100Tables.QuantisationLevels.Length) {
        next = Dv100Tables.QuantisationLevels.Length - 1;
        ++exhausted;
      }

      qlevels[selected] = next;
      sizes[selected] = _QuantiseMacroblock(blocks, selected, next);
      qnos[selected] = Dv100Tables.QuantisationLevels[next].QNo;
    }
  }

  private static int _QuantiseMacroblock(Block[] blocks, int macroblock, int qlevel) {
    var size = 0;
    for (var b = 0; b < _BlocksPerMacroblock; ++b)
      size += _Quantise(blocks[macroblock * _BlocksPerMacroblock + b], qlevel);
    return size;
  }

  private static int _Quantise(Block block, int qlevel) {
    var (qno, cno) = Dv100Tables.QuantisationLevels[qlevel];
    var quantum = Dv100Tables.QuantisationSteps[qno];
    block.CNo = cno;
    block.BitSize = _EndOfBlockBits;
    block.Current = 0;
    block.PartialBitCount = 0;
    block.PartialBitBuffer = 0;
    Array.Clear(block.Coefficients, 1, 63);
    Array.Clear(block.Next);

    var previous = 0;
    for (var coefficient = 1; coefficient < 64; ++coefficient) {
      var level = (block.Saved[coefficient] + quantum / 2) / quantum;
      level >>= cno;
      if (level == 0)
        continue;
      level = Math.Min(level, 255);
      block.Coefficients[coefficient] = (short)level;
      block.BitSize += DvVlc.EncodeSize(coefficient - previous - 1, level);
      block.Next[previous] = (byte)coefficient;
      previous = coefficient;
    }

    block.Next[previous] = 64;
    return block.BitSize;
  }

  private static int _Total(int[] sizes) {
    var total = 0;
    foreach (var size in sizes)
      total += size;
    return total;
  }

  private static void _Write(byte[] frame, in DvGeometry.Segment segment, Scratch scratch) {
    var blocks = scratch.Blocks;
    var writers = scratch.Writers;
    var position = segment.BlockOffset * DvProfile.DifBlockSize;

    for (var macroblock = 0; macroblock < DvProfile.MacroblocksPerSegment; ++macroblock) {
      var first = macroblock * _BlocksPerMacroblock;
      frame[position + 3] = (byte)scratch.QNos[macroblock];
      position += 4;

      for (var b = 0; b < _BlocksPerMacroblock; ++b) {
        var bytes = DvProfile.Dv100BlockSizes[b] >> 3;
        var writer = new DvBitWriter(frame, position, bytes);
        writers[first + b] = writer;
        position += bytes;

        var block = blocks[first + b];
        writer.Put(9, unchecked((uint)(((block.Coefficients[0] >> 3) - 1024 + 2) >> 2)));
        writer.Put(1, b == 0 ? 0u : 1u); // frame-mode macroblock; DV100 marks the remaining cells as field cells.
        writer.Put(2, (uint)block.CNo);
        _WriteCoefficients(block, writers, first + b, first + b + 1);
      }

      var pool = first;
      for (var b = 0; b < _BlocksPerMacroblock; ++b)
        if (blocks[first + b].PartialBitCount != 0)
          pool = _WriteCoefficients(blocks[first + b], writers, pool, first + _BlocksPerMacroblock);
    }

    var segmentPool = 0;
    for (var b = 0; b < _BlocksPerSegment; ++b)
      if (blocks[b].PartialBitCount != 0)
        segmentPool = _WriteCoefficients(blocks[b], writers, segmentPool, _BlocksPerSegment);

    for (var b = 0; b < _BlocksPerSegment; ++b)
      writers[b].PadRemaining(0xff);
  }

  private static int _WriteCoefficients(Block block, DvBitWriter[] writers, int pool, int end) {
    var size = block.PartialBitCount;
    var code = block.PartialBitBuffer;
    block.PartialBitCount = 0;
    block.PartialBitBuffer = 0;

    for (;;) {
      int left;
      while (size > (left = writers[pool].BitsLeft)) {
        if (left > 0) {
          size -= left;
          writers[pool].Put(left, code >> size);
          code = size < 32 ? code & ((1u << size) - 1) : code;
        }

        if (pool + 1 >= end) {
          block.PartialBitCount = size;
          block.PartialBitBuffer = code;
          return pool;
        }
        ++pool;
      }

      writers[pool].Put(size, code);
      if (block.Current >= 64)
        return pool;

      var previous = block.Current;
      block.Current = block.Next[previous];
      if (block.Current < 64) {
        var coefficient = block.Coefficients[block.Current];
        size = DvVlc.EncodeSize(block.Current - previous - 1, coefficient);
        code = DvVlc.EncodeBits(block.Current - previous - 1, coefficient) | block.Sign[block.Current];
      } else {
        size = _EndOfBlockBits;
        code = _EndOfBlock;
      }
    }
  }
}
