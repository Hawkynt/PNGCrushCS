using System;
using System.IO;

namespace FileFormat.Codecs.DnxHd;

/// <summary>
/// One 8×8 block, from its entropy codewords to samples in a plane.
/// </summary>
/// <remarks>
/// The five steps of SMPTE ST 2019-1:2016, Figure 42, done together because nothing else wants any
/// of the intermediates: entropy decoding (8.2.1), the inverse zig-zag (8.2.6), inverse quantisation
/// (8.2.7), the inverse transform (8.2.8.1) and the level adjustment (8.2.8.3).
/// <para/>
/// <b>The DC coefficient is not quantised</b> — 8.2.7 says so outright — so nothing is applied to it
/// on the way back and Annex D prints a dash where its weight would be. What it is instead is
/// predicted: 8.2.4 keeps a running value per component type, reset at the start of each macroblock
/// scan line, and each block's DC is the previous one plus a correction. That reset is what makes a
/// scan line independently decodable, which is the same property the scan index table exists for.
/// <para/>
/// <b>The DC codeword names a length, not a value.</b> It says how many bits follow, and those bits
/// are the correction — biased so that the top half of the range is positive and the bottom half
/// negative, which is what the comparison against <c>2^(η−1)</c> in 8.2.4 does.
/// </remarks>
internal sealed class DnxHdBlockDecoder {

  private readonly DnxHdVlcTable _dc;
  private readonly DnxHdVlcTable _amplitude;
  private readonly DnxHdVlcTable _run;
  private readonly byte[] _lumaWeights;
  private readonly byte[] _chromaWeights;
  private readonly int _divisor;
  private readonly int _bitDepth;
  private readonly int _indexBits;
  private readonly int _levelAdjustment;
  private readonly int _lowestSample;
  private readonly int _highestSample;

  internal DnxHdBlockDecoder(DnxHdFrameHeader header) {
    var group = header.Compression.VlcGroup;
    var weights = header.Compression.WeightTable;

    this._dc = DnxHdVlcTable.From(DnxHdVlcTables.DcLengths[group], DnxHdVlcTables.DcBitCounts[group]);
    this._amplitude = DnxHdVlcTable.From(DnxHdVlcTables.AmplitudeLengths[group], DnxHdVlcTables.AmplitudeSymbols[group]);
    this._run = DnxHdVlcTable.From(DnxHdVlcTables.RunLengths[group], DnxHdVlcTables.RunSymbols[group]);

    this._lumaWeights = DnxHdWeightTables.Luma[weights];
    this._chromaWeights = DnxHdWeightTables.Chroma[weights];
    this._divisor = header.Compression.InverseQuantisationDivisor;
    this._bitDepth = header.BitDepth;

    // Annex A.1: the amplitude index is six bits at ten and twelve bits of sampling and four at
    // eight. Reading the wrong width puts every large coefficient of the block wrong and then loses
    // the bitstream alignment, so it is settled from the depth once rather than guessed per block.
    this._indexBits = header.BitDepth == 8 ? 4 : 6;

    // 8.2.8.3: the transform's output is signed and centred on zero, so half the range is added back.
    this._levelAdjustment = 1 << (header.BitDepth - 1);
    this._lowestSample = 1 << (header.BitDepth - 8);
    this._highestSample = (1 << header.BitDepth) - 1 - this._lowestSample;
  }

  /// <summary>Reads one block and writes its samples into a plane.</summary>
  /// <param name="bits">The compressed payload, positioned at this block's first codeword.</param>
  /// <param name="chroma">Whether to quantise with the colour-difference weights.</param>
  /// <param name="quantisationScale">The macroblock's <c>qsf</c>, from its 12-bit header (8.2.2).</param>
  /// <param name="prediction">The running DC prediction for this block's component type.</param>
  /// <param name="target">The plane the samples are written to.</param>
  /// <param name="planeWidth">The width of that plane in samples.</param>
  /// <param name="planeHeight">The height of that plane; rows past it are discarded.</param>
  /// <param name="originX">The block's left column in the plane.</param>
  /// <param name="originY">The block's top row in the plane.</param>
  internal void Decode(
    DnxHdBitReader bits,
    bool chroma,
    int quantisationScale,
    ref int prediction,
    ushort[] target,
    int planeWidth,
    int planeHeight,
    int originX,
    int originY) {
    Span<double> block = stackalloc double[64];
    block.Clear();

    var weights = chroma ? this._chromaWeights : this._lumaWeights;

    // 8.2.4 — the DC, predicted from the previous block of this component type.
    prediction += this._Correction(bits);
    block[0] = prediction;

    // 8.2.5 — the AC coefficients, run-length coded.
    //
    // The loop ends at the end-of-block codeword and at nothing else. Figure 29 puts an EOB after
    // every block's codewords without exception, so a block that fills all sixty-three AC
    // coefficients carries one too — and the informative decoding pseudo-code of Figure 47 does not
    // show that, because its loop simply runs out at r = 64 and stops reading. Following the
    // pseudo-code instead of the figure leaves one codeword unread in exactly those blocks, and from
    // there the whole macroblock scan line is a bit out of step. Measured on a 1920x1080 frame:
    // 52 of its 68 scan lines fail to decode at all, and the 16 that survive are the ones that
    // happened to contain no such block.
    var r = 1;
    while (true) {
      var symbol = this._amplitude.Read(bits);
      if (DnxHdAmplitude.IsEndOfBlock(symbol))
        break;

      var amplitude = DnxHdAmplitude.Value(symbol);

      // Annex A.2 fixes the order of what follows the codeword, and it is not the order the decoding
      // pseudo-code of Figure 47 lists its steps in: the sign bit comes first, then the amplitude
      // index if there is one, then the zero run if there is one.
      var negative = bits.Bit() != 0;

      if (DnxHdAmplitude.HasIndex(symbol))
        amplitude += bits.Bits(this._indexBits) * 64;

      if (DnxHdAmplitude.HasRun(symbol))
        r += this._run.Read(bits);

      if (r >= 64)
        throw new InvalidDataException(
          $"A VC-3 block coded a run reaching coefficient {r} of 64, so its coding unit is damaged.");

      // Annex D's tables are printed as an 8x8 grid in raster order, so a weight is looked up by
      // where the coefficient sits in the block and not by where it sat in the bitstream. Indexing
      // them by the zig-zag position instead is a mistake that decodes every block and gets every
      // one of them slightly wrong: measured on a 1920x1080 frame it moves samples by up to 49
      // levels of 255 where the raster indexing moves none by more than 3.
      var position = DnxHdScan.RasterPosition[r];
      block[position] = this._Dequantise(negative ? -amplitude : amplitude, weights[position], quantisationScale);
      ++r;
    }

    DnxHdInverseDct.Transform(block);

    for (var j = 0; j < 8; ++j) {
      var row = originY + j;
      if (row >= planeHeight)
        break;

      var into = row * planeWidth + originX;
      for (var i = 0; i < 8; ++i) {
        var sample = (int)Math.Round(block[j * 8 + i], MidpointRounding.AwayFromZero) + this._levelAdjustment;
        target[into + i] = (ushort)(sample < this._lowestSample ? this._lowestSample
          : sample > this._highestSample ? this._highestSample
          : sample);
      }
    }
  }

  /// <summary>
  /// The DC prediction correction of SMPTE ST 2019-1:2016, 8.2.4 and Figure 46.
  /// </summary>
  /// <remarks>
  /// A codeword gives η, the number of bits that follow; those bits are ρ. A ρ in the top half of
  /// its range stands for itself and one in the bottom half for a negative value, which packs
  /// <c>±(2^η − 1)</c> down to <c>±2^(η−1)</c> into η bits with no sign bit of its own. A η of zero
  /// is a correction of zero and carries no bits at all.
  /// </remarks>
  private int _Correction(DnxHdBitReader bits) {
    var length = this._dc.Read(bits);
    if (length == 0)
      return 0;

    var value = bits.Bits(length);

    return value >= 1 << (length - 1) ? value : value + 1 - (1 << length);
  }

  /// <summary>
  /// Inverse quantisation of one AC coefficient, SMPTE ST 2019-1:2016, 8.2.7, equation (8.1).
  /// </summary>
  /// <remarks>
  /// The rounding term is the part worth writing out. Half the product of the weight and the scale
  /// factor is added before the divide, which rounds to nearest rather than towards zero — and a
  /// further half of the divisor is added on top, but <i>only</i> where the weight and the divisor
  /// differ. That exception is in the standard and it is not obviously a rounding rule at all; it is
  /// simply what the equation says, and leaving it out shifts a fraction of the coefficients of
  /// every block by one step of the quantiser.
  /// <para/>
  /// The magnitude is quantised and the sign reapplied afterwards, so the rounding is symmetric
  /// about zero rather than biased in one direction the way a signed divide would make it.
  /// </remarks>
  private int _Dequantise(int quantised, int weight, int quantisationScale) {
    if (quantised == 0)
      return 0;

    var magnitude = quantised < 0 ? -quantised : quantised;
    var step = weight * quantisationScale;
    var scaled = magnitude * step + step / 2;

    // The exception in the standard's own equation, and it is not obviously a rounding rule at all:
    // half the divisor is added on top, but only where the weight and the divisor differ. Leaving it
    // out, or applying it always, both decode every block and get some of them wrong — measured
    // against ffmpeg on the same frames, one raises the worst disagreement from 5 levels of 255 to 7
    // and the other to 4 while differing on half again as many samples. It is simply what (8.1)
    // says.
    if (weight != this._divisor)
      scaled += this._divisor / 2;

    var value = scaled / this._divisor;

    return quantised < 0 ? -value : value;
  }
}
