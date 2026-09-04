namespace FileFormat.Codecs.Ffv1;

/// <summary>
/// Codes the samples of one slice with the range coder: the same context model and median
/// predictor <see cref="Ffv1SliceDecoder"/> reads with, run the other way (RFC 9043 §3.2 to §3.8).
/// </summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/ffv1enc_template.c</c>, copyright (c) 2003-2016 Michael
/// Niedermayer, LGPL-2.1-or-later; this adaptation is distributed with PNGCrushCS under
/// LGPL-3.0-or-later.
/// <para/>
/// Nothing here is new: the context of a sample and the median it is predicted from are the
/// decoder's own routines, called on the same <see cref="Ffv1Plane"/> with the same border rules, so
/// the two cannot drift apart. What the encoder adds is the fold — the difference is taken modulo
/// the sample width and put into the signed half of that range, so a step from 250 to 5 costs the
/// same as one from 5 to 10 rather than being written out as minus two hundred and forty-five.
/// </remarks>
internal sealed class Ffv1SliceEncoder {

  private readonly int[][][] _quantTables;
  private readonly int _foldShift;

  internal Ffv1SliceEncoder(Ffv1Parameters parameters) {
    this._quantTables = parameters.QuantTables;
    this._foldShift = 32 - parameters.SampleBits;
  }

  /// <summary>Codes every line of a plane, one after the other.</summary>
  internal void EncodePlane(Ffv1RangeEncoder coder, Ffv1Plane plane, byte[][] states, int tableSet) {
    for (var y = 0; y < plane.Height; ++y)
      this.EncodeLine(coder, plane, y, states, tableSet);
  }

  /// <summary>Codes one line of a plane whose samples are already in place.</summary>
  internal void EncodeLine(Ffv1RangeEncoder coder, Ffv1Plane plane, int y, byte[][] states, int tableSet) {
    var tables = this._quantTables[tableSet];

    for (var x = 0; x < plane.Width; ++x) {
      var left = plane.At(x - 1, y);
      var top = plane.At(x, y - 1);
      var topLeft = plane.At(x - 1, y - 1);

      var context = Ffv1SliceDecoder.ContextOf(tables, plane, x, y, left, top, topLeft);
      var difference = plane[x, y] - Ffv1SliceDecoder.Median(left, top, left + top - topLeft);

      if (context < 0) {
        context = -context;
        difference = -difference;
      }

      difference = (difference << this._foldShift) >> this._foldShift;
      coder.Symbol(states[context], difference, true);
    }
  }
}
