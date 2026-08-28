using System;
using System.IO;

namespace FileFormat.Codecs.H264;

/// <summary>The explicit prediction weights for one entry of H.264 reference picture list 0.</summary>
internal readonly record struct H264ReferenceWeight(
  int LumaWeight,
  int LumaOffset,
  int CbWeight,
  int CbOffset,
  int CrWeight,
  int CrOffset);

/// <summary>
/// <c>pred_weight_table()</c> and the weighted-sample prediction process of H.264 clauses 7.3.3.2
/// and 8.4.2.3. The current decoder uses this for explicit weighted P prediction; the representation
/// deliberately keeps Cb and Cr separate so the same table can be extended to B list 1 without
/// changing the reconstruction API.
/// </summary>
internal sealed class H264PredictionWeights {

  private readonly H264ReferenceWeight[] _list0;

  private H264PredictionWeights(int lumaDenom, int chromaDenom, H264ReferenceWeight[] list0) {
    this.LumaLog2WeightDenom = lumaDenom;
    this.ChromaLog2WeightDenom = chromaDenom;
    this._list0 = list0;
  }

  internal int LumaLog2WeightDenom { get; }

  internal int ChromaLog2WeightDenom { get; }

  /// <summary>Reads the list-0 half of <c>pred_weight_table()</c> for a P slice.</summary>
  internal static H264PredictionWeights ParseP(
    ref H264BitReader reader,
    H264SequenceParameterSet sps,
    int numRefIdxL0Active) {
    var lumaDenom = reader.ReadUnsignedExpGolomb();
    if (lumaDenom > 7)
      throw new InvalidDataException(
        $"This H.264 slice states luma_log2_weight_denom {lumaDenom}; clause 7.4.3.2 bounds it at 0 through 7.");

    var hasChroma = !sps.SeparateColourPlaneFlag && sps.ChromaFormatIdc != 0;
    var chromaDenom = hasChroma ? reader.ReadUnsignedExpGolomb() : 0;
    if (chromaDenom > 7)
      throw new InvalidDataException(
        $"This H.264 slice states chroma_log2_weight_denom {chromaDenom}; clause 7.4.3.2 bounds it at 0 through 7.");

    var defaultLumaWeight = 1 << lumaDenom;
    var defaultChromaWeight = 1 << chromaDenom;
    var list0 = new H264ReferenceWeight[numRefIdxL0Active];

    for (var i = 0; i < list0.Length; ++i) {
      var lumaWeight = defaultLumaWeight;
      var lumaOffset = 0;
      if (reader.ReadBit() != 0) {
        lumaWeight = _ReadSignedByteRange(ref reader, "luma_weight_l0", i, -1);
        lumaOffset = _ReadSignedByteRange(ref reader, "luma_offset_l0", i, -1);
      }

      var cbWeight = defaultChromaWeight;
      var cbOffset = 0;
      var crWeight = defaultChromaWeight;
      var crOffset = 0;
      if (hasChroma && reader.ReadBit() != 0) {
        cbWeight = _ReadSignedByteRange(ref reader, "chroma_weight_l0", i, 0);
        cbOffset = _ReadSignedByteRange(ref reader, "chroma_offset_l0", i, 0);
        crWeight = _ReadSignedByteRange(ref reader, "chroma_weight_l0", i, 1);
        crOffset = _ReadSignedByteRange(ref reader, "chroma_offset_l0", i, 1);
      }

      list0[i] = new(lumaWeight, lumaOffset, cbWeight, cbOffset, crWeight, crOffset);
    }

    return new(lumaDenom, chromaDenom, list0);
  }

  /// <summary>Applies equations 8-215/8-216 to a luma prediction after interpolation and before residual addition.</summary>
  internal void ApplyLuma(int referenceIndex, Span<byte> prediction) {
    var weight = this._At(referenceIndex);
    _Apply(prediction, weight.LumaWeight, weight.LumaOffset, this.LumaLog2WeightDenom);
  }

  /// <summary>Applies the explicit list-0 weight to one chroma component after interpolation.</summary>
  internal void ApplyChroma(int referenceIndex, int component, Span<byte> prediction) {
    var weight = this._At(referenceIndex);
    if (component == 0)
      _Apply(prediction, weight.CbWeight, weight.CbOffset, this.ChromaLog2WeightDenom);
    else if (component == 1)
      _Apply(prediction, weight.CrWeight, weight.CrOffset, this.ChromaLog2WeightDenom);
    else
      throw new ArgumentOutOfRangeException(nameof(component));
  }

  private H264ReferenceWeight _At(int referenceIndex) {
    if ((uint)referenceIndex >= (uint)this._list0.Length)
      throw new InvalidDataException(
        $"An H.264 weighted prediction names list-0 reference {referenceIndex}, but its pred_weight_table carries "
        + $"only {this._list0.Length} entries.");

    return this._list0[referenceIndex];
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
}
