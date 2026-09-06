using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.Roq;

/// <summary>
/// Codes one picture into the two chunks RoQ states it as: a <c>QUAD_CODEBOOK</c> holding the cells it
/// needs and a <c>QUAD_VQ</c> holding the quadtree that names them.
/// </summary>
/// <remarks>
/// The mirror of <see cref="RoqPictureDecoder"/>, and written against it rather than against any other
/// encoder: every code emitted here is one that walk reads, in the order that walk reads it, and the
/// picture painted here is the picture that walk paints — which is what makes the two picture buffers
/// below not an implementation detail but the coding decision itself.
/// <para/>
/// <b>Two picture buffers, and what that makes a skip cost.</b> A <c>MOT</c> block writes nothing, so
/// what shows through is whatever the buffer being built already held — two pictures back, not one. An
/// encoder modelling a single buffer would price a skip against the picture immediately before and be
/// wrong about it on every other frame. So the alternation the decoder performs is performed here too,
/// and the price of a skip is measured against the target buffer's own stale content. <c>FCC</c> reads
/// the other buffer, the most recently completed picture, exactly as the decoder does.
/// <para/>
/// <b>What the decision costs.</b> Every 8x8 quadrant is priced at skip, motion, one 4x4 cell doubled to
/// fill it, and subdivision; every 4x4 block below a subdivision is priced at skip, motion, one 4x4 cell
/// at its own size, and four raw 2x2 cells. The price is the squared error the coding leaves over the
/// block's own samples — luminance and both chrominances, at the full resolution this format's motion
/// compensation leaves them at — plus a fixed number of error units for every bit the coding costs,
/// codebook cells included. That last term is what stops a picture spending 2560 bytes of codebook on a
/// 64x64 frame of 4096 pixels.
/// <para/>
/// <b>Sizing the codebook.</b> The farthest-point seeding is nested — the first <c>k</c> seeds of a
/// 256-seed run are the seeds a <c>k</c>-seed run would have picked — so one seeding gives every
/// codebook size worth trying at no extra cost, and the picture is priced at each of them. Only the
/// winning size is then refined by Lloyd's rule, and the refinement is kept only when it prices better:
/// a codebook fitting its training set more closely leaves the blocks free to choose again, and once in
/// a while what comes out is dearer than what went in.
/// <para/>
/// <b>An exact error, not a sampled one.</b> A 2x2 cell painted over a 4x4 or an 8x8 square spreads each
/// of its six numbers over an area of pixels, so what it leaves is <c>Σ(pixel − sample)²</c> over that
/// area, which expands to <c>n·sample² − 2·sample·Σpixel + Σpixel²</c>. Only the first two terms depend
/// on the cell, so a whole codebook can be priced against a block from six sums rather than from its
/// pixels — the same number the pixels would give, at a twentieth of the arithmetic.
/// </remarks>
internal sealed class RoqPictureEncoder {

  private const int _MACROBLOCK = 16;
  private const int _CB2_DIMENSIONS = 6;
  private const int _CB4_DIMENSIONS = 4 * _CB2_DIMENSIONS;
  private const int _CB4_INDICES = 4;

  /// <summary>The most cells either codebook may hold, being what one byte can name.</summary>
  internal const int CodebookSize = 256;

  private const int _CB2_BITS = 8 * _CB2_DIMENSIONS;
  private const int _CB4_BITS = 8 * _CB4_INDICES;

  /// <summary>What each coding costs in bits, its own two-bit code included.</summary>
  private const int _SKIP_BITS = 2;
  private const int _MOTION_BITS = 10;
  private const int _SOLID_BITS = 10;
  private const int _SUBDIVIDE_BITS = 2;
  private const int _FOUR_CELLS_BITS = 34;

  /// <summary>The codebook sizes the picture is priced at.</summary>
  /// <remarks>
  /// Spread by fours, which is what the seeding's own nesting makes free: a codebook four times the size
  /// costs four times the bytes, so the sizes worth trying are spread that way rather than evenly.
  /// </remarks>
  private static readonly int[] _CodebookSizes = [2, 8, 32, 128, 256];

  /// <summary>The furthest a motion vector reaches, being what one signed nibble states.</summary>
  private const int _MOTION_LOW = -8;
  private const int _MOTION_HIGH = 7;

  /// <summary>An argument byte that is a motion vector, and so is written as it stands.</summary>
  private const byte _PLAIN_ARGUMENT = 0;

  /// <summary>An argument byte naming a 4x4 cell, and so renumbered on the way out.</summary>
  private const byte _CB4_ARGUMENT = 1;

  /// <summary>An argument byte naming a 2x2 cell, and so renumbered on the way out.</summary>
  private const byte _CB2_ARGUMENT = 2;

  private readonly int _width;
  private readonly int _height;
  private readonly int _bitCost;
  private readonly int _quadrantsAcross;
  private readonly int _quadrantsDown;
  private readonly int _blocksAcross;
  private readonly int _blocksDown;

  private readonly RoqVectorQuantiser _cb2Quantiser = new();
  private readonly RoqVectorQuantiser _cb4Quantiser = new();

  private readonly byte[] _cb2Training;
  private readonly int _cb2TrainingCount;
  private readonly byte[] _cb4Training;
  private readonly int _cb4TrainingCount;

  private readonly byte[] _cb2Seeds = new byte[CodebookSize * _CB2_DIMENSIONS];
  private readonly byte[] _cb4Seeds = new byte[CodebookSize * _CB4_DIMENSIONS];
  private int _cb2SeedCount;
  private int _cb4SeedCount;

  /// <summary>The 2x2 cells as the codebook chunk states them: four luminances, then Cb and Cr.</summary>
  private readonly byte[] _cb2 = new byte[CodebookSize * _CB2_DIMENSIONS];

  /// <summary>The 4x4 cells while they are still samples rather than references.</summary>
  private readonly byte[] _cb4Vectors = new byte[CodebookSize * _CB4_DIMENSIONS];

  /// <summary>The 4x4 cells as the codebook chunk states them: four 2x2 cell numbers apiece.</summary>
  private readonly byte[] _cb4 = new byte[CodebookSize * _CB4_INDICES];
  private int _cb2Count;
  private int _cb4Count;

  /// <summary>How many 4x4 seed vectors were taken, before naming collapsed any of them together.</summary>
  private int _cb4VectorCount;

  private readonly int[] _skip8;
  private readonly int[] _skip4;
  private readonly int[] _motion8;
  private readonly int[] _motion4;
  private readonly byte[] _motion8Argument;
  private readonly byte[] _motion4Argument;

  private readonly int[][] _outerCosts = [new int[CodebookSize], new int[CodebookSize], new int[CodebookSize], new int[CodebookSize]];
  private readonly int[][] _innerCosts = [new int[CodebookSize], new int[CodebookSize], new int[CodebookSize], new int[CodebookSize]];

  private readonly byte[] _codes;
  private readonly byte[] _codeArguments;
  private readonly byte[] _argumentKinds;
  private readonly int[] _argumentValues;
  private int _codeCount;
  private int _argumentCount;

  private readonly bool[] _usedCb2 = new bool[CodebookSize];
  private readonly bool[] _usedCb4 = new bool[CodebookSize];

  /// <summary>What each of a subdivided quadrant's four blocks chose, held while the quadrant decides
  /// whether to take the subdivision at all.</summary>
  private readonly byte[] _childCode = new byte[4];
  private readonly byte[] _childArgumentCount = new byte[4];
  private readonly byte[] _childArgumentKind = new byte[4];
  private readonly int[] _childArgumentValues = new int[4 * _CB4_INDICES];

  internal RoqPictureEncoder(int width, int height, int bitCost) {
    this._width = width;
    this._height = height;
    this._bitCost = bitCost;
    this._quadrantsAcross = width / 8;
    this._quadrantsDown = height / 8;
    this._blocksAcross = width / 4;
    this._blocksDown = height / 4;

    this._cb2TrainingCount = width / 2 * (height / 2);
    this._cb2Training = new byte[this._cb2TrainingCount * _CB2_DIMENSIONS];
    this._cb4TrainingCount = this._blocksAcross * this._blocksDown + this._quadrantsAcross * this._quadrantsDown;
    this._cb4Training = new byte[this._cb4TrainingCount * _CB4_DIMENSIONS];

    this._skip8 = new int[this._quadrantsAcross * this._quadrantsDown];
    this._motion8 = new int[this._skip8.Length];
    this._motion8Argument = new byte[this._skip8.Length];
    this._skip4 = new int[this._blocksAcross * this._blocksDown];
    this._motion4 = new int[this._skip4.Length];
    this._motion4Argument = new byte[this._skip4.Length];

    // A macroblock states four quadrant codes and, where every one of them subdivides, sixteen more;
    // and a block that subdivides again is the walk's only code with four argument bytes.
    var macroblocks = width / _MACROBLOCK * (height / _MACROBLOCK);
    this._codes = new byte[macroblocks * 20];
    this._codeArguments = new byte[macroblocks * 20];
    this._argumentKinds = new byte[macroblocks * 20];
    this._argumentValues = new int[macroblocks * 16 * _CB4_INDICES];
  }

  /// <summary>How many 2x2 cells the codebook chunk just written holds.</summary>
  internal int Cb2Count { get; private set; }

  /// <summary>How many 4x4 cells the codebook chunk just written holds.</summary>
  internal int Cb4Count { get; private set; }

  /// <summary>Whether the picture just written leaves nothing at all to the pictures before it.</summary>
  internal bool IsWholePicture { get; private set; }

  /// <summary>
  /// Codes one picture, paints it into <paramref name="target"/> as the decoder will, and hands back the
  /// two chunk payloads.
  /// </summary>
  /// <param name="source">The picture wanted, as full-resolution samples.</param>
  /// <param name="reference">The most recently completed picture, which motion compensation reads.</param>
  /// <param name="target">The buffer being built, whose stale content a skipped block leaves showing.</param>
  /// <param name="intra">Whether to forbid the two codings that reach back, so the picture stands alone.</param>
  /// <param name="codebook">Where the <c>QUAD_CODEBOOK</c> payload goes, left empty when the picture
  /// names no cell at all and the codebook already loaded is to be left in place.</param>
  /// <param name="vectors">Where the <c>QUAD_VQ</c> payload goes.</param>
  internal void Encode(
    RoqFrame source, RoqFrame reference, RoqFrame target, bool intra,
    List<byte> codebook, List<byte> vectors) {
    this._BuildTraining(source);
    this._cb2SeedCount = this._cb2Quantiser.Seed(
      this._cb2Training, this._cb2TrainingCount, _CB2_DIMENSIONS, CodebookSize, this._cb2Seeds);
    this._cb4SeedCount = this._cb4Quantiser.Seed(
      this._cb4Training, this._cb4TrainingCount, _CB4_DIMENSIONS, CodebookSize, this._cb4Seeds);
    this._MeasureSkipAndMotion(source, reference, target, intra);

    var best = long.MaxValue;
    var bestSize = _CodebookSizes[0];
    foreach (var size in _CodebookSizes) {
      this._TakeSeeds(size);
      var score = this._Decide(source, intra);
      if (score < best) {
        best = score;
        bestSize = size;
      }

      // Once the seeding has run out of cells to pick — nothing left that some seed does not already
      // state exactly — a bigger size is the same codebook priced twice.
      if (this._cb2SeedCount < size && this._cb4SeedCount < size)
        break;
    }

    this._TakeSeeds(bestSize);
    this._Refine();
    if (this._Decide(source, intra) > best) {
      this._TakeSeeds(bestSize);
      this._Decide(source, intra);
    }

    this._Emit(codebook, vectors);
    this._Paint(reference, target);
  }

  // ============================================================================================
  // What the quantiser is fed
  // ============================================================================================

  /// <summary>
  /// One training vector per 2x2 cell of the picture, and one per 4x4 block and per 8x8 quadrant.
  /// </summary>
  /// <remarks>
  /// The 4x4 codebook is trained at both sizes it is used at. A 4x4 cell paints a 4x4 block at its own
  /// size and an 8x8 quadrant doubled, which are different pictures of the same area, so an 8x8 quadrant
  /// contributes the 4x4 picture it would be doubled from — every sample the mean of the square it
  /// covers. Training on the 4x4 blocks alone would size the codebook for half the decisions it has to
  /// serve.
  /// </remarks>
  private void _BuildTraining(RoqFrame source) {
    var at = 0;
    for (var y = 0; y < this._height; y += 2)
      for (var x = 0; x < this._width; x += 2, at += _CB2_DIMENSIONS)
        this._Cell(source, x, y, 1, this._cb2Training.AsSpan(at, _CB2_DIMENSIONS));

    at = 0;
    for (var y = 0; y < this._height; y += 4)
      for (var x = 0; x < this._width; x += 4, at += _CB4_DIMENSIONS)
        for (var quadrant = 0; quadrant < 4; ++quadrant)
          this._Cell(
            source, x + quadrant % 2 * 2, y + quadrant / 2 * 2, 1,
            this._cb4Training.AsSpan(at + quadrant * _CB2_DIMENSIONS, _CB2_DIMENSIONS));

    for (var y = 0; y < this._height; y += 8)
      for (var x = 0; x < this._width; x += 8, at += _CB4_DIMENSIONS)
        for (var quadrant = 0; quadrant < 4; ++quadrant)
          this._Cell(
            source, x + quadrant % 2 * 4, y + quadrant / 2 * 4, 2,
            this._cb4Training.AsSpan(at + quadrant * _CB2_DIMENSIONS, _CB2_DIMENSIONS));
  }

  /// <summary>
  /// The 2x2 cell coming closest to one square of the picture: four luminance means and one chrominance
  /// pair.
  /// </summary>
  /// <remarks>
  /// At <paramref name="scale"/> one the square is 2x2 and every mean is a single pixel. At two it is
  /// the 4x4 square a doubled cell covers, so each luminance is the mean of a 2x2 quarter of it and the
  /// chrominance pair the mean of all sixteen.
  /// </remarks>
  private void _Cell(RoqFrame source, int x, int y, int scale, Span<byte> cell) {
    var pixels = scale * scale;
    var blueSum = 0;
    var redSum = 0;

    for (var quadrant = 0; quadrant < 4; ++quadrant) {
      var left = x + quadrant % 2 * scale;
      var top = y + quadrant / 2 * scale;
      var lumaSum = 0;
      for (var row = 0; row < scale; ++row) {
        var offset = (top + row) * this._width + left;
        for (var column = 0; column < scale; ++column) {
          lumaSum += source.Y[offset + column];
          blueSum += source.Cb[offset + column];
          redSum += source.Cr[offset + column];
        }
      }

      cell[quadrant] = (byte)((lumaSum + pixels / 2) / pixels);
    }

    cell[4] = (byte)((blueSum + pixels * 2) / (pixels * 4));
    cell[5] = (byte)((redSum + pixels * 2) / (pixels * 4));
  }

  // ============================================================================================
  // The codebook
  // ============================================================================================

  /// <summary>Takes the first <paramref name="size"/> seeds of each codebook, which the farthest-point
  /// rule's nesting makes a codebook of that size.</summary>
  private void _TakeSeeds(int size) {
    this._cb2Count = Math.Min(size, this._cb2SeedCount);
    Array.Copy(this._cb2Seeds, this._cb2, this._cb2Count * _CB2_DIMENSIONS);

    this._cb4VectorCount = Math.Min(size, this._cb4SeedCount);
    Array.Copy(this._cb4Seeds, this._cb4Vectors, this._cb4VectorCount * _CB4_DIMENSIONS);
    this._MapCb4(this._cb4VectorCount);
  }

  /// <summary>
  /// Moves both codebooks onto the means of what they state best, and names the 4x4 cells again.
  /// </summary>
  /// <remarks>
  /// The 4x4 cells are refined as the samples they were seeded from rather than as the numbers they
  /// were reduced to, and only named again afterwards. Two cells that named the same four 2x2 cells
  /// before the move need not after it, so the naming is the last step and not a thing carried over.
  /// </remarks>
  private void _Refine() {
    this._cb2Quantiser.Refine(this._cb2Training, this._cb2TrainingCount, _CB2_DIMENSIONS, this._cb2Count, this._cb2);
    this._cb4Quantiser.Refine(
      this._cb4Training, this._cb4TrainingCount, _CB4_DIMENSIONS, this._cb4VectorCount, this._cb4Vectors);
    this._MapCb4(this._cb4VectorCount);
  }

  /// <summary>
  /// Turns 4x4 cells that are still samples into the four 2x2 cell numbers the format writes them as.
  /// </summary>
  /// <remarks>
  /// Two cells quantising onto the same four numbers are the same cell: the second states nothing the
  /// first does not and would cost four bytes to say it again, so it is dropped here rather than left
  /// for a decoder to paint twice.
  /// </remarks>
  private void _MapCb4(int count) {
    Span<int> seen = stackalloc int[CodebookSize];
    this._cb4Count = 0;

    for (var entry = 0; entry < count; ++entry) {
      var key = 0;
      for (var quadrant = 0; quadrant < _CB4_INDICES; ++quadrant) {
        var cell = RoqVectorQuantiser.Nearest(
          this._cb2, this._cb2Count, _CB2_DIMENSIONS,
          this._cb4Vectors.AsSpan(entry * _CB4_DIMENSIONS + quadrant * _CB2_DIMENSIONS, _CB2_DIMENSIONS), out _);
        this._cb4[this._cb4Count * _CB4_INDICES + quadrant] = (byte)cell;
        key = (key << 8) | cell;
      }

      var duplicate = false;
      for (var already = 0; already < this._cb4Count && !duplicate; ++already)
        duplicate = seen[already] == key;

      if (duplicate)
        continue;

      seen[this._cb4Count] = key;
      ++this._cb4Count;
    }
  }

  // ============================================================================================
  // What a skip and a motion vector cost, which no codebook changes
  // ============================================================================================

  private void _MeasureSkipAndMotion(RoqFrame source, RoqFrame reference, RoqFrame target, bool intra) {
    if (intra)
      return;

    for (var y = 0; y < this._quadrantsDown; ++y)
      for (var x = 0; x < this._quadrantsAcross; ++x) {
        var at = y * this._quadrantsAcross + x;
        this._skip8[at] = this._SquaredError(source, target, x * 8, y * 8, x * 8, y * 8, 8);
        this._motion8[at] = this._Search(source, reference, x * 8, y * 8, 8, out this._motion8Argument[at]);
      }

    for (var y = 0; y < this._blocksDown; ++y)
      for (var x = 0; x < this._blocksAcross; ++x) {
        var at = y * this._blocksAcross + x;
        this._skip4[at] = this._SquaredError(source, target, x * 4, y * 4, x * 4, y * 4, 4);
        this._motion4[at] = this._Search(source, reference, x * 4, y * 4, 4, out this._motion4Argument[at]);
      }
  }

  /// <summary>
  /// The best whole-pixel motion vector for one block, and what it leaves.
  /// </summary>
  /// <remarks>
  /// A full search of the sixteen-by-sixteen window one signed nibble states, cut short at every
  /// candidate the moment it passes the best already found, and begun at the null vector so that a block
  /// that has not moved is priced first and ties go to it. Vectors whose source would leave the picture
  /// are not tried at all: the decoder refuses those rather than clamping, so an encoder writing one
  /// would be writing a file it cannot read back.
  /// </remarks>
  private int _Search(RoqFrame source, RoqFrame reference, int x, int y, int n, out byte argument) {
    var best = this._SquaredError(source, reference, x, y, x, y, n);
    argument = (byte)((-_MOTION_LOW << 4) | -_MOTION_LOW);

    for (var dy = _MOTION_LOW; dy <= _MOTION_HIGH; ++dy) {
      var sourceY = y - dy;
      if (sourceY < 0 || sourceY + n > this._height)
        continue;

      for (var dx = _MOTION_LOW; dx <= _MOTION_HIGH; ++dx) {
        if (dx == 0 && dy == 0)
          continue;

        var sourceX = x - dx;
        if (sourceX < 0 || sourceX + n > this._width)
          continue;

        var error = this._SquaredError(source, reference, x, y, sourceX, sourceY, n, best);
        if (error >= best)
          continue;

        best = error;
        argument = (byte)(((dx - _MOTION_LOW) << 4) | (dy - _MOTION_LOW));
      }
    }

    return best;
  }

  private int _SquaredError(RoqFrame source, RoqFrame other, int x, int y, int otherX, int otherY, int n, int ceiling = int.MaxValue) {
    var total = 0;
    for (var row = 0; row < n; ++row) {
      var here = (y + row) * this._width + x;
      var there = (otherY + row) * this._width + otherX;
      for (var column = 0; column < n; ++column) {
        var luma = source.Y[here + column] - other.Y[there + column];
        var blue = source.Cb[here + column] - other.Cb[there + column];
        var red = source.Cr[here + column] - other.Cr[there + column];
        total += luma * luma + blue * blue + red * red;
      }

      if (total >= ceiling)
        return ceiling;
    }

    return total;
  }

  // ============================================================================================
  // Pricing the codebook against one square
  // ============================================================================================

  /// <summary>
  /// What every 2x2 cell in the codebook would cost, in squared error, painted over one square.
  /// </summary>
  /// <remarks>
  /// The square is <c>2·scale</c> across. Each of a cell's four luminances covers a
  /// <c>scale</c>-by-<c>scale</c> quarter of it and its chrominance pair covers the whole, so what the
  /// cell leaves is <c>Σ(pixel − sample)²</c> over each of those areas — which expands to a constant plus
  /// terms in the sample and the area's own sum. The constant is the same for every cell but is kept in,
  /// because this price is compared against a skip's and a motion vector's, and those are real squared
  /// errors rather than differences of one.
  /// </remarks>
  private void _FillCosts(RoqFrame source, int x, int y, int scale, int[] costs) {
    Span<int> lumaSums = stackalloc int[4];
    var pixels = scale * scale;
    var blueSum = 0;
    var redSum = 0;
    var constant = 0;

    for (var quadrant = 0; quadrant < 4; ++quadrant) {
      var left = x + quadrant % 2 * scale;
      var top = y + quadrant / 2 * scale;
      var lumaSum = 0;
      for (var row = 0; row < scale; ++row) {
        var offset = (top + row) * this._width + left;
        for (var column = 0; column < scale; ++column) {
          int luma = source.Y[offset + column];
          int blue = source.Cb[offset + column];
          int red = source.Cr[offset + column];
          lumaSum += luma;
          blueSum += blue;
          redSum += red;
          constant += luma * luma + blue * blue + red * red;
        }
      }

      lumaSums[quadrant] = lumaSum;
    }

    var chromaPixels = pixels * 4;
    for (var entry = 0; entry < this._cb2Count; ++entry) {
      var at = entry * _CB2_DIMENSIONS;
      var total = constant;
      for (var quadrant = 0; quadrant < 4; ++quadrant) {
        int sample = this._cb2[at + quadrant];
        total += pixels * sample * sample - 2 * sample * lumaSums[quadrant];
      }

      int blue = this._cb2[at + 4];
      int red = this._cb2[at + 5];
      total += chromaPixels * blue * blue - 2 * blue * blueSum;
      total += chromaPixels * red * red - 2 * red * redSum;
      costs[entry] = total;
    }
  }

  /// <summary>The cheapest 4x4 cell for a square already priced cell by cell, and what it costs.</summary>
  private int _BestCb4(int[][] costs, out int chosen) {
    chosen = 0;
    var best = int.MaxValue;

    for (var entry = 0; entry < this._cb4Count; ++entry) {
      var at = entry * _CB4_INDICES;
      var total = costs[0][this._cb4[at]] + costs[1][this._cb4[at + 1]]
                  + costs[2][this._cb4[at + 2]] + costs[3][this._cb4[at + 3]];
      if (total >= best)
        continue;

      best = total;
      chosen = entry;
    }

    return best;
  }

  private int _BestCb2(int[] costs, out int chosen) {
    chosen = 0;
    var best = int.MaxValue;

    for (var entry = 0; entry < this._cb2Count; ++entry) {
      if (costs[entry] >= best)
        continue;

      best = costs[entry];
      chosen = entry;
    }

    return best;
  }

  // ============================================================================================
  // Deciding the picture
  // ============================================================================================

  /// <summary>Prices and records every code of one picture, and returns what the whole of it costs.</summary>
  private long _Decide(RoqFrame source, bool intra) {
    this._codeCount = 0;
    this._argumentCount = 0;
    Array.Clear(this._usedCb2);
    Array.Clear(this._usedCb4);
    this.IsWholePicture = true;

    long total = 0;
    for (var macroblockY = 0; macroblockY < this._height / _MACROBLOCK; ++macroblockY)
      for (var macroblockX = 0; macroblockX < this._width / _MACROBLOCK; ++macroblockX) {
        var left = macroblockX * _MACROBLOCK;
        var top = macroblockY * _MACROBLOCK;
        for (var quadrant = 0; quadrant < 4; ++quadrant)
          total += this._Quadrant(source, left + quadrant % 2 * 8, top + quadrant / 2 * 8, intra);
      }

    var cells = 0;
    var quads = 0;
    for (var entry = 0; entry < this._cb4Count; ++entry) {
      if (!this._usedCb4[entry])
        continue;

      ++quads;
      for (var quadrant = 0; quadrant < _CB4_INDICES; ++quadrant)
        this._usedCb2[this._cb4[entry * _CB4_INDICES + quadrant]] = true;
    }

    for (var entry = 0; entry < this._cb2Count; ++entry)
      if (this._usedCb2[entry])
        ++cells;

    return total + (long)this._bitCost * (cells * _CB2_BITS + quads * _CB4_BITS);
  }

  /// <summary>One 8x8 quadrant: skip, motion, one 4x4 cell doubled to fill it, or subdivision.</summary>
  private long _Quadrant(RoqFrame source, int x, int y, bool intra) {
    var at = y / 8 * this._quadrantsAcross + x / 8;

    for (var quadrant = 0; quadrant < 4; ++quadrant)
      this._FillCosts(source, x + quadrant % 2 * 4, y + quadrant / 2 * 4, 2, this._outerCosts[quadrant]);

    var solid = this._BestCb4(this._outerCosts, out var solidCell) + (long)this._bitCost * _SOLID_BITS;
    var skip = intra ? long.MaxValue : this._skip8[at] + (long)this._bitCost * _SKIP_BITS;
    var motion = intra ? long.MaxValue : this._motion8[at] + (long)this._bitCost * _MOTION_BITS;

    var subdivided = (long)this._bitCost * _SUBDIVIDE_BITS;
    for (var child = 0; child < 4; ++child)
      subdivided += this._Child(source, x + child % 2 * 4, y + child / 2 * 4, child, intra);

    if (skip <= motion && skip <= solid && skip <= subdivided) {
      this._Put(0, 0, _PLAIN_ARGUMENT);
      this.IsWholePicture = false;
      return skip;
    }

    if (motion <= solid && motion <= subdivided) {
      this._Put(1, 1, _PLAIN_ARGUMENT);
      this._argumentValues[this._argumentCount++] = this._motion8Argument[at];
      this.IsWholePicture = false;
      return motion;
    }

    if (solid <= subdivided) {
      this._Put(2, 1, _CB4_ARGUMENT);
      this._argumentValues[this._argumentCount++] = solidCell;
      this._usedCb4[solidCell] = true;
      return solid;
    }

    this._Put(3, 0, _PLAIN_ARGUMENT);
    for (var child = 0; child < 4; ++child) {
      var arguments = this._childArgumentCount[child];
      this._Put(this._childCode[child], arguments, this._childArgumentKind[child]);
      for (var argument = 0; argument < arguments; ++argument)
        this._argumentValues[this._argumentCount++] = this._childArgumentValues[child * _CB4_INDICES + argument];

      switch (this._childCode[child]) {
        case 0:
        case 1:
          this.IsWholePicture = false;
          break;
        case 2:
          this._usedCb4[this._childArgumentValues[child * _CB4_INDICES]] = true;
          break;
        default:
          for (var argument = 0; argument < _CB4_INDICES; ++argument)
            this._usedCb2[this._childArgumentValues[child * _CB4_INDICES + argument]] = true;

          break;
      }
    }

    return subdivided;
  }

  /// <summary>One 4x4 block below a subdivision: skip, motion, one 4x4 cell at its own size, or four raw
  /// 2x2 cells — the walk's one terminal case, which carries no code of its own for them.</summary>
  private long _Child(RoqFrame source, int x, int y, int child, bool intra) {
    var at = y / 4 * this._blocksAcross + x / 4;

    for (var quadrant = 0; quadrant < 4; ++quadrant)
      this._FillCosts(source, x + quadrant % 2 * 2, y + quadrant / 2 * 2, 1, this._innerCosts[quadrant]);

    var solid = this._BestCb4(this._innerCosts, out var solidCell) + (long)this._bitCost * _SOLID_BITS;

    var cells = (long)this._bitCost * _FOUR_CELLS_BITS;
    Span<int> chosen = stackalloc int[4];
    for (var quadrant = 0; quadrant < 4; ++quadrant) {
      cells += this._BestCb2(this._innerCosts[quadrant], out var cell);
      chosen[quadrant] = cell;
    }

    var skip = intra ? long.MaxValue : this._skip4[at] + (long)this._bitCost * _SKIP_BITS;
    var motion = intra ? long.MaxValue : this._motion4[at] + (long)this._bitCost * _MOTION_BITS;

    if (skip <= motion && skip <= solid && skip <= cells) {
      this._childCode[child] = 0;
      this._childArgumentCount[child] = 0;
      this._childArgumentKind[child] = _PLAIN_ARGUMENT;
      return skip;
    }

    if (motion <= solid && motion <= cells) {
      this._childCode[child] = 1;
      this._childArgumentCount[child] = 1;
      this._childArgumentKind[child] = _PLAIN_ARGUMENT;
      this._childArgumentValues[child * _CB4_INDICES] = this._motion4Argument[at];
      return motion;
    }

    if (solid <= cells) {
      this._childCode[child] = 2;
      this._childArgumentCount[child] = 1;
      this._childArgumentKind[child] = _CB4_ARGUMENT;
      this._childArgumentValues[child * _CB4_INDICES] = solidCell;
      return solid;
    }

    this._childCode[child] = 3;
    this._childArgumentCount[child] = _CB4_INDICES;
    this._childArgumentKind[child] = _CB2_ARGUMENT;
    for (var quadrant = 0; quadrant < _CB4_INDICES; ++quadrant)
      this._childArgumentValues[child * _CB4_INDICES + quadrant] = chosen[quadrant];

    return cells;
  }

  private void _Put(byte code, byte arguments, byte kind) {
    this._codes[this._codeCount] = code;
    this._codeArguments[this._codeCount] = arguments;
    this._argumentKinds[this._codeCount] = kind;
    ++this._codeCount;
  }

  // ============================================================================================
  // Writing it out
  // ============================================================================================

  /// <summary>
  /// Renumbers both codebooks down to what the picture actually names and writes the two payloads.
  /// </summary>
  /// <remarks>
  /// A codebook chunk states its cells from nought and says how many follow, so there is no way to write
  /// cell 200 without writing the 200 before it. Cells are given their new numbers in the order the codes
  /// first ask for them, and a picture naming no cell at all writes no codebook chunk — the one the
  /// decoder already holds is left alone, which is what a run of motion-compensated pictures does in
  /// every real file.
  /// </remarks>
  private void _Emit(List<byte> codebook, List<byte> vectors) {
    Span<int> cb2Map = stackalloc int[CodebookSize];
    Span<int> cb4Map = stackalloc int[CodebookSize];
    cb2Map.Fill(-1);
    cb4Map.Fill(-1);

    var cells = 0;
    var quads = 0;

    var argument = 0;
    for (var code = 0; code < this._codeCount; ++code) {
      var arguments = this._codeArguments[code];
      switch (this._argumentKinds[code]) {
        case _CB4_ARGUMENT: {
          var entry = this._argumentValues[argument];
          if (cb4Map[entry] < 0) {
            cb4Map[entry] = quads++;
            for (var quadrant = 0; quadrant < _CB4_INDICES; ++quadrant) {
              var cell = this._cb4[entry * _CB4_INDICES + quadrant];
              if (cb2Map[cell] < 0)
                cb2Map[cell] = cells++;
            }
          }

          break;
        }

        case _CB2_ARGUMENT:
          for (var quadrant = 0; quadrant < arguments; ++quadrant) {
            var cell = this._argumentValues[argument + quadrant];
            if (cb2Map[cell] < 0)
              cb2Map[cell] = cells++;
          }

          break;
      }

      argument += arguments;
    }

    this.Cb2Count = cells;
    this.Cb4Count = quads;

    if (cells > 0) {
      // A codebook stating 2x2 cells and no 4x4 ones is legal, but a count of nought is spelled the
      // same way a count of 256 is and only the chunk's own length tells them apart. One 4x4 cell costs
      // four bytes and leaves nothing to tell apart.
      if (quads == 0) {
        quads = 1;
        this.Cb4Count = 1;
      }

      var written = new byte[cells * _CB2_DIMENSIONS + quads * _CB4_INDICES];
      for (var entry = 0; entry < this._cb2Count; ++entry)
        if (cb2Map[entry] >= 0)
          Array.Copy(this._cb2, entry * _CB2_DIMENSIONS, written, cb2Map[entry] * _CB2_DIMENSIONS, _CB2_DIMENSIONS);

      var quadsAt = cells * _CB2_DIMENSIONS;
      for (var entry = 0; entry < this._cb4Count; ++entry) {
        if (cb4Map[entry] < 0)
          continue;

        for (var quadrant = 0; quadrant < _CB4_INDICES; ++quadrant)
          written[quadsAt + cb4Map[entry] * _CB4_INDICES + quadrant] =
            (byte)cb2Map[this._cb4[entry * _CB4_INDICES + quadrant]];
      }

      codebook.AddRange(written);
    }

    this._WriteVectors(vectors, cb2Map, cb4Map);
  }

  /// <summary>
  /// Writes the quadtree: sixteen bits of codes, then the argument bytes those eight codes asked for,
  /// and again until the picture is accounted for.
  /// </summary>
  /// <remarks>
  /// The code stream and the argument bytes share one stream, which is the one thing a writer of this
  /// format has to get right: a decoder refills its code register only when it runs dry, so a code's
  /// argument byte belongs after the whole word that code sits in and not immediately after the code
  /// itself. A last word holding fewer than eight codes is written with the unused pairs at nought;
  /// nothing reads them.
  /// </remarks>
  private void _WriteVectors(List<byte> vectors, ReadOnlySpan<int> cb2Map, ReadOnlySpan<int> cb4Map) {
    Span<byte> pending = stackalloc byte[8 * _CB4_INDICES];
    var pendingCount = 0;
    var word = 0;
    var codes = 0;
    var argument = 0;

    for (var code = 0; code < this._codeCount; ++code) {
      word |= this._codes[code] << (14 - 2 * codes);
      ++codes;

      var arguments = this._codeArguments[code];
      var kind = this._argumentKinds[code];
      for (var index = 0; index < arguments; ++index) {
        var value = this._argumentValues[argument + index];
        pending[pendingCount++] = kind switch {
          _CB4_ARGUMENT => (byte)cb4Map[value],
          _CB2_ARGUMENT => (byte)cb2Map[value],
          _ => (byte)value,
        };
      }

      argument += arguments;
      if (codes < 8)
        continue;

      _Flush(vectors, word, pending[..pendingCount]);
      word = 0;
      codes = 0;
      pendingCount = 0;
    }

    if (codes > 0)
      _Flush(vectors, word, pending[..pendingCount]);
  }

  private static void _Flush(List<byte> vectors, int word, ReadOnlySpan<byte> pending) {
    vectors.Add((byte)word);
    vectors.Add((byte)(word >> 8));
    foreach (var value in pending)
      vectors.Add(value);
  }

  // ============================================================================================
  // Painting what was written
  // ============================================================================================

  /// <summary>Paints the picture the codes state into the buffer the decoder will build it in.</summary>
  private void _Paint(RoqFrame reference, RoqFrame target) {
    var code = 0;
    var argument = 0;

    for (var macroblockY = 0; macroblockY < this._height / _MACROBLOCK; ++macroblockY)
      for (var macroblockX = 0; macroblockX < this._width / _MACROBLOCK; ++macroblockX) {
        var left = macroblockX * _MACROBLOCK;
        var top = macroblockY * _MACROBLOCK;
        for (var quadrant = 0; quadrant < 4; ++quadrant) {
          var x = left + quadrant % 2 * 8;
          var y = top + quadrant / 2 * 8;
          switch (this._codes[code++]) {
            case 0:
              break;
            case 1:
              this._Move(reference, target, x, y, 8, this._argumentValues[argument++]);
              break;
            case 2:
              this._PaintCb4(target, x, y, 2, this._argumentValues[argument++]);
              break;
            default:
              for (var child = 0; child < 4; ++child) {
                var childX = x + child % 2 * 4;
                var childY = y + child / 2 * 4;
                switch (this._codes[code++]) {
                  case 0:
                    break;
                  case 1:
                    this._Move(reference, target, childX, childY, 4, this._argumentValues[argument++]);
                    break;
                  case 2:
                    this._PaintCb4(target, childX, childY, 1, this._argumentValues[argument++]);
                    break;
                  default:
                    for (var cell = 0; cell < _CB4_INDICES; ++cell)
                      this._PaintCb2(target, childX + cell % 2 * 2, childY + cell / 2 * 2, 1, this._argumentValues[argument++]);

                    break;
                }
              }

              break;
          }
        }
      }
  }

  private void _Move(RoqFrame reference, RoqFrame target, int x, int y, int n, int argument) {
    var sourceX = x - ((argument >> 4 & 0xF) + _MOTION_LOW);
    var sourceY = y - ((argument & 0xF) + _MOTION_LOW);

    for (var row = 0; row < n; ++row) {
      var into = (y + row) * this._width + x;
      var from = (sourceY + row) * this._width + sourceX;
      Array.Copy(reference.Y, from, target.Y, into, n);
      Array.Copy(reference.Cb, from, target.Cb, into, n);
      Array.Copy(reference.Cr, from, target.Cr, into, n);
    }
  }

  /// <summary>Paints a 4x4 cell at its own size or doubled, which is its four 2x2 cells at that scale.</summary>
  private void _PaintCb4(RoqFrame target, int x, int y, int scale, int entry) {
    for (var quadrant = 0; quadrant < _CB4_INDICES; ++quadrant)
      this._PaintCb2(
        target, x + quadrant % 2 * 2 * scale, y + quadrant / 2 * 2 * scale, scale,
        this._cb4[entry * _CB4_INDICES + quadrant]);
  }

  /// <summary>Paints one 2x2 cell over a square <c>2·scale</c> across: each luminance over a
  /// <c>scale</c>-by-<c>scale</c> quarter of it, the chrominance pair over the whole.</summary>
  private void _PaintCb2(RoqFrame target, int x, int y, int scale, int entry) {
    var at = entry * _CB2_DIMENSIONS;
    var blue = this._cb2[at + 4];
    var red = this._cb2[at + 5];

    for (var quadrant = 0; quadrant < 4; ++quadrant) {
      var luma = this._cb2[at + quadrant];
      var left = x + quadrant % 2 * scale;
      var top = y + quadrant / 2 * scale;
      for (var row = 0; row < scale; ++row) {
        var offset = (top + row) * this._width + left;
        for (var column = 0; column < scale; ++column) {
          target.Y[offset + column] = luma;
          target.Cb[offset + column] = blue;
          target.Cr[offset + column] = red;
        }
      }
    }
  }
}
