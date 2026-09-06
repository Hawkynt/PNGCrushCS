namespace FileFormat.Codecs.MsMpeg4;

/// <summary>
/// The prediction version 3 puts on the luminance half of an intra picture's coded block pattern.
/// </summary>
/// <remarks>
/// Version 3 states all six coded block pattern bits of an intra picture's macroblock in one codeword,
/// and the four luminance ones are stated as the exclusive-or of what the block does with what its
/// neighbours did — the block to the left where the two above agree with each other, and the block
/// above where they do not. It is the same idea as the DC prediction and a different rule, and it is
/// why an intra picture of version 3 cannot be read a macroblock at a time without keeping a grid of
/// what every block did.
/// <para/>
/// The grid is in 8x8 block coordinates rather than macroblock ones, with a row above and a column
/// left of the picture that stay nought. That border is what makes a block at the edge predict from
/// nothing instead of from the far end of the row above, and keeping it as real storage rather than as
/// a run of edge tests is what keeps the predictor three array reads.
/// <para/>
/// Nothing resets it at a slice boundary. That is deliberate and it is what the reference decoder
/// does: the DC and the coefficients stop at a slice, and this does not.
/// </remarks>
internal sealed class MsMpeg4CodedBlockPrediction {

  private readonly byte[] _coded;
  private readonly int _stride;
  private readonly int _macroblockWidth;

  internal MsMpeg4CodedBlockPrediction(int macroblockWidth, int macroblockHeight) {
    this._macroblockWidth = macroblockWidth;
    this._stride = 2 * macroblockWidth + 1;
    this._coded = new byte[this._stride * (2 * macroblockHeight + 1)];
  }

  /// <summary>What the neighbours say this block's coded flag should be.</summary>
  internal int Predict(int address, int block) {
    var at = this._At(address, block);
    var left = this._coded[at - 1];
    var aboveLeft = this._coded[at - 1 - this._stride];
    var above = this._coded[at - this._stride];

    return aboveLeft == above ? left : above;
  }

  /// <summary>Records what the block actually did, for the blocks after it.</summary>
  internal void Record(int address, int block, int coded) => this._coded[this._At(address, block)] = (byte)coded;

  private int _At(int address, int block) {
    var column = address % this._macroblockWidth;
    var row = address / this._macroblockWidth;

    return (2 * row + (block >> 1) + 1) * this._stride + 2 * column + (block & 1) + 1;
  }
}
