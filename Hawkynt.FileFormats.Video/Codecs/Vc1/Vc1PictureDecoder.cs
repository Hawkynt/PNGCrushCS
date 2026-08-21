using System;
using System.IO;

namespace FileFormat.Codecs.Vc1;

/// <summary>
/// Decodes one progressive intra-coded picture of a Simple or Main profile stream (SMPTE 421M 8.1).
/// </summary>
/// <remarks>
/// The macroblocks are walked in raster order and each is six blocks — four luma, then Cb, then Cr.
/// Everything a block needs beyond its own bits comes from its neighbours: which of the six carry AC
/// coefficients is predicted from the blocks around it, the DC is a difference from one of three
/// adjacent DCs, and with AC prediction on, a whole row or column of coefficients is a difference too.
/// That is why the picture is decoded in one pass with the quantised coefficients kept as it goes,
/// rather than block by block in isolation.
/// </remarks>
internal sealed class Vc1PictureDecoder {

  private static readonly Vc1VlcTable _Cbpcy = new("I-Picture CBPCY", Vc1Tables.IPictureCbpcy);

  private readonly Vc1SequenceHeader _sequence;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly byte[] _lumaCoded;
  private readonly Vc1EscapeState _escape = new();

  internal Vc1PictureDecoder(Vc1SequenceHeader sequence, int macroblockWidth, int macroblockHeight) {
    this._sequence = sequence;
    this._macroblockWidth = macroblockWidth;
    this._macroblockHeight = macroblockHeight;
    this._lumaCoded = new byte[macroblockWidth * macroblockHeight * 4];
  }

  /// <summary>Decodes one intra picture into a frame.</summary>
  internal Vc1Frame Decode(ReadOnlySpan<byte> data, Vc1PictureHeader header, out Vc1PictureHeader read) {
    read = header;

    var frame = new Vc1Frame(this._macroblockWidth, this._macroblockHeight);
    var reader = new Vc1BitReader(data);
    read = Vc1PictureHeader.ReadFrom(ref reader, this._sequence);

    if (read.PictureType != Vc1PictureType.Intra)
      throw new NotSupportedException(
        $"This decoder reads intra pictures of the Simple and Main profiles; this picture is {_Name(read.PictureType)}.");

    this.DecodeIntra(ref reader, read, frame);
    this.BitsConsumed = reader.BitPosition;
    this.BitsAvailable = data.Length << 3;
    return frame;
  }

  /// <summary>
  /// How many bits the last picture took, and how many it had.
  /// </summary>
  /// <remarks>
  /// A picture that decodes correctly ends within a byte of its packet, since the encoder pads to a
  /// byte boundary and nothing follows. A decode that has gone out of step almost always ends a long
  /// way from the end, so the gap between these two is the cheapest test there is for whether the
  /// bitstream was read the way it was written.
  /// </remarks>
  internal int BitsConsumed { get; private set; }

  internal int BitsAvailable { get; private set; }

  private static string _Name(Vc1PictureType type) => type switch {
    Vc1PictureType.Predicted => "predicted (a P picture)",
    Vc1PictureType.Bidirectional => "bidirectionally predicted (a B or BI picture)",
    Vc1PictureType.Skipped => "skipped",
    _ => "intra",
  };

  internal void DecodeIntra(ref Vc1BitReader reader, Vc1PictureHeader header, Vc1Frame frame) {
    Array.Clear(this._lumaCoded);
    this._escape.Reset();

    var quantiser = header.Quantiser;
    var doubleQuant = (2 * quantiser) + (header.HalfStep ? 1 : 0);
    var dcStepSize = quantiser switch {
      1 or 2 => 2 * quantiser,
      3 or 4 => 8,
      _ => (quantiser / 2) + 6,
    };

    // The smoothing filter and the DC seed are two halves of one decision. With smoothing on, a block
    // outside the picture predicts from nought and the constant 128 is added at the end; with it off,
    // the seed carries that offset itself and nothing is added (8.1.3.2, 8.1.3.10).
    var smoothing = this._sequence.Overlap && quantiser >= 9;
    var defaultPredictor = smoothing ? 0 : (1024 + (dcStepSize >> 1)) / dcStepSize;

    var luma = new Vc1IntraPrediction(this._macroblockWidth * 2, this._macroblockHeight * 2);
    var cb = new Vc1IntraPrediction(this._macroblockWidth, this._macroblockHeight);
    var cr = new Vc1IntraPrediction(this._macroblockWidth, this._macroblockHeight);

    var lumaSet = Vc1AcCodingSet.For(header.LumaCodingSetIndex, luma: true, header.QuantiserIndex);
    var chromaSet = Vc1AcCodingSet.For(header.ChromaCodingSetIndex, luma: false, header.QuantiserIndex);
    var lumaDc = Vc1BlockDecoder.DcTable(header.HighMotionDcTable, luma: true);
    var chromaDc = Vc1BlockDecoder.DcTable(header.HighMotionDcTable, luma: false);

    // Table 59 is used where the quantiser is fine enough that levels need eleven bits to carry.
    var conservativeEscape = quantiser <= 7;

    Span<int> ordered = stackalloc int[64];
    Span<int> block = stackalloc int[64];

    for (var mbY = 0; mbY < this._macroblockHeight; ++mbY)
      for (var mbX = 0; mbX < this._macroblockWidth; ++mbX) {
        var pattern = this._ReadCodedBlockPattern(ref reader, mbX, mbY);
        var acPrediction = reader.ReadBit() != 0;

        for (var i = 0; i < 6; ++i) {
          var isLuma = i < 4;
          var prediction = isLuma ? luma : i == 4 ? cb : cr;
          var column = isLuma ? (mbX * 2) + (i & 1) : mbX;
          var row = isLuma ? (mbY * 2) + (i >> 1) : mbY;

          var (dcPredictor, fromTop) = prediction.Predict(column, row, defaultPredictor);

          ordered.Clear();
          var differential = Vc1BlockDecoder.ReadDcDifferential(
            ref reader, isLuma ? lumaDc : chromaDc, quantiser);
          ordered[0] = dcPredictor + differential;

          // A block the pattern says is not coded still has a DC, and still predicts for its
          // neighbours; what it has none of is AC coefficients.
          var coded = (pattern & (1 << (5 - i))) != 0;
          if (coded)
            Vc1BlockDecoder.ReadAcCoefficients(
              ref reader, isLuma ? lumaSet : chromaSet, this._escape, quantiser, conservativeEscape, ordered);

          Vc1BlockDecoder.InverseScan(ordered, Vc1BlockDecoder.ScanFor(acPrediction, fromTop), block);

          if (acPrediction)
            _AddAcPrediction(block, prediction, column, row, fromTop);

          prediction.Store(column, row, block);

          _Dequantise(block, doubleQuant, quantiser, dcStepSize, header.UniformQuantiser);
          Vc1InverseTransform.Apply(block);
          _Place(frame, block, i, mbX, mbY);
        }
      }

    if (smoothing) {
      Vc1OverlapSmoothing.Apply(frame.Luma, frame.LumaWidth, frame.LumaHeight);
      Vc1OverlapSmoothing.Apply(frame.Cb, frame.ChromaWidth, frame.ChromaHeight);
      Vc1OverlapSmoothing.Apply(frame.Cr, frame.ChromaWidth, frame.ChromaHeight);
    }

    _Finish(frame.Luma, smoothing);
    _Finish(frame.Cb, smoothing);
    _Finish(frame.Cr, smoothing);
  }

  /// <summary>
  /// Adds the neighbouring block's edge coefficients to this one's (Figure 48).
  /// </summary>
  /// <remarks>
  /// The direction is the one the DC prediction chose. Where there is no block that way — the top row
  /// predicting upwards, the left column predicting leftwards — the predictor is nought rather than a
  /// default, because there is nothing there to have been differenced against.
  /// </remarks>
  private static void _AddAcPrediction(Span<int> block, Vc1IntraPrediction prediction, int column, int row, bool fromTop) {
    if (fromTop) {
      if (row == 0)
        return;

      var above = prediction.Top(column, row - 1);
      for (var i = 0; i < 7; ++i)
        block[i + 1] += above[i];

      return;
    }

    if (column == 0)
      return;

    var left = prediction.Left(column - 1, row);
    for (var i = 0; i < 7; ++i)
      block[(i + 1) * 8] += left[i];
  }

  /// <summary>Turns quantised coefficients into transform coefficients (8.1.3.3, 8.1.3.8).</summary>
  private static void _Dequantise(Span<int> block, int doubleQuant, int quantiser, int dcStepSize, bool uniform) {
    block[0] *= dcStepSize;

    for (var i = 1; i < 64; ++i) {
      var value = block[i];
      if (value == 0)
        continue;

      // The nonuniform quantiser has a dead zone around nought, so reconstructing a level puts it a
      // further half step away from zero in whichever direction it already lay.
      block[i] = uniform
        ? value * doubleQuant
        : (value * doubleQuant) + (value < 0 ? -quantiser : quantiser);
    }
  }

  /// <summary>Writes one reconstructed block into its plane.</summary>
  private static void _Place(Vc1Frame frame, ReadOnlySpan<int> block, int index, int mbX, int mbY) {
    var (samples, stride) = frame.PlaneOf(index);
    var x = index < 4 ? (mbX * 16) + ((index & 1) * 8) : mbX * 8;
    var y = index < 4 ? (mbY * 16) + ((index >> 1) * 8) : mbY * 8;

    for (var row = 0; row < 8; ++row) {
      var to = ((y + row) * stride) + x;
      for (var column = 0; column < 8; ++column)
        samples[to + column] = block[(row * 8) + column];
    }
  }

  /// <summary>
  /// Turns the reconstruction into samples: the constant 128 where it is owed, then clamping (8.1.3.10).
  /// </summary>
  /// <remarks>
  /// Where overlap smoothing did not run, 128 is not added — the DC seed carried the offset instead,
  /// and adding it here as well would wash out every picture the sequence chose not to smooth.
  /// </remarks>
  private static void _Finish(int[] plane, bool smoothing) {
    var offset = smoothing ? 128 : 0;
    for (var i = 0; i < plane.Length; ++i) {
      var value = plane[i] + offset;
      plane[i] = value < 0 ? 0 : value > 255 ? 255 : value;
    }
  }

  /// <summary>
  /// Reads the coded block pattern and undoes the prediction the encoder applied to its luma bits
  /// (8.1.2.1, Figure 34).
  /// </summary>
  /// <remarks>
  /// Only the four luma bits are predicted; the two colour-difference bits are stated outright. The
  /// prediction is from the coded status of the neighbouring luma blocks, which is why the status of
  /// every macroblock has to be kept for the row below it to read.
  /// </remarks>
  private int _ReadCodedBlockPattern(ref Vc1BitReader reader, int mbX, int mbY) {
    var decoded = _Cbpcy.Read(ref reader);

    var left = mbX > 0;
    var above = mbY > 0;
    var aboveLeft = left && above;

    var l1 = left ? this._Coded(mbX - 1, mbY, 1) : 0;
    var l3 = left ? this._Coded(mbX - 1, mbY, 3) : 0;
    var t2 = above ? this._Coded(mbX, mbY - 1, 2) : 0;
    var t3 = above ? this._Coded(mbX, mbY - 1, 3) : 0;
    var lt3 = aboveLeft ? this._Coded(mbX - 1, mbY - 1, 3) : 0;

    var y0 = (lt3 == t2 ? l1 : t2) ^ ((decoded >> 5) & 1);
    var y1 = (t2 == t3 ? y0 : t3) ^ ((decoded >> 4) & 1);
    var y2 = (l1 == y0 ? l3 : y0) ^ ((decoded >> 3) & 1);
    var y3 = (y0 == y1 ? y2 : y1) ^ ((decoded >> 2) & 1);

    var at = ((mbY * this._macroblockWidth) + mbX) * 4;
    this._lumaCoded[at] = (byte)y0;
    this._lumaCoded[at + 1] = (byte)y1;
    this._lumaCoded[at + 2] = (byte)y2;
    this._lumaCoded[at + 3] = (byte)y3;

    return (y0 << 5) | (y1 << 4) | (y2 << 3) | (y3 << 2) | (decoded & 0x03);
  }

  private int _Coded(int mbX, int mbY, int block) => this._lumaCoded[(((mbY * this._macroblockWidth) + mbX) * 4) + block];
}
