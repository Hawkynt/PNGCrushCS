using System;
using System.IO;

namespace FileFormat.Codecs.H264;

internal readonly record struct H264ReferenceWeight(
  int LumaWeight,
  int LumaOffset,
  int CbWeight,
  int CbOffset,
  int CrWeight,
  int CrOffset);

/// <summary>H.264 <c>pred_weight_table()</c> and explicit weighted sample prediction.</summary>
internal sealed class H264PredictionWeights {
  private readonly H264ReferenceWeight[] _list0;
  private readonly H264ReferenceWeight[] _list1;

  private H264PredictionWeights(
    int lumaDenom,
    int chromaDenom,
    H264ReferenceWeight[] list0,
    H264ReferenceWeight[] list1) {
    this.LumaLog2WeightDenom = lumaDenom;
    this.ChromaLog2WeightDenom = chromaDenom;
    this._list0 = list0;
    this._list1 = list1;
  }

  internal int LumaLog2WeightDenom { get; }
  internal int ChromaLog2WeightDenom { get; }

  internal static H264PredictionWeights ParseP(
    ref H264BitReader reader,
    H264SequenceParameterSet sps,
    int numRefIdxL0Active) {
    var (lumaDenom, chromaDenom, hasChroma) = _ReadDenominators(ref reader, sps);
    var list0 = _ReadList(ref reader, numRefIdxL0Active, lumaDenom, chromaDenom, hasChroma, 0);
    return new(lumaDenom, chromaDenom, list0, []);
  }

  internal static H264PredictionWeights ParseB(
    ref H264BitReader reader,
    H264SequenceParameterSet sps,
    int numRefIdxL0Active,
    int numRefIdxL1Active) {
    var (lumaDenom, chromaDenom, hasChroma) = _ReadDenominators(ref reader, sps);
    var list0 = _ReadList(ref reader, numRefIdxL0Active, lumaDenom, chromaDenom, hasChroma, 0);
    var list1 = _ReadList(ref reader, numRefIdxL1Active, lumaDenom, chromaDenom, hasChroma, 1);
    return new(lumaDenom, chromaDenom, list0, list1);
  }

  internal void ApplyLuma(int referenceIndex, Span<byte> prediction)
    => this.ApplyLuma(0, referenceIndex, prediction);

  internal void ApplyLuma(int list, int referenceIndex, Span<byte> prediction) {
    var weight = this._At(list, referenceIndex);
    _Apply(prediction, weight.LumaWeight, weight.LumaOffset, this.LumaLog2WeightDenom);
  }

  internal void ApplyChroma(int referenceIndex, int component, Span<byte> prediction)
    => this.ApplyChroma(0, referenceIndex, component, prediction);

  internal void ApplyChroma(int list, int referenceIndex, int component, Span<byte> prediction) {
    var weight = this._At(list, referenceIndex);
    var (value, offset) = component switch {
      0 => (weight.CbWeight, weight.CbOffset),
      1 => (weight.CrWeight, weight.CrOffset),
      _ => throw new ArgumentOutOfRangeException(nameof(component)),
    };
    _Apply(prediction, value, offset, this.ChromaLog2WeightDenom);
  }

  internal void CombineLuma(
    int refIdxL0,
    ReadOnlySpan<byte> predictionL0,
    int refIdxL1,
    ReadOnlySpan<byte> predictionL1,
    Span<byte> output) {
    var first = this._At(0, refIdxL0);
    var second = this._At(1, refIdxL1);
    _Combine(
      predictionL0, predictionL1, output,
      first.LumaWeight, first.LumaOffset,
      second.LumaWeight, second.LumaOffset,
      this.LumaLog2WeightDenom);
  }

  internal void CombineChroma(
    int refIdxL0,
    int refIdxL1,
    int component,
    ReadOnlySpan<byte> predictionL0,
    ReadOnlySpan<byte> predictionL1,
    Span<byte> output) {
    var first = this._At(0, refIdxL0);
    var second = this._At(1, refIdxL1);
    var (w0, o0, w1, o1) = component switch {
      0 => (first.CbWeight, first.CbOffset, second.CbWeight, second.CbOffset),
      1 => (first.CrWeight, first.CrOffset, second.CrWeight, second.CrOffset),
      _ => throw new ArgumentOutOfRangeException(nameof(component)),
    };
    _Combine(predictionL0, predictionL1, output, w0, o0, w1, o1, this.ChromaLog2WeightDenom);
  }

  private H264ReferenceWeight _At(int list, int referenceIndex) {
    var entries = list switch {
      0 => this._list0,
      1 => this._list1,
      _ => throw new ArgumentOutOfRangeException(nameof(list)),
    };
    if ((uint)referenceIndex >= (uint)entries.Length)
      throw new InvalidDataException(
        $"An H.264 weighted prediction names list-{list} reference {referenceIndex}, but its pred_weight_table carries "
        + $"only {entries.Length} entries.");
    return entries[referenceIndex];
  }

  private static (int LumaDenom, int ChromaDenom, bool HasChroma) _ReadDenominators(
    ref H264BitReader reader,
    H264SequenceParameterSet sps) {
    var lumaDenom = reader.ReadUnsignedExpGolomb();
    if (lumaDenom > 7)
      throw new InvalidDataException(
        $"This H.264 slice states luma_log2_weight_denom {lumaDenom}; clause 7.4.3.2 bounds it at 0 through 7.");
    var hasChroma = !sps.SeparateColourPlaneFlag && sps.ChromaFormatIdc != 0;
    var chromaDenom = hasChroma ? reader.ReadUnsignedExpGolomb() : 0;
    if (chromaDenom > 7)
      throw new InvalidDataException(
        $"This H.264 slice states chroma_log2_weight_denom {chromaDenom}; clause 7.4.3.2 bounds it at 0 through 7.");
    return (lumaDenom, chromaDenom, hasChroma);
  }

  private static H264ReferenceWeight[] _ReadList(
    ref H264BitReader reader,
    int count,
    int lumaDenom,
    int chromaDenom,
    bool hasChroma,
    int list) {
    var defaultLumaWeight = 1 << lumaDenom;
    var defaultChromaWeight = 1 << chromaDenom;
    var entries = new H264ReferenceWeight[count];
    for (var i = 0; i < count; ++i) {
      var lumaWeight = defaultLumaWeight;
      var lumaOffset = 0;
      if (reader.ReadBit() != 0) {
        lumaWeight = _ReadSignedByteRange(ref reader, $"luma_weight_l{list}", i, -1);
        lumaOffset = _ReadSignedByteRange(ref reader, $"luma_offset_l{list}", i, -1);
      }

      var cbWeight = defaultChromaWeight;
      var cbOffset = 0;
      var crWeight = defaultChromaWeight;
      var crOffset = 0;
      if (hasChroma && reader.ReadBit() != 0) {
        cbWeight = _ReadSignedByteRange(ref reader, $"chroma_weight_l{list}", i, 0);
        cbOffset = _ReadSignedByteRange(ref reader, $"chroma_offset_l{list}", i, 0);
        crWeight = _ReadSignedByteRange(ref reader, $"chroma_weight_l{list}", i, 1);
        crOffset = _ReadSignedByteRange(ref reader, $"chroma_offset_l{list}", i, 1);
      }
      entries[i] = new(lumaWeight, lumaOffset, cbWeight, cbOffset, crWeight, crOffset);
    }
    return entries;
  }

  private static int _ReadSignedByteRange(ref H264BitReader reader, string field, int index, int component) {
    var value = reader.ReadSignedExpGolomb();
    if (value is < -128 or > 127)
      throw new InvalidDataException(
        $"This H.264 slice states {field}[{index}]"
        + (component >= 0 ? $"[{component}]" : string.Empty)
        + $" as {value}; clause 7.4.3.2 bounds prediction weights and offsets at -128 through 127.");
    return value;
  }

  private static void _Apply(Span<byte> prediction, int weight, int offset, int log2Denom) {
    var round = log2Denom == 0 ? 0 : 1 << (log2Denom - 1);
    for (var i = 0; i < prediction.Length; ++i) {
      var value = ((weight * prediction[i] + round) >> log2Denom) + offset;
      prediction[i] = (byte)Math.Clamp(value, 0, 255);
    }
  }

  private static void _Combine(
    ReadOnlySpan<byte> first,
    ReadOnlySpan<byte> second,
    Span<byte> output,
    int weight0,
    int offset0,
    int weight1,
    int offset1,
    int log2Denom) {
    if (first.Length != second.Length || output.Length < first.Length)
      throw new ArgumentException("Weighted bi-prediction buffers must describe the same block.");
    var rounding = 1 << log2Denom;
    var offset = (offset0 + offset1 + 1) >> 1;
    for (var i = 0; i < first.Length; ++i) {
      var value = ((weight0 * first[i] + weight1 * second[i] + rounding) >> (log2Denom + 1)) + offset;
      output[i] = (byte)Math.Clamp(value, 0, 255);
    }
  }
}
