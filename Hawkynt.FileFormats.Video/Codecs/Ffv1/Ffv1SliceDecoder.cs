using System;
using System.IO;

namespace FileFormat.Codecs.Ffv1;

/// <summary>
/// Decodes the samples of one slice: the context model, the median predictor and the two coders
/// (RFC 9043 §3.2 to §3.8).
/// </summary>
/// <remarks>
/// Every sample is predicted from three of its neighbours by their median, and the difference is
/// coded in a context chosen by five more. The context is a mixed-radix packing of five quantised
/// differences — left minus above-left, above-left minus above, above minus above-right, the sample
/// two to the left minus the left, and the sample two above minus the above — so that a flat area, an
/// edge and a gradient each get their own statistics. A context and its negative are the same
/// context with the difference's sign turned round, which halves how many there are.
/// <para/>
/// The two coders share all of that and differ only in how the difference itself is written. The
/// range coder spends thirty-two adaptive states per context on it. Golomb-Rice spends four running
/// numbers, and adds a run mode: a context of exactly zero means the prediction has been perfect,
/// and a picture with flat areas is mostly that, so those runs are coded by length instead of a
/// symbol a sample.
/// </remarks>
internal sealed class Ffv1SliceDecoder {

  private readonly Ffv1Parameters _parameters;
  private readonly int[][][] _quantTables;
  private readonly int _sampleBits;
  private readonly int _sampleMask;

  internal Ffv1SliceDecoder(Ffv1Parameters parameters) {
    this._parameters = parameters;
    this._quantTables = parameters.QuantTables;
    this._sampleBits = parameters.SampleBits;
    this._sampleMask = (1 << parameters.SampleBits) - 1;
  }

  /// <summary>Decodes one plane's worth of lines with the range coder.</summary>
  internal void DecodePlane(Ffv1RangeCoder coder, Ffv1Plane plane, byte[][] states, int tableSet) {
    for (var y = 0; y < plane.Height; ++y)
      this._DecodeLineRange(coder, plane, y, states, tableSet);
  }

  /// <summary>Decodes one plane's worth of lines with the Golomb-Rice coder.</summary>
  internal void DecodePlane(Ffv1GolombDecoder golomb, Ffv1Plane plane, Ffv1GolombState[] states, int tableSet, ref int runIndex) {
    for (var y = 0; y < plane.Height; ++y)
      this._DecodeLineGolomb(golomb, plane, y, states, tableSet, ref runIndex);
  }

  /// <summary>Decodes one line with the range coder.</summary>
  internal void DecodeLine(Ffv1RangeCoder coder, Ffv1Plane plane, int y, byte[][] states, int tableSet)
    => this._DecodeLineRange(coder, plane, y, states, tableSet);

  /// <summary>Decodes one line with the Golomb-Rice coder.</summary>
  internal void DecodeLine(Ffv1GolombDecoder golomb, Ffv1Plane plane, int y, Ffv1GolombState[] states, int tableSet, ref int runIndex)
    => this._DecodeLineGolomb(golomb, plane, y, states, tableSet, ref runIndex);

  private void _DecodeLineRange(Ffv1RangeCoder coder, Ffv1Plane plane, int y, byte[][] states, int tableSet) {
    var tables = this._quantTables[tableSet];

    for (var x = 0; x < plane.Width; ++x) {
      var left = plane.At(x - 1, y);
      var top = plane.At(x, y - 1);
      var topLeft = plane.At(x - 1, y - 1);

      var context = _Context(tables, plane, x, y, left, top, topLeft);
      var negative = context < 0;
      if (negative)
        context = -context;

      if (context >= states.Length)
        throw new InvalidDataException($"A sample states context {context}, where the table set it uses has {states.Length}.");

      var difference = coder.Symbol(states[context], true);
      if (negative)
        difference = -difference;

      plane[x, y] = (_Median(left, top, left + top - topLeft) + difference) & this._sampleMask;
    }
  }

  private void _DecodeLineGolomb(Ffv1GolombDecoder golomb, Ffv1Plane plane, int y, Ffv1GolombState[] states, int tableSet, ref int runIndex) {
    var tables = this._quantTables[tableSet];
    var runMode = 0;
    var runCount = 0;

    for (var x = 0; x < plane.Width; ++x) {
      var left = plane.At(x - 1, y);
      var top = plane.At(x, y - 1);
      var topLeft = plane.At(x - 1, y - 1);

      var context = _Context(tables, plane, x, y, left, top, topLeft);
      var negative = context < 0;
      if (negative)
        context = -context;

      if (context >= states.Length)
        throw new InvalidDataException($"A sample states context {context}, where the table set it uses has {states.Length}.");

      int difference;
      if (context == 0 && runMode == 0)
        runMode = 1;

      if (runMode != 0) {
        if (runCount == 0 && runMode == 1) {
          if (golomb.Bit() != 0) {
            runCount = 1 << Ffv1GolombDecoder.Log2Run(runIndex);
            if (x + runCount <= plane.Width && runIndex < Ffv1GolombDecoder.MaximumRunIndex)
              ++runIndex;
          } else {
            var length = Ffv1GolombDecoder.Log2Run(runIndex);
            runCount = length != 0 ? golomb.Bits(length) : 0;
            if (runIndex != 0)
              --runIndex;

            runMode = 2;
          }
        }

        --runCount;
        if (runCount < 0) {
          runMode = 0;
          runCount = 0;
          difference = golomb.Symbol(states[context], this._sampleBits);

          // Zero cannot occur where a run has just ended, so it is taken out of the alphabet and
          // everything at or above it moves up one.
          if (difference >= 0)
            ++difference;
        } else
          difference = 0;
      } else
        difference = golomb.Symbol(states[context], this._sampleBits);

      if (negative)
        difference = -difference;

      plane[x, y] = (_Median(left, top, left + top - topLeft) + difference) & this._sampleMask;
    }
  }

  /// <summary>
  /// The context a sample's difference is coded in: five quantised neighbour differences, packed.
  /// </summary>
  /// <remarks>
  /// Each of the five is looked up in its own table and the results added, and because each table's
  /// entries were already multiplied by the range of the tables before it, adding them cannot make
  /// two different neighbourhoods land on the same number.
  /// </remarks>
  private static int _Context(int[][] tables, Ffv1Plane plane, int x, int y, int left, int top, int topLeft) {
    var topRight = plane.At(x + 1, y - 1);
    var leftLeft = plane.At(x - 2, y);
    var topTop = plane.At(x, y - 2);

    return tables[0][(left - topLeft) & 0xFF]
           + tables[1][(topLeft - top) & 0xFF]
           + tables[2][(top - topRight) & 0xFF]
           + tables[3][(leftLeft - left) & 0xFF]
           + tables[4][(topTop - top) & 0xFF];
  }

  private static int _Median(int a, int b, int c) {
    if (a > b)
      (a, b) = (b, a);

    return c < a ? a : c > b ? b : c;
  }
}
