using System;
using System.IO;

namespace FileFormat.Codecs.Dv;

/// <summary>
/// Decodes one video segment: five macroblocks, thirty blocks, and the three passes it takes to read
/// coefficients that were allowed to spill out of the space they were written in.
/// </summary>
/// <remarks>
/// <b>Why three passes.</b> Every block has a fixed bit budget, and a block that needs more takes the
/// space its neighbours in the same macroblock did not use; a macroblock that still needs more takes
/// what the other four macroblocks of the segment left. So a block cannot be finished in isolation:
/// pass one reads each block as far as its own budget goes and gathers what every finished block left
/// over, pass two lets the unfinished blocks of a macroblock consume that, and pass three does the
/// same at the segment level. A codeword may be cut in half by a boundary, which is why an unfinished
/// block carries its dangling bits with it into the next pass.
/// <para/>
/// <b>Why a segment is the unit.</b> Five macroblocks share a bit pool and nothing crosses a segment
/// boundary, so segments are independent of each other — that is what makes a DV frame decodable in
/// any order and, on a recorder, recoverable when a segment is lost.
/// <para/>
/// Ported from FFmpeg's <c>dv_decode_video_segment</c> in <c>libavcodec/dvdec.c</c>. Its error
/// concealment is not carried across: a damaged frame here decodes to what its bits actually say
/// rather than to the repaired picture FFmpeg builds, so the two agree on every well-formed frame and
/// may differ on a broken one.
/// </remarks>
internal static class DvSegmentDecoder {

  /// <summary>The blocks a whole video segment holds.</summary>
  private const int _BLOCKS = DvProfile.MacroblocksPerSegment * DvProfile.BlocksPerMacroblock;

  /// <summary>The bytes a macroblock's own overflow space can hold — its six blocks' budgets.</summary>
  private const int _MACROBLOCK_OVERFLOW_BYTES = 80;

  /// <summary>The bytes a segment's overflow space can hold.</summary>
  private const int _SEGMENT_OVERFLOW_BYTES = 80 * DvProfile.MacroblocksPerSegment;

  /// <summary>What is known about one block between the passes.</summary>
  private struct BlockState {

    /// <summary>The last coefficient position reached; sixty-four or more means finished.</summary>
    internal int Position;

    /// <summary>How many bits of an unfinished codeword are being carried forward.</summary>
    internal int PartialBitCount;

    /// <summary>Those bits, left-aligned in the word.</summary>
    internal uint PartialBitBuffer;

    /// <summary>Where in the de-quantisation table this block's coefficients are scaled from.</summary>
    internal int FactorBase;

    /// <summary>Whether the block was coded with the 2-4-8 transform.</summary>
    internal bool FieldCoded;
  }

  /// <summary>Everything a segment decode needs to scribble on, kept across segments so it is allocated once.</summary>
  internal sealed class Scratch {

    internal readonly short[] Coefficients = new short[_BLOCKS * 64];
    internal readonly byte[] MacroblockOverflow = new byte[_MACROBLOCK_OVERFLOW_BYTES];
    internal readonly byte[] SegmentOverflow = new byte[_SEGMENT_OVERFLOW_BYTES];
    internal readonly byte[] ChromaScratch = new byte[64];
  }

  /// <summary>
  /// Builds the de-quantisation table: every coefficient's weight, for every quantisation number,
  /// both transforms and both class ranges.
  /// </summary>
  /// <remarks>
  /// Folded into one table because the three things that scale a coefficient — the weighting matrix,
  /// the quantisation number and the class number — all reduce to one multiplier per coefficient
  /// position, and DV has few enough combinations that all of them fit in five and a half thousand
  /// integers. The upper half is the lower half doubled, which is what class 3 amounts to.
  /// </remarks>
  internal static int[] BuildFactors() {
    var factors = new int[2 * 2 * 22 * 64];
    var plain = 0;
    var doubled = 2 * 22 * 64;

    for (var transform = 0; transform < 2; ++transform) {
      var weights = transform == 0 ? DvTables.InverseWeights88 : DvTables.InverseWeights248;
      for (var quantiser = 0; quantiser < 22; ++quantiser) {
        var coefficient = 0;
        for (var area = 0; area < 4; ++area)
          for (; coefficient < DvTables.AreaBoundaries[area]; ++coefficient) {
            factors[plain] = weights[coefficient] << (DvTables.QuantShifts[quantiser][area] + 1);
            factors[doubled++] = factors[plain++] << 1;
          }
      }
    }

    return factors;
  }

  /// <summary>Decodes one segment into the frame's planes.</summary>
  internal static void Decode(
    ReadOnlySpan<byte> frame, DvProfile profile, in DvGeometry.Segment segment, int[] factors,
    DvPlanes planes, Scratch scratch) {

    var states = new BlockState[_BLOCKS];
    var coefficients = scratch.Coefficients;
    Array.Clear(coefficients);

    var segmentOverflow = new DvBitWriter(scratch.SegmentOverflow, 0, _SEGMENT_OVERFLOW_BYTES);
    var position = segment.BlockOffset * DvProfile.DifBlockSize;

    for (var macroblock = 0; macroblock < DvProfile.MacroblocksPerSegment; ++macroblock) {
      var first = macroblock * DvProfile.BlocksPerMacroblock;
      if (position + 4 > frame.Length)
        throw new InvalidDataException("A DV video segment runs past the end of the frame.");

      // The macroblock header is one byte of status and quantisation number; the other three are the
      // DIF block's own identifier, which the segment walk has already accounted for.
      var quantiser = frame[position + 3] & 0x0f;
      position += 4;

      var macroblockOverflow = new DvBitWriter(scratch.MacroblockOverflow, 0, _MACROBLOCK_OVERFLOW_BYTES);

      for (var b = 0; b < DvProfile.BlocksPerMacroblock; ++b) {
        var budget = DvProfile.BlockSizes[b];
        var bytes = budget >> 3;
        if (position + bytes > frame.Length)
          throw new InvalidDataException("A DV block runs past the end of the frame.");

        var reader = new DvBitReader(frame.Slice(position, bytes), budget);
        position += bytes;

        var index = 0;
        var dc = reader.ReadSigned(ref index, 9);
        var fieldCoded = reader.ReadBits(ref index, 1) != 0;
        var classNumber = (int)reader.ReadBits(ref index, 2);

        ref var state = ref states[first + b];
        state.FieldCoded = fieldCoded;
        state.FactorBase =
          (classNumber == 3 ? 2 * 22 * 64 : 0)
          + (fieldCoded ? 22 * 64 : 0)
          + (quantiser + DvTables.QuantOffsets[classNumber]) * 64;

        // The first coefficient is coded at a quarter of its weight and without the 128 the transform
        // does not add, so it is scaled up and re-centred here rather than in the transform.
        coefficients[(first + b) * 64] = (short)(dc * 4 + 1024);

        index = _DecodeCoefficients(reader, ref state, coefficients, (first + b) * 64, factors, index);

        // Only a finished block leaves anything for its neighbours; an unfinished one has consumed
        // every bit it was given.
        if (state.Position >= 64)
          macroblockOverflow.CopyFrom(reader, index);
      }

      // Pass two: the macroblock's unfinished blocks, in order, out of what its finished ones left.
      var spare = new DvBitReader(scratch.MacroblockOverflow.AsSpan(0, (macroblockOverflow.BitCount + 7) / 8), macroblockOverflow.BitCount);
      var spareIndex = 0;
      var b2 = 0;
      for (; b2 < DvProfile.BlocksPerMacroblock; ++b2) {
        ref var state = ref states[first + b2];
        if (state.Position >= 64 || spare.BitLimit - spareIndex <= 0)
          continue;

        spareIndex = _DecodeCoefficients(spare, ref state, coefficients, (first + b2) * 64, factors, spareIndex);

        // A block that is still unfinished has taken everything there was, so no later block can
        // start: whatever follows in this buffer belongs to this block's next pass.
        if (state.Position < 64)
          break;
      }

      if (b2 >= DvProfile.BlocksPerMacroblock)
        segmentOverflow.CopyFrom(spare, spareIndex);
    }

    // Pass three: whatever is still unfinished, out of what the whole segment left.
    var pool = new DvBitReader(scratch.SegmentOverflow.AsSpan(0, (segmentOverflow.BitCount + 7) / 8), segmentOverflow.BitCount);
    var poolIndex = 0;
    for (var b = 0; b < _BLOCKS; ++b) {
      ref var state = ref states[b];
      if (state.Position >= 64 || pool.BitLimit - poolIndex <= 0)
        continue;

      poolIndex = _DecodeCoefficients(pool, ref state, coefficients, b * 64, factors, poolIndex);
    }

    for (var macroblock = 0; macroblock < DvProfile.MacroblocksPerSegment; ++macroblock)
      _Place(profile, segment, macroblock, coefficients, states, planes, scratch.ChromaScratch);
  }

  /// <summary>
  /// Reads run-level codes into one block until it is full or the bits run out.
  /// </summary>
  /// <remarks>
  /// The dangling-codeword handling is the whole subtlety here. A code that would reach past the
  /// budget is not read: its bits are kept, left-aligned, and prepended to whatever stream this block
  /// is continued in. That is why the first code of a continued block is read out of a window built
  /// from two sources at once.
  /// </remarks>
  /// <returns>The bit position reached, from which the caller copies what is left over.</returns>
  private static int _DecodeCoefficients(
    in DvBitReader reader, ref BlockState state, short[] coefficients, int blockBase, int[] factors, int index) {

    var scan = state.FieldCoded ? DvTables.Zigzag248 : DvTables.Zigzag;
    var position = state.Position;
    var prefixCount = state.PartialBitCount;
    var prefixBits = state.PartialBitBuffer;
    var limit = reader.BitLimit;
    var start = index;

    state.PartialBitCount = 0;
    state.PartialBitBuffer = 0;

    // The carried bits sit in front of where this stream is being read from, so the block's position
    // counts from before it: the first code is read out of the prefix and the stream together.
    index -= prefixCount;

    for (;;) {
      var window = _Window(reader, index, start, prefixCount, prefixBits);
      var code = DvVlc.Decode(window);

      if (index + code.Length > limit) {
        var kept = limit - index;
        if (kept is < 0 or > 31)
          throw new InvalidDataException(
            $"A DV block carries {kept} bits of an unfinished code, which no code of this format is long enough to be.");

        state.PartialBitCount = kept;
        state.PartialBitBuffer = window & ~(uint.MaxValue >> kept);
        index = limit;
        break;
      }

      index += code.Length;
      position += code.Run + 1;
      if (position >= 64)
        break;

      coefficients[blockBase + scan[position]] =
        (short)((code.Level * factors[state.FactorBase + position] + (1 << (DvTables.InverseWeightBits - 1)))
                >> DvTables.InverseWeightBits);
    }

    state.Position = position;
    return index;
  }

  /// <summary>
  /// The next thirty-two bits, taken from the carried-over prefix first and the stream after it.
  /// </summary>
  /// <remarks>
  /// The prefix occupies the positions immediately before <paramref name="start"/>, which is where
  /// this block's bits actually begin — not the beginning of the buffer. Passes two and three read a
  /// buffer several blocks have already taken from, so the two are rarely the same place.
  /// </remarks>
  private static uint _Window(in DvBitReader reader, int index, int start, int prefixCount, uint prefixBits) {
    if (index >= start)
      return reader.PeekWord(index);

    var remaining = start - index;
    return (prefixBits << (prefixCount - remaining)) | (reader.PeekWord(start) >> remaining);
  }

  /// <summary>
  /// Puts one macroblock's six transformed blocks where the picture wants them.
  /// </summary>
  /// <remarks>
  /// The three samplings put the same six blocks in three different shapes. 4:1:1 lays its four luma
  /// blocks in a row, thirty-two samples wide and eight tall, and gives them one colour block each
  /// covering the same thirty-two columns. 4:2:0 stacks them two by two, sixteen by sixteen, and the
  /// colour block covers the whole square. DVCPRO50's 4:2:2 uses only two of the four — the other two
  /// are written as empty blocks and skipped here — for a macroblock sixteen wide and eight tall.
  /// <para/>
  /// The last macroblock column of a 4:1:1 raster is the exception the format could not avoid: 720 is
  /// not a multiple of 32, so the final sixteen columns are coded as a 16x16 square like 4:2:0's, and
  /// its colour block holds two 4x8 halves stacked side by side that have to be unstacked on the way
  /// out.
  /// </remarks>
  private static void _Place(
    DvProfile profile, in DvGeometry.Segment segment, int macroblock, short[] coefficients,
    BlockState[] states, DvPlanes planes, byte[] chromaScratch) {

    var first = macroblock * DvProfile.BlocksPerMacroblock;
    var mbX = segment.MacroblockX[macroblock];
    var mbY = segment.MacroblockY[macroblock];
    var lumaStride = planes.Width;

    // Past column 704 a 4:1:1 macroblock is folded into a square, so its lower half is a whole block
    // row down rather than eight samples to the right.
    var folded = profile.Sampling == DvSampling.FourOneOne && mbX >= 704 / 8;
    var lumaStep = profile.Sampling == DvSampling.FourTwoZero || folded ? lumaStride * 8 : 16;
    var lumaOffset = mbY * 8 * lumaStride + mbX * 8;

    _Transform(states[first], coefficients, first * 64, planes.Luma, lumaOffset, lumaStride);
    if (profile.Sampling == DvSampling.FourTwoTwo) {
      _Transform(states[first + 2], coefficients, (first + 2) * 64, planes.Luma, lumaOffset + 8, lumaStride);
    } else {
      _Transform(states[first + 1], coefficients, (first + 1) * 64, planes.Luma, lumaOffset + 8, lumaStride);
      _Transform(states[first + 2], coefficients, (first + 2) * 64, planes.Luma, lumaOffset + lumaStep, lumaStride);
      _Transform(states[first + 3], coefficients, (first + 3) * 64, planes.Luma, lumaOffset + 8 + lumaStep, lumaStride);
    }

    var chromaStride = planes.ChromaWidth;
    var chromaOffset =
      ((profile.Sampling == DvSampling.FourTwoZero ? mbY >> 1 : mbY) * chromaStride
       + (profile.Sampling == DvSampling.FourOneOne ? mbX >> 2 : mbX >> 1)) * 8;

    // Red difference first: DV writes the two colour blocks in the order Cr, Cb.
    for (var component = 0; component < 2; ++component) {
      var target = component == 0 ? planes.Cr : planes.Cb;
      var block = first + 4 + component;

      if (!folded) {
        _Transform(states[block], coefficients, block * 64, target, chromaOffset, chromaStride);
        continue;
      }

      _Transform(states[block], coefficients, block * 64, chromaScratch, 0, 8);
      for (var row = 0; row < 8; ++row) {
        var source = row * 8;
        var upper = chromaOffset + row * chromaStride;
        var lower = upper + chromaStride * 8;
        for (var column = 0; column < 4; ++column) {
          target[upper + column] = chromaScratch[source + column];
          target[lower + column] = chromaScratch[source + 4 + column];
        }
      }
    }
  }

  private static void _Transform(in BlockState state, short[] coefficients, int blockBase, byte[] plane, int offset, int stride) {
    var block = coefficients.AsSpan(blockBase, 64);
    if (state.FieldCoded)
      DvInverseDct.Put248(block, plane, offset, stride);
    else
      DvInverseDct.Put(block, plane, offset, stride);
  }
}
