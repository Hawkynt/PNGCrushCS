using System;
using System.IO;

namespace FileFormat.Codecs.Dv;

/// <summary>Decodes one SMPTE 370M DV100 video segment.</summary>
/// <remarks>
/// DV100 keeps DV's three-pass run/level bit pooling but changes the macroblock to eight blocks, the
/// per-block budgets and the quantiser/weighting model. The implementation follows the public SMPTE
/// 370M syntax and is cross-checked against FFmpeg's LGPL-2.1-or-later DV decoder; provenance is in
/// <c>THIRD-PARTY-NOTICE.FFmpeg.txt</c>.
/// </remarks>
internal static class Dv100SegmentDecoder {

  private const int _BlocksPerMacroblock = 8;
  private const int _BlocksPerSegment = DvProfile.MacroblocksPerSegment * _BlocksPerMacroblock;
  private const int _MacroblockOverflowBytes = 80;
  private const int _SegmentOverflowBytes = 80 * DvProfile.MacroblocksPerSegment;

  private static readonly uint[] _Factors1080 = _BuildFactors(Dv100Tables.Inverse1080Luma, Dv100Tables.Inverse1080Chroma);
  private static readonly uint[] _Factors720 = _BuildFactors(Dv100Tables.Inverse720Luma, Dv100Tables.Inverse720Chroma);

  private struct BlockState {
    internal int Position;
    internal int PartialBitCount;
    internal uint PartialBitBuffer;
    internal int FactorBase;
  }

  /// <summary>Reusable storage for one segment decode.</summary>
  internal sealed class Scratch {
    internal readonly short[] Coefficients = new short[_BlocksPerSegment * 64];
    internal readonly BlockState[] States = new BlockState[_BlocksPerSegment];
    internal readonly byte[] MacroblockOverflow = new byte[_MacroblockOverflowBytes];
    internal readonly byte[] SegmentOverflow = new byte[_SegmentOverflowBytes];
    internal readonly byte[] BlockPixels = new byte[64];
  }

  internal static void Decode(
    ReadOnlySpan<byte> frame, DvProfile profile, in DvGeometry.Segment segment,
    DvPlanes planes, Scratch scratch) {

    if (!profile.IsDv100)
      throw new ArgumentException("The DV100 segment decoder only accepts SMPTE 370M profiles.", nameof(profile));

    var factors = profile.Height == 720 ? _Factors720 : _Factors1080;
    var states = scratch.States;
    var coefficients = scratch.Coefficients;
    Array.Clear(states);
    Array.Clear(coefficients);

    var segmentOverflow = new DvBitWriter(scratch.SegmentOverflow, 0, _SegmentOverflowBytes);
    var position = segment.BlockOffset * DvProfile.DifBlockSize;

    for (var macroblock = 0; macroblock < DvProfile.MacroblocksPerSegment; ++macroblock) {
      var first = macroblock * _BlocksPerMacroblock;
      if (position + 4 > frame.Length)
        throw new InvalidDataException("A DV100 video segment runs past the end of the frame.");

      var quantiser = frame[position + 3] & 0x0f;
      position += 4;
      var macroblockOverflow = new DvBitWriter(scratch.MacroblockOverflow, 0, _MacroblockOverflowBytes);

      for (var b = 0; b < _BlocksPerMacroblock; ++b) {
        var budget = DvProfile.Dv100BlockSizes[b];
        var bytes = budget >> 3;
        if (position + bytes > frame.Length)
          throw new InvalidDataException("A DV100 block runs past the end of the frame.");

        var reader = new DvBitReader(frame.Slice(position, bytes), budget);
        position += bytes;
        var index = 0;
        var dc = reader.ReadSigned(ref index, 9);
        _ = reader.ReadBits(ref index, 1); // DCT mode belongs to the macroblock placement, read from block zero below.
        var classNumber = (int)reader.ReadBits(ref index, 2);

        ref var state = ref states[first + b];
        state.FactorBase =
          (b >= 4 ? 4 * 16 * 64 : 0)
          + classNumber * 16 * 64
          + quantiser * 64;

        coefficients[(first + b) * 64] = (short)(dc * 4 + 1024);
        index = _DecodeCoefficients(reader, ref state, coefficients, (first + b) * 64, factors, index);

        if (state.Position >= 64)
          macroblockOverflow.CopyFrom(reader, index);
      }

      var spare = new DvBitReader(
        scratch.MacroblockOverflow.AsSpan(0, (macroblockOverflow.BitCount + 7) / 8),
        macroblockOverflow.BitCount);
      var spareIndex = 0;
      var b2 = 0;
      for (; b2 < _BlocksPerMacroblock; ++b2) {
        ref var state = ref states[first + b2];
        if (state.Position >= 64 || spare.BitLimit - spareIndex <= 0)
          continue;

        spareIndex = _DecodeCoefficients(spare, ref state, coefficients, (first + b2) * 64, factors, spareIndex);
        if (state.Position < 64)
          break;
      }

      if (b2 >= _BlocksPerMacroblock)
        segmentOverflow.CopyFrom(spare, spareIndex);
    }

    var pool = new DvBitReader(
      scratch.SegmentOverflow.AsSpan(0, (segmentOverflow.BitCount + 7) / 8),
      segmentOverflow.BitCount);
    var poolIndex = 0;
    for (var b = 0; b < _BlocksPerSegment; ++b) {
      ref var state = ref states[b];
      if (state.Position >= 64 || pool.BitLimit - poolIndex <= 0)
        continue;
      poolIndex = _DecodeCoefficients(pool, ref state, coefficients, b * 64, factors, poolIndex);
    }

    for (var macroblock = 0; macroblock < DvProfile.MacroblocksPerSegment; ++macroblock)
      _Place(frame, profile, segment, macroblock, coefficients, planes, scratch);
  }

  private static uint[] _BuildFactors(ReadOnlySpan<ushort> luma, ReadOnlySpan<ushort> chroma) {
    var factors = new uint[2 * 4 * 16 * 64];
    for (var component = 0; component < 2; ++component) {
      var weights = component == 0 ? luma : chroma;
      for (var classNumber = 0; classNumber < 4; ++classNumber)
        for (var quantiser = 0; quantiser < 16; ++quantiser)
          for (var coefficient = 0; coefficient < 64; ++coefficient) {
            var at = component * 4 * 16 * 64 + classNumber * 16 * 64 + quantiser * 64 + coefficient;
            factors[at] = (uint)((Dv100Tables.QuantisationSteps[quantiser] << (classNumber + 9)) * weights[coefficient]);
          }
    }
    return factors;
  }

  private static int _DecodeCoefficients(
    in DvBitReader reader, ref BlockState state, short[] coefficients, int blockBase,
    uint[] factors, int index) {

    var position = state.Position;
    var prefixCount = state.PartialBitCount;
    var prefixBits = state.PartialBitBuffer;
    var limit = reader.BitLimit;
    var start = index;
    state.PartialBitCount = 0;
    state.PartialBitBuffer = 0;
    index -= prefixCount;

    for (;;) {
      var window = _Window(reader, index, start, prefixCount, prefixBits);
      var code = DvVlc.Decode(window);
      if (index + code.Length > limit) {
        var kept = limit - index;
        if (kept is < 0 or > 31)
          throw new InvalidDataException(
            $"A DV100 block carries {kept} bits of an unfinished code, which no DV code is long enough to be.");
        state.PartialBitCount = kept;
        state.PartialBitBuffer = window & ~(uint.MaxValue >> kept);
        index = limit;
        break;
      }

      index += code.Length;
      position += code.Run + 1;
      if (position >= 64)
        break;

      var scaled = unchecked((uint)code.Level * factors[state.FactorBase + position] + (1u << (DvTables.InverseWeightBits - 1)));
      coefficients[blockBase + DvTables.Zigzag[position]] = unchecked((short)(scaled >> DvTables.InverseWeightBits));
    }

    state.Position = position;
    return index;
  }

  private static uint _Window(in DvBitReader reader, int index, int start, int prefixCount, uint prefixBits) {
    if (index >= start)
      return reader.PeekWord(index);
    var remaining = start - index;
    return (prefixBits << (prefixCount - remaining)) | (reader.PeekWord(start) >> remaining);
  }

  private static void _Place(
    ReadOnlySpan<byte> frame, DvProfile profile, in DvGeometry.Segment segment, int macroblock,
    short[] coefficients, DvPlanes planes, Scratch scratch) {

    var first = macroblock * _BlocksPerMacroblock;
    var (mbX, mbY) = DvGeometry.MacroblockForFrame(profile, segment, macroblock, frame);
    var fieldMode = _FieldMode(frame, segment, macroblock);
    var lumaLeft = mbX * 8;
    var lumaTop = mbY * 8;
    var lumaOffset = lumaTop * planes.Width + lumaLeft;

    if (mbY == 134) {
      _PlaceLast1080Row(first, fieldMode, coefficients, planes, lumaOffset, mbX, scratch);
      return;
    }

    var lumaStride = planes.Width;
    if (fieldMode) {
      _Put(coefficients, first + 0, planes.Luma, lumaOffset, lumaStride * 2);
      _Put(coefficients, first + 1, planes.Luma, lumaOffset + 8, lumaStride * 2);
      _Put(coefficients, first + 2, planes.Luma, lumaOffset + lumaStride, lumaStride * 2);
      _Put(coefficients, first + 3, planes.Luma, lumaOffset + lumaStride + 8, lumaStride * 2);
    } else {
      _Put(coefficients, first + 0, planes.Luma, lumaOffset, lumaStride);
      _Put(coefficients, first + 1, planes.Luma, lumaOffset + 8, lumaStride);
      _Put(coefficients, first + 2, planes.Luma, lumaOffset + lumaStride * 8, lumaStride);
      _Put(coefficients, first + 3, planes.Luma, lumaOffset + lumaStride * 8 + 8, lumaStride);
    }

    var chromaStride = planes.ChromaWidth;
    var chromaOffset = lumaTop * chromaStride + (mbX >> 1) * 8;
    _PlaceChroma(first + 4, fieldMode, coefficients, planes.Cr, chromaOffset, chromaStride);
    _PlaceChroma(first + 6, fieldMode, coefficients, planes.Cb, chromaOffset, chromaStride);
  }

  private static bool _FieldMode(ReadOnlySpan<byte> frame, in DvGeometry.Segment segment, int macroblock) {
    var position = (segment.BlockOffset + macroblock) * DvProfile.DifBlockSize + 4;
    return position < frame.Length && (frame[position] & 0x40) != 0;
  }

  private static void _PlaceChroma(
    int first, bool fieldMode, short[] coefficients, byte[] plane, int offset, int stride) {
    if (fieldMode) {
      _Put(coefficients, first, plane, offset, stride * 2);
      _Put(coefficients, first + 1, plane, offset + stride, stride * 2);
    } else {
      _Put(coefficients, first, plane, offset, stride);
      _Put(coefficients, first + 1, plane, offset + stride * 8, stride);
    }
  }

  private static void _PlaceLast1080Row(
    int first, bool fieldMode, short[] coefficients, DvPlanes planes, int lumaOffset, int mbX, Scratch scratch) {

    if (!fieldMode) {
      for (var b = 0; b < 4; ++b)
        _Put(coefficients, first + b, planes.Luma, lumaOffset + b * 8, planes.Width);

      var chromaOffset = 134 * 8 * planes.ChromaWidth + (mbX >> 1) * 8;
      _Put(coefficients, first + 4, planes.Cr, chromaOffset, planes.ChromaWidth);
      _Put(coefficients, first + 5, planes.Cr, chromaOffset + 8, planes.ChromaWidth);
      _Put(coefficients, first + 6, planes.Cb, chromaOffset, planes.ChromaWidth);
      _Put(coefficients, first + 7, planes.Cb, chromaOffset + 8, planes.ChromaWidth);
      return;
    }

    _PutFourRows(coefficients, first + 0, planes.Luma, lumaOffset, planes.Width * 2, 0, scratch.BlockPixels);
    _PutFourRows(coefficients, first + 0, planes.Luma, lumaOffset + 16, planes.Width * 2, 4, scratch.BlockPixels);
    _PutFourRows(coefficients, first + 1, planes.Luma, lumaOffset + 8, planes.Width * 2, 0, scratch.BlockPixels);
    _PutFourRows(coefficients, first + 1, planes.Luma, lumaOffset + 24, planes.Width * 2, 4, scratch.BlockPixels);
    _PutFourRows(coefficients, first + 2, planes.Luma, lumaOffset + planes.Width, planes.Width * 2, 0, scratch.BlockPixels);
    _PutFourRows(coefficients, first + 2, planes.Luma, lumaOffset + planes.Width + 16, planes.Width * 2, 4, scratch.BlockPixels);
    _PutFourRows(coefficients, first + 3, planes.Luma, lumaOffset + planes.Width + 8, planes.Width * 2, 0, scratch.BlockPixels);
    _PutFourRows(coefficients, first + 3, planes.Luma, lumaOffset + planes.Width + 24, planes.Width * 2, 4, scratch.BlockPixels);

    var chromaOffset = 134 * 8 * planes.ChromaWidth + (mbX >> 1) * 8;
    _PutFourRows(coefficients, first + 4, planes.Cr, chromaOffset, planes.ChromaWidth * 2, 0, scratch.BlockPixels);
    _PutFourRows(coefficients, first + 4, planes.Cr, chromaOffset + 8, planes.ChromaWidth * 2, 4, scratch.BlockPixels);
    _PutFourRows(coefficients, first + 5, planes.Cr, chromaOffset + planes.ChromaWidth, planes.ChromaWidth * 2, 0, scratch.BlockPixels);
    _PutFourRows(coefficients, first + 5, planes.Cr, chromaOffset + planes.ChromaWidth + 8, planes.ChromaWidth * 2, 4, scratch.BlockPixels);
    _PutFourRows(coefficients, first + 6, planes.Cb, chromaOffset, planes.ChromaWidth * 2, 0, scratch.BlockPixels);
    _PutFourRows(coefficients, first + 6, planes.Cb, chromaOffset + 8, planes.ChromaWidth * 2, 4, scratch.BlockPixels);
    _PutFourRows(coefficients, first + 7, planes.Cb, chromaOffset + planes.ChromaWidth, planes.ChromaWidth * 2, 0, scratch.BlockPixels);
    _PutFourRows(coefficients, first + 7, planes.Cb, chromaOffset + planes.ChromaWidth + 8, planes.ChromaWidth * 2, 4, scratch.BlockPixels);
  }

  private static void _Put(short[] coefficients, int block, byte[] plane, int offset, int stride)
    => DvInverseDct.Put(coefficients.AsSpan(block * 64, 64), plane, offset, stride);

  private static void _PutFourRows(
    short[] coefficients, int block, byte[] plane, int offset, int stride, int sourceRow, byte[] pixels) {
    Array.Clear(pixels);
    DvInverseDct.Put(coefficients.AsSpan(block * 64, 64), pixels, 0, 8);
    for (var row = 0; row < 4; ++row)
      pixels.AsSpan((sourceRow + row) * 8, 8).CopyTo(plane.AsSpan(offset + row * stride, 8));
  }
}
