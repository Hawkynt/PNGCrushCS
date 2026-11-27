using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.Svq1;

/// <summary>An MSB-first bit buffer that can be cut back to an earlier length and appended to another.</summary>
internal sealed class Svq1BitWriter {
  private readonly List<byte> _bits = new(4096);

  public int Count => this._bits.Count;

  public void Write(int count, uint value) {
    for (var i = count - 1; i >= 0; --i)
      this._bits.Add((byte)((value >> i) & 1));
  }

  public void Truncate(int count) => this._bits.RemoveRange(count, this._bits.Count - count);

  public void Append(Svq1BitWriter other) => this._bits.AddRange(other._bits);

  public byte[] ToBytes() {
    var bytes = new byte[(this._bits.Count + 7) / 8];
    for (var i = 0; i < this._bits.Count; ++i)
      if (this._bits[i] != 0)
        bytes[i >> 3] |= (byte)(0x80 >> (i & 7));
    return bytes;
  }
}

/// <summary>
/// Encodes Sorenson Video 1: multi-stage vector quantisation searched over the quadtree, with skip,
/// half-sample motion and intra macroblocks chosen by distortion plus a bit cost.
/// </summary>
/// <remarks>
/// The quadtree search, <see cref="_EncodeBlock"/>, is converted from FFmpeg's
/// <c>libavcodec/svq1enc.c</c> <c>encode_block</c> (Michael Niedermayer), LGPL-2.1-or-later: it tries
/// one to six codebook stages and the mean alone for every vector, then whether splitting the vector in
/// two codes it more cheaply, writing each level's bits into a buffer of its own because the decoder
/// reads the tree breadth first. FFmpeg's motion estimation rests on its MPEG encoder and is not taken:
/// the vector search here is this package's own, whole-sample over ±8 and then half-sample around the
/// best. The temporal reference counts up, so the decoder's swap of the ±128 means for streams that
/// leave it at zero never applies, and the reference each frame predicts from is what the decoder makes
/// of the frame before.
/// </remarks>
internal sealed class Svq1Encoder {

  private const int _SEARCH = 8;

  private static readonly short[][] _IntraSums = Array.ConvertAll(Svq1Tables.IntraCodebooks, _Sums);
  private static readonly short[][] _InterSums = Array.ConvertAll(Svq1Tables.InterCodebooks, _Sums);

  private static short[] _Sums(sbyte[] codebook) {
    var size = codebook.Length / 96;
    var sums = new short[96];
    for (var v = 0; v < 96; ++v) {
      var sum = 0;
      for (var j = 0; j < size; ++j)
        sum += codebook[v * size + j];
      sums[v] = (short)sum;
    }
    return sums;
  }

  private readonly int _width;
  private readonly int _height;
  private readonly int _lambda;
  private readonly Svq1Decoder _decoder;
  private readonly Svq1BitWriter[] _levels = [new(), new(), new(), new(), new(), new()];
  private readonly short[][][] _encoded = [.. new short[6][][]];
  private int _frameNumber;

  internal Svq1Encoder(int width, int height, int lambda) {
    this._width = width;
    this._height = height;
    this._lambda = lambda;
    this._decoder = new(width, height, hasExtraData: false);
    for (var level = 0; level < 6; ++level) {
      this._encoded[level] = new short[7][];
      for (var stage = 0; stage < 7; ++stage)
        this._encoded[level][stage] = new short[256];
    }
  }

  /// <summary>Codes one picture given as display-size Y and quarter-size U and V planes.</summary>
  internal byte[] Encode(byte[] luma, byte[] cb, byte[] cr, int chromaWidth, int chromaHeight, bool keyFrame) {
    var previous = this._decoder.LastPlanes;
    if (previous == null)
      keyFrame = true;

    var output = new Svq1BitWriter();
    output.Write(22, 0x20);
    output.Write(8, (uint)(this._frameNumber & 0xFF));
    output.Write(2, keyFrame ? 0u : 1u);
    if (keyFrame) {
      output.Write(5, 2);
      var sizeCode = Array.IndexOf(Svq1Tables.FrameSizes, (this._width, this._height));
      if (sizeCode < 0) {
        output.Write(3, 7);
        output.Write(12, (uint)this._width);
        output.Write(12, (uint)this._height);
      } else {
        output.Write(3, (uint)sizeCode);
      }
    }
    output.Write(2, 0);

    var sources = new[] { (luma, this._width, this._height), (cb, chromaWidth, chromaHeight), (cr, chromaWidth, chromaHeight) };
    for (var i = 0; i < 3; ++i) {
      var codedWidth = i == 0 ? (this._width + 15) & ~15 : ((this._width / 4) + 15) & ~15;
      var codedHeight = i == 0 ? (this._height + 15) & ~15 : ((this._height / 4) + 15) & ~15;
      var source = _Pad(sources[i].Item1, sources[i].Item2, sources[i].Item3, i == 0 ? this._width : this._width / 4,
        i == 0 ? this._height : this._height / 4, codedWidth, codedHeight);
      this._EncodePlane(output, source, codedWidth, codedHeight, keyFrame ? null : previous![i]);
    }

    while ((output.Count & 31) != 0)
      output.Write(1, 0);

    var packet = output.ToBytes();
    this._decoder.Decode(packet);
    ++this._frameNumber;
    return packet;
  }

  /// <summary>The plane at its coded size: the coded part of the source, its last row and column repeated.</summary>
  private static byte[] _Pad(byte[] source, int sourceWidth, int sourceHeight, int width, int height, int codedWidth, int codedHeight) {
    var result = new byte[codedWidth * codedHeight];
    for (var y = 0; y < codedHeight; ++y)
    for (var x = 0; x < codedWidth; ++x) {
      var sx = Math.Min(Math.Min(x, width - 1), sourceWidth - 1);
      var sy = Math.Min(Math.Min(y, height - 1), sourceHeight - 1);
      result[y * codedWidth + x] = source[sy * sourceWidth + sx];
    }
    return result;
  }

  // ==============================================================================================
  // Plane and macroblock
  // ==============================================================================================

  private void _EncodePlane(Svq1BitWriter output, byte[] source, int width, int height, Svq1Decoder.Plane? reference) {
    var motion = new Svq1Decoder.MotionVector[width / 8 + 3];
    var plane = new Svq1Decoder.Plane(width, height);
    source.CopyTo(plane.Samples, 0);
    var prediction = new Svq1Decoder.Plane(width, height);

    for (var y = 0; y < height; y += 16) {
      for (var x = 0; x < width; x += 16) {
        if (reference == null) {
          this._ClearLevels();
          this._EncodeBlock(plane.Samples, y * width + x, null, 0, width, 5, 64, intra: true);
          this._Flush(output);
          continue;
        }

        // Intra.
        this._ClearLevels();
        this._levels[5].Write(3, 0);
        var intraScore = 3 * this._lambda + this._EncodeBlock(plane.Samples, y * width + x, null, 0, width, 5, 64, intra: true);
        var intraBits = this._Snapshot();

        // Inter, through the vector the search finds.
        var p0 = motion[0];
        var (p1, p2) = y == 0 ? (p0, p0) : (motion[x / 8 + 2], motion[x / 8 + 4]);
        var predictedX = Svq1Decoder._Median(p0.X, p1.X, p2.X);
        var predictedY = Svq1Decoder._Median(p0.Y, p1.Y, p2.Y);
        var vector = this._Search(plane, reference, prediction, x, y, predictedX, predictedY);

        this._ClearLevels();
        this._levels[5].Write(2, 1);
        _WriteMotion(this._levels[5], vector.X - predictedX);
        _WriteMotion(this._levels[5], vector.Y - predictedY);
        var interScore = this._lambda * this._levels[5].Count
          + this._EncodeBlock(plane.Samples, y * width + x, prediction.Samples, y * width + x, width, 5, 64, intra: false);
        var interBits = this._Snapshot();

        // Skip: the reference block as it stands.
        var skipScore = this._lambda;
        for (var r = 0; r < 16; ++r)
        for (var c = 0; c < 16; ++c) {
          var d = plane.Samples[(y + r) * width + x + c] - reference.Samples[(y + r) * width + x + c];
          skipScore += d * d;
        }

        if (skipScore <= interScore && skipScore <= intraScore) {
          output.Write(1, 1);
          motion[0] = motion[x / 8 + 2] = motion[x / 8 + 3] = default;
        } else if (interScore <= intraScore) {
          foreach (var bits in interBits)
            output.Append(bits);
          motion[0] = motion[x / 8 + 2] = motion[x / 8 + 3] = vector;
        } else {
          foreach (var bits in intraBits)
            output.Append(bits);
          motion[0] = motion[x / 8 + 2] = motion[x / 8 + 3] = default;
        }
      }

      motion[0] = default;
    }
  }

  private void _ClearLevels() {
    foreach (var level in this._levels)
      level.Truncate(0);
  }

  /// <summary>The level buffers in the order the decoder reads them, 5 down to 0.</summary>
  private List<Svq1BitWriter> _Snapshot() {
    var copy = new List<Svq1BitWriter>(6);
    for (var level = 5; level >= 0; --level) {
      var bits = new Svq1BitWriter();
      bits.Append(this._levels[level]);
      copy.Add(bits);
    }
    return copy;
  }

  private void _Flush(Svq1BitWriter output) {
    for (var level = 5; level >= 0; --level)
      output.Append(this._levels[level]);
  }

  private static void _WriteMotion(Svq1BitWriter output, int difference) {
    // The decoder wraps the sum into six signed bits, so a difference is taken modulo 64.
    if (difference > 32)
      difference -= 64;
    else if (difference < -32)
      difference += 64;

    var magnitude = Math.Abs(difference);
    var (code, length) = Svq1Tables.MotionCodes[magnitude];
    output.Write(length, (uint)code);
    if (magnitude != 0)
      output.Write(1, difference < 0 ? 1u : 0u);
  }

  /// <summary>
  /// A whole-sample search over ±8 then a half-sample one around the best, each candidate predicted
  /// exactly as the decoder predicts it, clamping included; leaves the winner's prediction in
  /// <paramref name="prediction"/>.
  /// </summary>
  private Svq1Decoder.MotionVector _Search(
    Svq1Decoder.Plane source, Svq1Decoder.Plane reference, Svq1Decoder.Plane prediction, int x, int y, int predictedX, int predictedY) {
    var best = new Svq1Decoder.MotionVector(0, 0);
    var bestCost = long.MaxValue;

    void Try(int mx, int my) {
      if (mx is < -32 or > 31 || my is < -32 or > 31)
        return;
      var cx = Math.Clamp(mx, -2 * x, 2 * (source.Width - x - 16));
      var cy = Math.Clamp(my, -2 * y, 2 * (source.Height - y - 16));
      long cost = this._lambda * (Math.Abs(mx - predictedX) + Math.Abs(my - predictedY));
      var s = reference.Samples;
      var w = reference.Width;
      var sx = x + (cx >> 1);
      var sy = y + (cy >> 1);
      var half = (cx & 1) | ((cy & 1) << 1);
      for (var r = 0; r < 16 && cost < bestCost; ++r)
      for (var c = 0; c < 16; ++c) {
        var at = (sy + r) * w + sx + c;
        int p = half switch {
          0 => s[at],
          1 => (s[at] + s[at + 1] + 1) >> 1,
          2 => (s[at] + s[at + w] + 1) >> 1,
          _ => (s[at] + s[at + 1] + s[at + w] + s[at + w + 1] + 2) >> 2,
        };
        var d = source.Samples[(y + r) * source.Width + x + c] - p;
        cost += d * d;
      }
      if (cost < bestCost) {
        bestCost = cost;
        best = new(mx, my);
      }
    }

    for (var dy = -_SEARCH; dy <= _SEARCH; ++dy)
    for (var dx = -_SEARCH; dx <= _SEARCH; ++dx)
      Try(dx * 2, dy * 2);

    var whole = best;
    for (var dy = -1; dy <= 1; ++dy)
    for (var dx = -1; dx <= 1; ++dx)
      if (dx != 0 || dy != 0)
        Try(whole.X + dx, whole.Y + dy);

    var bx = Math.Clamp(best.X, -2 * x, 2 * (source.Width - x - 16));
    var by = Math.Clamp(best.Y, -2 * y, 2 * (source.Height - y - 16));
    Svq1Decoder.PutPixels(reference, x + (bx >> 1), y + (by >> 1), (bx & 1) | ((by & 1) << 1), prediction, x, y, 16);
    return best;
  }

  // ==============================================================================================
  // FFmpeg's encode_block
  // ==============================================================================================

  private int _EncodeBlock(byte[] source, int at, byte[]? reference, int referenceAt, int stride, int level, int threshold, bool intra) {
    var w = 2 << ((level + 2) >> 1);
    var h = 2 << ((level + 1) >> 1);
    var size = w * h;
    var block = this._encoded[level];
    Span<int> blockSum = stackalloc int[7];
    Span<int> bestVector = stackalloc int[6];

    var codebook = level < 4 ? (intra ? Svq1Tables.IntraCodebooks[level] : Svq1Tables.InterCodebooks[level]) : null;
    var codebookSum = level < 4 ? (intra ? _IntraSums[level] : _InterSums[level]) : null;
    var meanCodes = intra ? Svq1Tables.IntraMeanCodes : Svq1Tables.InterMeanCodes;
    var meanOffset = intra ? 0 : 256;
    var multistage = intra ? Svq1Tables.IntraMultistageCodes[level] : Svq1Tables.InterMultistageCodes[level];

    var bestScore = 0;
    for (var y = 0; y < h; ++y)
    for (var x = 0; x < w; ++x) {
      var v = source[at + x + y * stride] - (intra ? 0 : reference![referenceAt + x + y * stride]);
      block[0][x + w * y] = (short)v;
      bestScore += v * v;
      blockSum[0] += v;
    }

    var bestCount = 0;
    bestScore -= (int)((uint)blockSum[0] * (uint)blockSum[0] >> (level + 3));
    var bestMean = (blockSum[0] + (size >> 1)) >> (level + 3);

    if (level < 4) {
      for (var count = 1; count < 7; ++count) {
        var bestVectorScore = int.MaxValue;
        var bestVectorSum = -999;
        var bestVectorMean = -999;
        var stage = count - 1;

        for (var i = 0; i < 16; ++i) {
          int sum = codebookSum![stage * 16 + i];
          var vector = (stage * 16 + i) * size;
          var sqr = 0;
          for (var j = 0; j < size; ++j) {
            var d = codebook![vector + j] - block[stage][j];
            sqr += d * d;
          }
          var diff = blockSum[stage] - sum;
          var score = (int)(sqr - ((long)diff * diff >> (level + 3)));
          if (score < bestVectorScore) {
            var mean = (diff + (size >> 1)) >> (level + 3);
            mean = Math.Clamp(mean, intra ? 0 : -256, 255);
            bestVectorScore = score;
            bestVector[stage] = i;
            bestVectorSum = sum;
            bestVectorMean = mean;
          }
        }

        var chosen = (stage * 16 + bestVector[stage]) * size;
        for (var j = 0; j < size; ++j)
          block[stage + 1][j] = (short)(block[stage][j] - codebook![chosen + j]);
        blockSum[stage + 1] = blockSum[stage] - bestVectorSum;
        bestVectorScore += this._lambda * (1 + 4 * count + multistage[1 + count].Length + meanCodes[bestVectorMean + meanOffset].Length);

        if (bestVectorScore < bestScore) {
          bestScore = bestVectorScore;
          bestCount = count;
          bestMean = bestVectorMean;
        }
      }
    }

    // A mean of ±128 is avoided: one decoder family swaps the two.
    if (bestMean == -128)
      bestMean = -127;
    else if (bestMean == 128)
      bestMean = 127;
    bestMean = Math.Clamp(bestMean, intra ? 0 : -256, 255);

    var split = false;
    if (bestScore > threshold && level > 0) {
      Span<int> backup = stackalloc int[6];
      for (var i = level - 1; i >= 0; --i)
        backup[i] = this._levels[i].Count;

      var offset = (level & 1) != 0 ? stride * h / 2 : w / 2;
      var score = this._EncodeBlock(source, at, reference, referenceAt, stride, level - 1, threshold >> 1, intra);
      score += this._EncodeBlock(source, at + offset, reference, referenceAt + offset, stride, level - 1, threshold >> 1, intra);
      score += this._lambda;

      if (score < bestScore) {
        bestScore = score;
        split = true;
      } else {
        for (var i = level - 1; i >= 0; --i)
          this._levels[i].Truncate(backup[i]);
      }
    }

    if (level > 0)
      this._levels[level].Write(1, split ? 1u : 0u);

    if (!split) {
      var (stageCode, stageLength) = multistage[1 + bestCount];
      this._levels[level].Write(stageLength, (uint)stageCode);
      var (meanCode, meanLength) = meanCodes[bestMean + meanOffset];
      this._levels[level].Write(meanLength, (uint)meanCode);
      for (var i = 0; i < bestCount; ++i)
        this._levels[level].Write(4, (uint)bestVector[i]);
    }

    return bestScore;
  }
}
