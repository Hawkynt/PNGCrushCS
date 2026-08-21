namespace FileFormat.Codecs.Vp8;

/// <summary>
/// The six numbers a decoded coefficient is multiplied by, and the header fields they come from
/// (RFC 6386, 9.6 and 14.1).
/// </summary>
/// <remarks>
/// Six and not one: the plane (luma, chroma, or the Y2 block of luma DC values) and the position
/// (the first coefficient or any of the other fifteen) between them pick one of six factors. Five of
/// the six are written as a delta from the sixth, which is the only one always present.
/// <para/>
/// The two lookups are not linear, and the Y2 and chroma factors are not simply looked up. Y2 DC is
/// twice the DC lookup, Y2 AC is the AC lookup multiplied by 155 and divided by 100 with a floor of
/// eight, and chroma DC is capped at 132. Those three adjustments are in RFC 6386 section 14.1 and
/// its reference decoder; getting any of them wrong scales a whole plane's residue and shows up as
/// a picture that is right in shape and wrong in contrast.
/// </remarks>
internal sealed class Vp8Quantiser {

  internal const int BLOCK_TYPE_LUMA = 0;
  internal const int BLOCK_TYPE_CHROMA = 1;
  internal const int BLOCK_TYPE_Y2 = 2;

  internal int BaseIndex;

  private int _lumaDcDelta;
  private int _y2DcDelta;
  private int _y2AcDelta;
  private int _chromaDcDelta;
  private int _chromaAcDelta;

  /// <summary>Factors laid out as <c>(segment * 3 + blockType) * 2 + (position == 0 ? 0 : 1)</c>.</summary>
  private readonly short[] _factors = new short[Vp8Segmentation.SEGMENT_COUNT * 3 * 2];

  internal void Parse(ref Vp8BoolDecoder reader) {
    this.BaseIndex = reader.ReadLiteral(7);
    this._lumaDcDelta = _ReadOptionalSignedValue(ref reader);
    this._y2DcDelta = _ReadOptionalSignedValue(ref reader);
    this._y2AcDelta = _ReadOptionalSignedValue(ref reader);
    this._chromaDcDelta = _ReadOptionalSignedValue(ref reader);
    this._chromaAcDelta = _ReadOptionalSignedValue(ref reader);
  }

  /// <summary>Works out the factors for every segment, once the segmentation for this frame is known.</summary>
  internal void Build(Vp8Segmentation segmentation) {
    for (var segment = 0; segment < Vp8Segmentation.SEGMENT_COUNT; ++segment) {
      var index = segmentation.QuantiserIndexFor(segment, this.BaseIndex);
      var at = segment * 6;

      this._factors[at + BLOCK_TYPE_LUMA * 2] = _DcFactor(index + this._lumaDcDelta);
      this._factors[at + BLOCK_TYPE_LUMA * 2 + 1] = _AcFactor(index);

      var chromaDc = _DcFactor(index + this._chromaDcDelta);
      this._factors[at + BLOCK_TYPE_CHROMA * 2] = chromaDc > 132 ? (short)132 : chromaDc;
      this._factors[at + BLOCK_TYPE_CHROMA * 2 + 1] = _AcFactor(index + this._chromaAcDelta);

      this._factors[at + BLOCK_TYPE_Y2 * 2] = (short)(_DcFactor(index + this._y2DcDelta) * 2);
      var y2Ac = _AcFactor(index + this._y2AcDelta) * 155 / 100;
      this._factors[at + BLOCK_TYPE_Y2 * 2 + 1] = (short)(y2Ac < 8 ? 8 : y2Ac);
    }
  }

  /// <summary>The factor for one segment, plane and position.</summary>
  internal short Factor(int segment, int blockType, int isAlternating)
    => this._factors[(segment * 3 + blockType) * 2 + isAlternating];

  private static short _DcFactor(int index) => Vp8Tables.DcQuantiser[_Clamp(index)];

  private static short _AcFactor(int index) => Vp8Tables.AcQuantiser[_Clamp(index)];

  private static int _Clamp(int index) => index < 0 ? 0 : index > 127 ? 127 : index;

  private static int _ReadOptionalSignedValue(ref Vp8BoolDecoder reader)
    => reader.ReadFlag() != 0 ? reader.ReadSignedValue(4) : 0;
}
