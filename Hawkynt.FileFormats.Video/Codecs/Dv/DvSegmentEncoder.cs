using System;

namespace FileFormat.Codecs.Dv;

/// <summary>
/// Codes one video segment: transforms its thirty blocks, chooses a quantiser that makes them fit,
/// and writes them into the fixed space the segment has on tape.
/// </summary>
/// <remarks>
/// <b>The bit budget is the whole problem.</b> Five macroblocks share 2680 bits of AC coefficients
/// and not one bit more, because a DV frame is a fixed length whatever is in it. So the encoder does
/// not choose a quality and see what it costs; it measures what each block would cost, and if the
/// five together overrun, quantises the largest ones harder and measures again until they fit. That
/// search is why every block is coded twice over — once to size it, once to write it.
/// <para/>
/// <b>Three passes again, for the same reason as the decoder.</b> A block writes into its own
/// allowance until that is full, then into what its neighbours in the macroblock left, then into what
/// the segment left. A codeword that will not fit in the space remaining is split across the
/// boundary, so a block carries the tail of a codeword forward into the next pass.
/// <para/>
/// Converted from FFmpeg's <c>dv_encode_video_segment</c>, <c>dv_set_class_number_sd</c>,
/// <c>dv_guess_qnos</c> and <c>dv_encode_ac</c> in <c>libavcodec/dvenc.c</c>. The class-number
/// assignment is FFmpeg's improved one rather than SMPTE 314M Table 22's: the standard's method sends
/// every block whose largest coefficient exceeds 36 to the coarsest class, which throws away quality
/// an encoder that tracks its own bit consumption does not need to throw away.
/// </remarks>
internal static class DvSegmentEncoder {

  /// <summary>The AC bits five macroblocks of a standard-definition segment have between them.</summary>
  /// <remarks>
  /// A luma block is allotted 112 bits and a colour block 80, of which twelve go to the first
  /// coefficient, the transform mode and the class number: 100 and 68 respectively, four luma blocks
  /// and two colour blocks to a macroblock, five macroblocks to a segment.
  /// </remarks>
  private const int _SEGMENT_AC_BITS = (100 * 4 + 68 * 2) * DvProfile.MacroblocksPerSegment;

  /// <summary>The blocks a whole video segment holds.</summary>
  private const int _BLOCKS = DvProfile.MacroblocksPerSegment * DvProfile.BlocksPerMacroblock;

  /// <summary>The coarsest quantisation number, where the search starts.</summary>
  private const int _COARSEST_QUANTISER = 15;

  /// <summary>The end-of-block stamp and its length.</summary>
  private const uint _END_OF_BLOCK = 6;

  private const int _END_OF_BLOCK_BITS = 4;

  /// <summary>
  /// A coefficient smaller than this in magnitude is dropped before it is weighted.
  /// </summary>
  /// <remarks>
  /// FFmpeg's own default. A dead zone costs a little accuracy on quiet blocks and buys bits that go
  /// to the blocks which need them, which at a bit rate nobody can exceed is the only currency there
  /// is.
  /// </remarks>
  private const int _DEAD_ZONE = 7;

  /// <summary>One block as the encoder holds it between sizing and writing.</summary>
  internal sealed class Block {

    /// <summary>The weighted, quantised coefficients in scan order; the first is the untouched DC.</summary>
    internal readonly short[] Coefficients = new short[64];

    /// <summary>A linked list over the non-zero coefficients: <c>Next[i]</c> is the one after <c>i</c>.</summary>
    internal readonly byte[] Next = new byte[64];

    /// <summary>One bit a coefficient, set where it is negative.</summary>
    internal readonly byte[] Sign = new byte[64];

    /// <summary>How coarsely each of the four areas has already been quantised.</summary>
    internal readonly int[] AreaQuantiser = new int[4];

    /// <summary>What each area costs to write, including its end-of-block bit.</summary>
    internal readonly int[] AreaBits = new int[4];

    /// <summary>The last coefficient before each area, which is where its runs are measured from.</summary>
    internal readonly int[] AreaPrevious = new int[5];

    /// <summary>The coefficient the writer has reached.</summary>
    internal int Current;

    /// <summary>The class number, which shifts the quantiser and is written into the block header.</summary>
    internal int ClassNumber;

    /// <summary>The tail of a codeword that did not fit in the space it was being written into.</summary>
    internal int PartialBitCount;

    internal uint PartialBitBuffer;

    internal int TotalBits => this.AreaBits[0] + this.AreaBits[1] + this.AreaBits[2] + this.AreaBits[3];

    internal void Reset() {
      Array.Clear(this.Coefficients);
      Array.Clear(this.Next);
      Array.Clear(this.Sign);
      Array.Clear(this.AreaQuantiser);
      Array.Clear(this.AreaBits);
      Array.Clear(this.AreaPrevious);
      this.Current = 0;
      this.ClassNumber = 0;
      this.PartialBitCount = 0;
      this.PartialBitBuffer = 0;
    }
  }

  /// <summary>Everything a segment encode scribbles on, kept across segments so it is allocated once.</summary>
  internal sealed class Scratch {

    internal readonly Block[] Blocks = _NewBlocks();
    internal readonly short[] Transformed = new short[64];
    internal readonly byte[] FoldedChroma = new byte[8 * 16];
    internal readonly DvBitWriter[] Writers = new DvBitWriter[_BLOCKS];
    internal readonly int[] Quantisers = new int[DvProfile.MacroblocksPerSegment];

    private static Block[] _NewBlocks() {
      var blocks = new Block[_BLOCKS];
      for (var i = 0; i < blocks.Length; ++i)
        blocks[i] = new();

      return blocks;
    }
  }

  /// <summary>Codes one segment of a picture into the frame buffer.</summary>
  internal static void Encode(byte[] frame, DvProfile profile, in DvGeometry.Segment segment, DvPlanes planes, Scratch scratch) {
    var blocks = scratch.Blocks;
    var total = 0;

    for (var macroblock = 0; macroblock < DvProfile.MacroblocksPerSegment; ++macroblock) {
      scratch.Quantisers[macroblock] = _COARSEST_QUANTISER;
      total += _MeasureMacroblock(profile, segment, macroblock, planes, scratch);
    }

    if (total > _SEGMENT_AC_BITS)
      _ChooseQuantisers(blocks, scratch.Quantisers);

    _Write(frame, segment, scratch);
  }

  // ==============================================================================================
  // Measuring
  // ==============================================================================================

  /// <summary>Transforms and sizes the six blocks of one macroblock.</summary>
  private static int _MeasureMacroblock(
    DvProfile profile, in DvGeometry.Segment segment, int macroblock, DvPlanes planes, Scratch scratch) {

    var first = macroblock * DvProfile.BlocksPerMacroblock;
    var mbX = segment.MacroblockX[macroblock];
    var mbY = segment.MacroblockY[macroblock];
    var lumaStride = planes.Width;

    var folded = profile.Sampling == DvSampling.FourOneOne && mbX >= 704 / 8;
    var lumaStep = profile.Sampling == DvSampling.FourTwoZero || folded ? lumaStride * 8 : 16;
    var lumaOffset = mbY * 8 * lumaStride + mbX * 8;
    var total = 0;

    if (profile.Sampling == DvSampling.FourTwoTwo) {
      // Only two of the four luma blocks carry picture; the other two are written empty, which the
      // code table turns into an immediate end-of-block, and the decoder skips them.
      total += _Measure(scratch, first, planes.Luma, lumaOffset, lumaStride, chroma: false);
      total += _MeasureEmpty(scratch, first + 1);
      total += _Measure(scratch, first + 2, planes.Luma, lumaOffset + 8, lumaStride, chroma: false);
      total += _MeasureEmpty(scratch, first + 3);
    } else {
      total += _Measure(scratch, first, planes.Luma, lumaOffset, lumaStride, chroma: false);
      total += _Measure(scratch, first + 1, planes.Luma, lumaOffset + 8, lumaStride, chroma: false);
      total += _Measure(scratch, first + 2, planes.Luma, lumaOffset + lumaStep, lumaStride, chroma: false);
      total += _Measure(scratch, first + 3, planes.Luma, lumaOffset + 8 + lumaStep, lumaStride, chroma: false);
    }

    var chromaStride = planes.ChromaWidth;
    var chromaOffset =
      ((profile.Sampling == DvSampling.FourTwoZero ? mbY >> 1 : mbY) * chromaStride
       + (profile.Sampling == DvSampling.FourOneOne ? mbX >> 2 : mbX >> 1)) * 8;

    // Red difference first: DV writes the two colour blocks in the order Cr, Cb.
    for (var component = 0; component < 2; ++component) {
      var plane = component == 0 ? planes.Cr : planes.Cb;
      var block = first + 4 + component;

      if (!folded) {
        total += _Measure(scratch, block, plane, chromaOffset, chromaStride, chroma: true);
        continue;
      }

      // The last colour block of a 4:1:1 raster is two four-sample-wide halves, sixteen lines apart,
      // packed side by side into one square block.
      for (var row = 0; row < 8; ++row)
        for (var column = 0; column < 4; ++column) {
          scratch.FoldedChroma[row * 16 + column] = plane[chromaOffset + row * chromaStride + column];
          scratch.FoldedChroma[row * 16 + 4 + column] = plane[chromaOffset + (row + 8) * chromaStride + column];
        }

      total += _Measure(scratch, block, scratch.FoldedChroma, 0, 16, chroma: true);
    }

    return total;
  }

  /// <summary>Transforms one 8x8 block out of a plane and sizes what it would cost to write.</summary>
  private static int _Measure(Scratch scratch, int index, byte[] plane, int offset, int stride, bool chroma) {
    var transformed = scratch.Transformed;
    for (var row = 0; row < 8; ++row)
      for (var column = 0; column < 8; ++column)
        transformed[row * 8 + column] = plane[offset + row * stride + column];

    DvForwardDct.Transform(transformed);
    return _Classify(scratch.Blocks[index], transformed, chroma);
  }

  /// <summary>Sizes an empty block — the placeholder 4:2:2 writes where its other two luma blocks would be.</summary>
  private static int _MeasureEmpty(Scratch scratch, int index) {
    var transformed = scratch.Transformed;
    Array.Clear(transformed);
    return _Classify(scratch.Blocks[index], transformed, chroma: false);
  }

  /// <summary>
  /// Weights a transformed block, picks its class number and works out what writing it would cost.
  /// </summary>
  /// <remarks>
  /// The class number is a second quantiser the block header carries, and choosing it is the one
  /// judgement in the whole encoder. SMPTE 314M Table 22 assigns class 3 — the coarsest — to any block
  /// whose largest coefficient exceeds 36, which is safe for an encoder that cannot predict its own
  /// bit consumption and wasteful for one that can. This assigns class 2 to almost everything and
  /// reaches for class 3 only where a coefficient exceeds 255 and would otherwise not be writable at
  /// all; the rate is then made to fit by the quantiser search rather than by pessimism here.
  /// </remarks>
  private static int _Classify(Block block, ReadOnlySpan<short> transformed, bool chroma) {
    block.Reset();
    block.Coefficients[0] = transformed[0];

    var largest = -1;
    var previous = 0;
    var coefficient = 0;

    for (var area = 0; area < 4; ++area) {
      block.AreaPrevious[area] = previous;
      block.AreaBits[area] = 1; // a quarter of the four-bit end-of-block stamp
      for (coefficient = DvTables.AreaStarts[area]; coefficient < DvTables.AreaStarts[area + 1]; ++coefficient) {
        int value = transformed[DvTables.Zigzag[coefficient]];
        if (Math.Abs(value) <= _DEAD_ZONE)
          continue;

        block.Sign[coefficient] = (byte)(value < 0 ? 1 : 0);

        // The extra division by sixteen undoes both the transform's eightfold expansion and the
        // doubling the weights carry.
        var weighted = (int)(((long)Math.Abs(value) * DvTables.ForwardWeights88[coefficient]
                              + (1L << (DvTables.ForwardWeightBits + 3)))
                             >> (DvTables.ForwardWeightBits + 4));
        if (weighted == 0)
          continue;

        block.Coefficients[coefficient] = (short)weighted;
        if (weighted > largest)
          largest = weighted;

        block.AreaBits[area] += DvVlc.EncodeSize(coefficient - previous - 1, weighted);
        block.Next[previous] = (byte)coefficient;
        previous = coefficient;
      }
    }

    block.Next[previous] = (byte)coefficient;
    block.ClassNumber = largest < 0 ? 0 : largest > 255 ? 3 : 2;
    block.ClassNumber += chroma ? 1 : 0;

    if (block.ClassNumber >= 3)
      _Halve(block);

    return block.TotalBits;
  }

  /// <summary>Halves every coefficient of a block, which is what class 3 means.</summary>
  /// <remarks>
  /// The decoder doubles the de-quantisation factor for class 3, so halving here is not a loss on top
  /// of the class — it is the class. Coefficients that reach zero drop out of the list, which is
  /// where most of the bits saved actually come from.
  /// </remarks>
  private static void _Halve(Block block) {
    block.ClassNumber = 3;
    var previous = 0;
    var coefficient = block.Next[0];

    for (var area = 0; area < 4; ++area) {
      block.AreaPrevious[area] = previous;
      block.AreaBits[area] = 1;
      for (; coefficient < DvTables.AreaStarts[area + 1]; coefficient = block.Next[coefficient]) {
        block.Coefficients[coefficient] >>= 1;
        if (block.Coefficients[coefficient] == 0)
          continue;

        block.AreaBits[area] += DvVlc.EncodeSize(coefficient - previous - 1, block.Coefficients[coefficient]);
        block.Next[previous] = (byte)coefficient;
        previous = coefficient;
      }
    }

    block.Next[previous] = (byte)coefficient;
  }

  // ==============================================================================================
  // Fitting the segment into its allowance
  // ==============================================================================================

  /// <summary>
  /// Quantises the segment coarsely enough to fit, one step at a time, and stops the moment it does.
  /// </summary>
  /// <remarks>
  /// The quantisation number counts down from the coarsest, and each step that actually changes a
  /// shift halves the affected area's coefficients again. Coefficients reaching zero are spliced out
  /// of the list, and the splice has to repair the run of whichever later coefficient followed them —
  /// that bookkeeping is what keeps the size measurement exact instead of approximate, and an exact
  /// measurement is what lets the search stop at the first quantiser that fits rather than the first
  /// that obviously fits.
  /// <para/>
  /// If every quantiser has been spent and the segment still does not fit, coefficients are dropped
  /// outright by magnitude, doubling the threshold until it does. A DV frame has to be written.
  /// </remarks>
  private static void _ChooseQuantisers(Block[] blocks, int[] quantisers) {
    var sizes = new int[DvProfile.MacroblocksPerSegment];
    Array.Fill(sizes, 1 << 24);

    do {
      for (var macroblock = 0; macroblock < DvProfile.MacroblocksPerSegment; ++macroblock) {
        if (quantisers[macroblock] == 0)
          continue;

        --quantisers[macroblock];
        sizes[macroblock] = 0;

        for (var b = 0; b < DvProfile.BlocksPerMacroblock; ++b) {
          var block = blocks[macroblock * DvProfile.BlocksPerMacroblock + b];
          var row = quantisers[macroblock] + DvTables.QuantOffsets[block.ClassNumber];

          for (var area = 0; area < 4; ++area) {
            if (block.AreaQuantiser[area] != DvTables.QuantShifts[row][area])
              _CoarsenArea(block, area);

            sizes[macroblock] += block.AreaBits[area];
          }
        }

        var total = 0;
        foreach (var size in sizes)
          total += size;

        if (total <= _SEGMENT_AC_BITS)
          return;
      }
    } while (quantisers[0] != 0 || quantisers[1] != 0 || quantisers[2] != 0 || quantisers[3] != 0 || quantisers[4] != 0);

    for (var threshold = 2; ; threshold += threshold) {
      var total = _BLOCKS * _END_OF_BLOCK_BITS;

      foreach (var block in blocks) {
        var previous = block.AreaPrevious[0];
        for (var coefficient = (int)block.Next[previous]; coefficient < 64; coefficient = block.Next[coefficient])
          if (block.Coefficients[coefficient] < threshold && block.Coefficients[coefficient] > -threshold) {
            block.Next[previous] = block.Next[coefficient];
          } else {
            total += DvVlc.EncodeSize(coefficient - previous - 1, block.Coefficients[coefficient]);
            previous = coefficient;
          }
      }

      if (total <= _SEGMENT_AC_BITS)
        return;
    }
  }

  /// <summary>Halves one area of a block again and re-measures it, splicing out what reached zero.</summary>
  private static void _CoarsenArea(Block block, int area) {
    block.AreaBits[area] = 1;
    ++block.AreaQuantiser[area];

    var previous = block.AreaPrevious[area];
    var end = DvTables.AreaStarts[area + 1];

    for (var coefficient = (int)block.Next[previous]; coefficient < end; coefficient = block.Next[coefficient]) {
      block.Coefficients[coefficient] >>= 1;
      if (block.Coefficients[coefficient] != 0) {
        block.AreaBits[area] += DvVlc.EncodeSize(coefficient - previous - 1, block.Coefficients[coefficient]);
        previous = coefficient;
        continue;
      }

      // The coefficient has gone. Whatever follows it now has a longer run in front of it, and if
      // that successor lives in a later area then it is that area's cost — and that area's starting
      // point — which has to be corrected.
      var successor = (int)block.Next[coefficient];
      if (successor >= end && successor < 64) {
        var later = area + 1;
        for (; successor >= DvTables.AreaStarts[later + 1]; ++later)
          block.AreaPrevious[later] = previous;

        block.AreaBits[later] +=
          DvVlc.EncodeSize(successor - previous - 1, block.Coefficients[successor])
          - DvVlc.EncodeSize(successor - coefficient - 1, block.Coefficients[successor]);
        block.AreaPrevious[later] = previous;
      }

      block.Next[previous] = (byte)successor;
    }

    block.AreaPrevious[area + 1] = previous;
  }

  // ==============================================================================================
  // Writing
  // ==============================================================================================

  /// <summary>Writes the segment's thirty blocks into the frame, spilling into shared space as needed.</summary>
  private static void _Write(byte[] frame, in DvGeometry.Segment segment, Scratch scratch) {
    var blocks = scratch.Blocks;
    var writers = scratch.Writers;
    var position = segment.BlockOffset * DvProfile.DifBlockSize;

    for (var macroblock = 0; macroblock < DvProfile.MacroblocksPerSegment; ++macroblock) {
      var first = macroblock * DvProfile.BlocksPerMacroblock;

      // The macroblock header: an all-clear status nibble and the quantisation number.
      frame[position + 3] = (byte)scratch.Quantisers[macroblock];
      position += 4;

      for (var b = 0; b < DvProfile.BlocksPerMacroblock; ++b) {
        var bytes = DvProfile.BlockSizes[b] >> 3;
        var writer = new DvBitWriter(frame, position, bytes);
        writers[first + b] = writer;
        position += bytes;

        var block = blocks[first + b];
        writer.Put(9, (uint)(((block.Coefficients[0] >> 3) - 1024 + 2) >> 2));
        writer.Put(1, 0); // frame-mode transform; the 2-4-8 mode is not written here
        writer.Put(2, (uint)block.ClassNumber);

        _WriteCoefficients(block, writers, first + b, first + b + 1);
      }

      // Second pass: whatever did not fit goes into what this macroblock's other blocks left.
      var pool = first;
      for (var b = 0; b < DvProfile.BlocksPerMacroblock; ++b)
        if (blocks[first + b].PartialBitCount != 0)
          pool = _WriteCoefficients(blocks[first + b], writers, pool, first + DvProfile.BlocksPerMacroblock);
    }

    // Third pass: what is still left goes into what the whole segment left.
    var segmentPool = 0;
    for (var b = 0; b < _BLOCKS; ++b)
      if (blocks[b].PartialBitCount != 0)
        segmentPool = _WriteCoefficients(blocks[b], writers, segmentPool, _BLOCKS);

    // DV pads unused space with 0xff, which the code table reads as the longest escape there is and
    // which a decoder therefore never mistakes for a coefficient of a block that had already ended.
    for (var b = 0; b < _BLOCKS; ++b)
      writers[b].PadRemaining(0xff);
  }

  /// <summary>
  /// Writes a block's coefficients into a chain of spaces, stopping when the chain runs out.
  /// </summary>
  /// <remarks>
  /// A codeword that will not fit is written as far as it goes and the rest carried forward, because
  /// the spaces are contiguous on tape even though they belong to different blocks — a decoder
  /// reading them back concatenates them in exactly this order.
  /// </remarks>
  /// <returns>The space the next block should carry on from.</returns>
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
        size = _END_OF_BLOCK_BITS;
        code = _END_OF_BLOCK;
      }
    }
  }
}
