using System;
using System.IO;

namespace FileFormat.Codecs.Vc1;

/// <summary>Decodes the progressive Simple/Main predictive subset emitted by <see cref="Vc1VideoEncoder"/>.</summary>
/// <remarks>
/// This is deliberately strict. It reconstructs 1-MV P macroblocks whose accepted motion differential is zero and
/// direct B macroblocks whose co-located anchor motion vector is therefore also zero. That already gives real temporal
/// prediction, P residuals and two-reference B residuals while preserving bit-exact reference pictures. Mixed/4-MV,
/// non-zero motion, intensity compensation, non-raw bitplanes, differential quantisation and variable transforms are
/// rejected by name rather than decoded as a plausible-looking but wrong picture.
/// </remarks>
internal sealed class Vc1PredictivePictureDecoder {

  private static readonly Vc1VlcTable _MotionVectorDifferential =
    new("Motion-vector differential table 0", Vc1PredictiveTables.MotionVectorDifferential0);
  private static readonly Vc1VlcTable _CodedBlockPattern =
    new("P/B coded-block-pattern table 0", Vc1PredictiveTables.CodedBlockPattern0);

  private readonly Vc1SequenceHeader _sequence;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly Vc1EscapeState _escape = new();

  internal Vc1PredictivePictureDecoder(Vc1SequenceHeader sequence, int macroblockWidth, int macroblockHeight) {
    this._sequence = sequence;
    this._macroblockWidth = macroblockWidth;
    this._macroblockHeight = macroblockHeight;
  }

  internal Vc1Frame DecodePredicted(ReadOnlySpan<byte> data, Vc1Frame reference, out Vc1PictureHeader header) {
    ArgumentNullException.ThrowIfNull(reference);
    var reader = new Vc1BitReader(data);
    header = Vc1PictureHeader.ReadFrom(ref reader, this._sequence);
    if (header.PictureType != Vc1PictureType.Predicted)
      throw new InvalidDataException($"A P-picture decoder was handed a {header.PictureType} picture.");

    this._RefuseUnsupportedPictureTools(header, "P");
    _ReadRawBitPlaneHeader(ref reader, "SKIPMB");

    var motionTable = reader.ReadBits(2);
    var cbpTable = reader.ReadBits(2);
    this._RefuseUnsupportedTableSelectors(motionTable, cbpTable);
    this._ReadTransformHeader(ref reader, header, out var codingSet);

    var result = new Vc1Frame(this._macroblockWidth, this._macroblockHeight);
    this._escape.Reset();

    Span<int> ordered = stackalloc int[64];
    Span<int> residual = stackalloc int[64];
    for (var mbY = 0; mbY < this._macroblockHeight; ++mbY)
      for (var mbX = 0; mbX < this._macroblockWidth; ++mbX) {
        var skipped = reader.ReadBit() != 0;
        if (skipped) {
          _CopyMacroblock(reference, result, mbX, mbY);
          continue;
        }

        var mvIndex = _MotionVectorDifferential.Read(ref reader);
        if (mvIndex != 36)
          throw new NotSupportedException(
            $"This progressive VC-1 P macroblock uses MVDATA table-0 index {mvIndex}; the implemented 1-MV path accepts the zero-differential, residual-present index 36.");

        var pattern = _CodedBlockPattern.Read(ref reader);
        this._DecodeResidualMacroblock(ref reader, header, codingSet, pattern, reference, null, result, mbX, mbY, ordered, residual);
      }

    return result;
  }

  internal Vc1Frame DecodeBidirectional(
    ReadOnlySpan<byte> data,
    Vc1Frame previousReference,
    Vc1Frame nextReference,
    out Vc1PictureHeader header) {
    ArgumentNullException.ThrowIfNull(previousReference);
    ArgumentNullException.ThrowIfNull(nextReference);

    var reader = new Vc1BitReader(data);
    header = Vc1PictureHeader.ReadFrom(ref reader, this._sequence);
    if (header.PictureType != Vc1PictureType.Bidirectional)
      throw new InvalidDataException($"A B-picture decoder was handed a {header.PictureType} picture.");

    this._RefuseUnsupportedPictureTools(header, "B");
    _ReadRawBitPlaneHeader(ref reader, "DIRECTMB");
    _ReadRawBitPlaneHeader(ref reader, "SKIPMB");

    var motionTable = reader.ReadBits(2);
    var cbpTable = reader.ReadBits(2);
    this._RefuseUnsupportedTableSelectors(motionTable, cbpTable);
    this._ReadTransformHeader(ref reader, header, out var codingSet);

    var result = new Vc1Frame(this._macroblockWidth, this._macroblockHeight);
    this._escape.Reset();

    Span<int> ordered = stackalloc int[64];
    Span<int> residual = stackalloc int[64];
    for (var mbY = 0; mbY < this._macroblockHeight; ++mbY)
      for (var mbX = 0; mbX < this._macroblockWidth; ++mbX) {
        var direct = reader.ReadBit() != 0;
        var skipped = reader.ReadBit() != 0;
        if (!direct)
          throw new NotSupportedException(
            "This progressive VC-1 B macroblock is forward/backward/interpolated rather than direct; explicit B motion vectors are not implemented yet.");

        if (skipped) {
          _AverageMacroblock(previousReference, nextReference, result, mbX, mbY);
          continue;
        }

        var pattern = _CodedBlockPattern.Read(ref reader);
        this._DecodeResidualMacroblock(
          ref reader, header, codingSet, pattern, previousReference, nextReference, result, mbX, mbY, ordered, residual);
      }

    return result;
  }

  private void _RefuseUnsupportedPictureTools(Vc1PictureHeader header, string type) {
    if (this._sequence.SyncMarker)
      throw new NotSupportedException($"Progressive VC-1 {type} pictures with sync markers are not implemented.");
    if (this._sequence.DifferentialQuantisation != 0)
      throw new NotSupportedException($"Progressive VC-1 {type} pictures with differential macroblock quantisation are not implemented.");
    if (this._sequence.VariableSizedTransform)
      throw new NotSupportedException($"Progressive VC-1 {type} pictures with variable-sized inter transforms are not implemented.");
    if (this._sequence.Overlap)
      throw new NotSupportedException($"Progressive VC-1 {type} pictures with overlapped inter reconstruction are not implemented.");
    if (header.MotionMode is Vc1MotionMode.MixedMv or Vc1MotionMode.IntensityCompensation)
      throw new NotSupportedException($"Progressive VC-1 {type} pictures using {header.MotionMode} are not implemented.");
  }

  private void _RefuseUnsupportedTableSelectors(int motionTable, int cbpTable) {
    if (motionTable != 0)
      throw new NotSupportedException($"VC-1 motion-vector differential table {motionTable} is not implemented; table 0 is supported.");
    if (cbpTable != 0)
      throw new NotSupportedException($"VC-1 P/B coded-block-pattern table {cbpTable} is not implemented; table 0 is supported.");
  }

  private void _ReadTransformHeader(ref Vc1BitReader reader, Vc1PictureHeader header, out Vc1AcCodingSet codingSet) {
    var transformAcIndex = Vc1PictureHeader.ReadCodingSetIndex(ref reader);
    reader.ReadBit(); // TRANSDCTAB is irrelevant to inter blocks but is present in every frame type.
    codingSet = Vc1AcCodingSet.For(transformAcIndex, luma: false, header.QuantiserIndex);
  }

  private void _DecodeResidualMacroblock(
    ref Vc1BitReader reader,
    Vc1PictureHeader header,
    Vc1AcCodingSet codingSet,
    int pattern,
    Vc1Frame firstReference,
    Vc1Frame? secondReference,
    Vc1Frame destination,
    int mbX,
    int mbY,
    scoped Span<int> ordered,
    scoped Span<int> residual) {
    var doubleQuant = (2 * header.Quantiser) + (header.HalfStep ? 1 : 0);
    var conservativeEscape = header.Quantiser <= 7;

    for (var blockIndex = 0; blockIndex < 6; ++blockIndex) {
      ordered.Clear();
      residual.Clear();
      if ((pattern & (1 << (5 - blockIndex))) != 0) {
        Vc1BlockDecoder.ReadInterCoefficients(
          ref reader, codingSet, this._escape, header.Quantiser, conservativeEscape, ordered);
        Vc1BlockDecoder.InverseScan(ordered, Vc1PredictiveTables.Inter8x8Scan, residual);
        _DequantiseInter(residual, doubleQuant, header.Quantiser, header.UniformQuantiser);
        Vc1InverseTransform.Apply(residual);
      }

      _ReconstructBlock(firstReference, secondReference, destination, residual, blockIndex, mbX, mbY);
    }
  }

  private static void _DequantiseInter(Span<int> block, int doubleQuant, int quantiser, bool uniform) {
    for (var i = 0; i < block.Length; ++i) {
      var value = block[i];
      if (value == 0)
        continue;
      block[i] = uniform
        ? value * doubleQuant
        : (value * doubleQuant) + (value < 0 ? -quantiser : quantiser);
    }
  }

  private static void _ReconstructBlock(
    Vc1Frame firstReference,
    Vc1Frame? secondReference,
    Vc1Frame destination,
    ReadOnlySpan<int> residual,
    int blockIndex,
    int mbX,
    int mbY) {
    var (first, stride) = firstReference.PlaneOf(blockIndex);
    var (to, _) = destination.PlaneOf(blockIndex);
    int[]? second = secondReference == null ? null : secondReference.PlaneOf(blockIndex).Samples;
    var x = blockIndex < 4 ? (mbX * 16) + ((blockIndex & 1) * 8) : mbX * 8;
    var y = blockIndex < 4 ? (mbY * 16) + ((blockIndex >> 1) * 8) : mbY * 8;

    for (var row = 0; row < 8; ++row) {
      var at = ((y + row) * stride) + x;
      for (var column = 0; column < 8; ++column) {
        var index = at + column;
        var prediction = second == null ? first[index] : (first[index] + second[index] + 1) >> 1;
        var delta = residual.IsEmpty ? 0 : residual[(row * 8) + column];
        var value = prediction + delta;
        to[index] = value < 0 ? 0 : value > 255 ? 255 : value;
      }
    }
  }

  private static void _CopyMacroblock(Vc1Frame source, Vc1Frame destination, int mbX, int mbY) {
    for (var block = 0; block < 6; ++block)
      _ReconstructBlock(source, null, destination, ReadOnlySpan<int>.Empty, block, mbX, mbY);
  }

  private static void _AverageMacroblock(Vc1Frame first, Vc1Frame second, Vc1Frame destination, int mbX, int mbY) {
    for (var block = 0; block < 6; ++block)
      _ReconstructBlock(first, second, destination, ReadOnlySpan<int>.Empty, block, mbX, mbY);
  }

  private static void _ReadRawBitPlaneHeader(ref Vc1BitReader reader, string name) {
    reader.ReadBit(); // INVERT has no effect in raw mode.

    if (reader.ReadBit() != 0)
      throw new NotSupportedException($"VC-1 {name} uses {(reader.ReadBit() == 0 ? "Norm-2" : "Norm-6")} bitplane coding; raw coding is implemented.");

    if (reader.ReadBit() != 0)
      throw new NotSupportedException($"VC-1 {name} uses {(reader.ReadBit() == 0 ? "row-skip" : "column-skip")} bitplane coding; raw coding is implemented.");

    if (reader.ReadBit() != 0)
      throw new NotSupportedException($"VC-1 {name} uses differential Norm-2 bitplane coding; raw coding is implemented.");

    if (reader.ReadBit() != 0)
      throw new NotSupportedException($"VC-1 {name} uses differential Norm-6 bitplane coding; raw coding is implemented.");
  }
}
