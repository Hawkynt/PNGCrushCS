using System;
using System.IO;

namespace FileFormat.Codecs.H263;

/// <summary>
/// Encodes one Annex O temporal B-picture using true two-reference bidirectional macroblocks.
/// </summary>
/// <remarks>
/// The encoder deliberately uses explicit Bi-dir macroblocks with zero forward and backward vectors.
/// That is still genuine Annex O bidirectional prediction: every predicted sample is the average of
/// the co-located past and future references, and the residual is coded against that average. Direct
/// mode is supported by the decoder, but choosing it here would couple a B macroblock to the future
/// anchor's motion field and is a rate decision rather than a requirement for interoperable B output.
/// </remarks>
internal sealed class H263BidirectionalPictureEncoder {

  private readonly int _width;
  private readonly int _height;
  private readonly int _sourceFormat;
  private readonly int _temporalReference;
  private readonly int _quantiser;
  private readonly H263Frame _source;
  private readonly H263Frame _past;
  private readonly H263Frame _future;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;

  internal H263BidirectionalPictureEncoder(
    int width,
    int height,
    int sourceFormat,
    int temporalReference,
    int quantiser,
    H263Frame source,
    H263Frame past,
    H263Frame future) {
    this._width = width;
    this._height = height;
    this._sourceFormat = sourceFormat;
    this._temporalReference = temporalReference;
    this._quantiser = quantiser;
    this._source = source ?? throw new ArgumentNullException(nameof(source));
    this._past = past ?? throw new ArgumentNullException(nameof(past));
    this._future = future ?? throw new ArgumentNullException(nameof(future));
    this._macroblockWidth = source.LumaWidth / 16;
    this._macroblockHeight = source.LumaHeight / 16;

    if (past.LumaWidth != source.LumaWidth || past.LumaHeight != source.LumaHeight
        || future.LumaWidth != source.LumaWidth || future.LumaHeight != source.LumaHeight)
      throw new InvalidDataException("An H.263 B-picture and both of its temporal references must have the same coded geometry.");
  }

  internal byte[] Encode() {
    var writer = new H263BitWriter();
    H263PlusHeaderWriter.Write(
      writer,
      this._width,
      this._height,
      this._sourceFormat,
      this._temporalReference,
      this._quantiser,
      H263PictureKind.Bidirectional);

    Span<int> levels = stackalloc int[6 * 64];
    Span<bool> coded = stackalloc bool[6];
    Span<int> prediction = stackalloc int[64];
    Span<int> backward = stackalloc int[64];
    Span<int> residual = stackalloc int[64];

    var count = this._macroblockWidth * this._macroblockHeight;
    for (var address = 0; address < count; ++address) {
      var luminancePattern = 0;
      var chrominancePattern = 0;

      for (var index = 0; index < 6; ++index) {
        this._PredictAverage(address, index, prediction, backward);
        this._ReadResidual(address, index, prediction, residual);

        var block = levels.Slice(index * 64, 64);
        coded[index] = H263BlockEncoder.QuantiseInter(residual, this._quantiser, block);
        if (!coded[index])
          continue;

        if (index < 4)
          luminancePattern |= 1 << (3 - index);
        else
          chrominancePattern |= 1 << (5 - index);
      }

      writer.WriteBit(0); // COD: an explicit macroblock follows.
      var hasTexture = luminancePattern != 0 || chrominancePattern != 0;
      writer.WriteCode(H263AnnexOVlc.BidirectionalMacroblockTypeCode(hasTexture ? 9 : 8));

      if (hasTexture) {
        writer.WriteCode(H263AnnexOVlc.ChromaPatternCode(chrominancePattern));
        // B-picture bidirectional macroblocks use the INTER interpretation of CBPY (O.4.4).
        writer.WriteCode(H263VlcWriter.LuminancePattern(luminancePattern ^ 0xF));
      }

      // Figure O.6 orders both vector fields after DQUANT. Every explicit vector is zero; since all
      // preceding explicit vectors are also zero, their same-direction median predictors are zero too.
      writer.WriteCode(H263VlcWriter.MotionVectorDifference(0));
      writer.WriteCode(H263VlcWriter.MotionVectorDifference(0));
      writer.WriteCode(H263VlcWriter.MotionVectorDifference(0));
      writer.WriteCode(H263VlcWriter.MotionVectorDifference(0));

      if (!hasTexture)
        continue;

      for (var index = 0; index < 6; ++index)
        if (coded[index])
          H263BlockEncoder.WriteInter(writer, levels.Slice(index * 64, 64));
    }

    return writer.ToArray();
  }

  private void _PredictAverage(int address, int index, Span<int> prediction, Span<int> backward) {
    var (pastPlane, pastWidth, _) = this._past.PlaneOf(index);
    var (futurePlane, futureWidth, _) = this._future.PlaneOf(index);
    var (left, top) = this._BlockOrigin(address, index);

    if (!H263MotionCompensation.TryPredict(
          prediction, pastPlane, pastWidth, left, top, 0, 0, clampToEdge: true)
        || !H263MotionCompensation.TryPredict(
          backward, futurePlane, futureWidth, left, top, 0, 0, clampToEdge: true))
      throw new InvalidOperationException("A zero-vector Annex O prediction unexpectedly left its reference picture.");

    for (var i = 0; i < 64; ++i)
      prediction[i] = (prediction[i] + backward[i]) >> 1;
  }

  private void _ReadResidual(
    int address, int index, ReadOnlySpan<int> prediction, Span<int> residual) {
    var (plane, width, _) = this._source.PlaneOf(index);
    var (left, top) = this._BlockOrigin(address, index);

    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 8; ++x)
      residual[y * 8 + x] = plane[(top + y) * width + left + x] - prediction[y * 8 + x];
  }

  private (int Left, int Top) _BlockOrigin(int address, int index) {
    var column = address % this._macroblockWidth;
    var row = address / this._macroblockWidth;
    return index < 4
      ? (column * 16 + (index & 1) * 8, row * 16 + (index >> 1) * 8)
      : (column * 8, row * 8);
  }
}
