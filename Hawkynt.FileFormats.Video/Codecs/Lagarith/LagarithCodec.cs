using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Codecs.Lagarith;

/// <summary>The frame types a Lagarith packet opens with, FFmpeg's <c>LagarithFrameType</c>.</summary>
internal enum LagarithFrameType : byte {
  Raw = 1,
  UnalignedRgb24 = 2,
  ArithmeticYuy2 = 3,
  ArithmeticRgb24 = 4,
  SolidGray = 5,
  SolidColor = 6,
  OldArithmeticRgb = 7,
  ArithmeticRgba = 8,
  SolidRgba = 9,
  ArithmeticYv12 = 10,
  ReducedResolution = 11,
}

/// <summary>How a plane's samples are predicted from the ones before them.</summary>
internal enum LagarithPrediction {
  /// <summary>RGB planes: median prediction, the second row's first sample from above.</summary>
  Rgb,
  /// <summary>YV12 planes: as RGB but the second row's first sample from the median with the row above.</summary>
  Yv12,
  /// <summary>YUY2 luminance, with its own first two rows.</summary>
  Yuy2Luma,
  /// <summary>YUY2 chrominance.</summary>
  Yuy2Chroma,
}

/// <summary>
/// Lagarith's planes: their four codings and three predictors, both ways.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/lagarith.c</c> (LGPL-2.1-or-later): <c>lag_decode_arith_plane</c>,
/// <c>lag_decode_line</c>, <c>lag_decode_zero_run_line</c>, <c>lag_pred_line</c>,
/// <c>lag_pred_line_yuy2</c> and <c>add_lag_median_prediction</c>. The encoding direction is the
/// predictors run forwards, which this package adds.
/// </remarks>
internal sealed class LagarithPlaneCodec {

  private const int _MAX_OVERREAD = 4;

  // FFmpeg keeps the zeros still owed by a run across planes, in the decoder's context.
  private int _zerosRemaining;

  internal static byte Median(int a, int b, int c) {
    if (a > b) {
      if (c > b)
        b = c > a ? a : c;
    } else if (b > c)
      b = c > a ? c : a;
    return (byte)b;
  }

  private static byte _ZeroRun(byte index) {
    var x = (sbyte)index;
    return (byte)((x * 2) ^ (x >> 7));
  }

  /// <summary>Decodes one plane, rows in coded order, into <paramref name="plane"/>.</summary>
  internal void Decode(byte[] data, int start, int end, byte[] plane, int width, int height, LagarithPrediction prediction) {
    if (end - start < 2)
      throw new InvalidDataException("A Lagarith plane is shorter than its two-byte header.");

    int escape = data[start];
    if (escape < 4) {
      var offset = 1;
      if (end - start < 5)
        throw new InvalidDataException("A Lagarith plane is shorter than its five-byte header.");
      if (escape != 0 && BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(start + 1)) < (uint)(width * height))
        offset += 4;

      var probabilities = new uint[258];
      var reader = new LagarithBitReader(data.AsSpan(start + offset, end - start - offset));
      var scale = LagarithEntropy.ReadProbabilities(ref reader, probabilities);
      var coded = start + offset + (reader.BitPosition + 7) / 8;
      var decoder = new LagarithRangeDecoder(data, coded, end, probabilities, scale);

      var zeros = 0;
      var trigger = escape == 0 ? -1 : escape;
      for (var row = 0; row < height; ++row) {
        if (decoder.Overread > _MAX_OVERREAD)
          throw new InvalidDataException("A Lagarith plane's coded data ran out before the plane did.");
        this._DecodeLine(decoder, plane.AsSpan(row * width, width), trigger, ref zeros);
      }
    } else if (escape < 8) {
      escape -= 4;
      var at = start + 1;
      if (escape > 0) {
        for (var row = 0; row < height; ++row)
          at += this._DecodeZeroRunLine(plane.AsSpan(row * width, width), data, at, end, width, escape);
      } else {
        if (end - at < width * height)
          throw new InvalidDataException("A Lagarith plane stored uncompressed is shorter than the plane.");
        data.AsSpan(at, width * height).CopyTo(plane);
      }
    } else if (escape == 0xFF) {
      plane.AsSpan(0, width * height).Fill(data[start + 1]);
      return;
    } else
      throw new InvalidDataException($"A Lagarith plane states the escape code {escape:X2}, which is not one.");

    Predict(plane, width, height, prediction);
  }

  private void _DecodeLine(LagarithRangeDecoder decoder, Span<byte> line, int trigger, ref int zeros) {
    var i = 0;
    while (true) {
      if (this._zerosRemaining != 0) {
        var count = Math.Min(this._zerosRemaining, line.Length - i);
        line.Slice(i, count).Clear();
        i += count;
        this._zerosRemaining -= count;
      }

      var restarted = false;
      while (i < line.Length) {
        line[i] = decoder.Read();
        zeros = line[i] != 0 ? 0 : zeros + 1;
        ++i;
        if (zeros == trigger) {
          var index = decoder.Read();
          zeros = 0;
          this._zerosRemaining = _ZeroRun(index);
          restarted = true;
          break;
        }
      }

      if (!restarted)
        return;
    }
  }

  /// <summary>FFmpeg's <c>lag_decode_zero_run_line</c>, which leaves the last two samples of a line zero.</summary>
  private int _DecodeZeroRunLine(Span<byte> line, byte[] data, int source, int end, int width, int escape) {
    var mask1 = escape < 2 ? 0xFF : 0;
    var mask2 = escape < 3 ? 0xFF : 0;
    var finish = width - 2;
    var origin = source;
    var at = 0;
    var i = 0;
    line.Clear();

    while (true) {
      if (this._zerosRemaining != 0) {
        var count = Math.Min(this._zerosRemaining, width - i);
        if (finish - at < count)
          throw new InvalidDataException("A Lagarith zero-run line owes more zeros than it has room for.");
        this._zerosRemaining -= count;
        at += count;
      }

      var restarted = false;
      while (at < finish) {
        i = 0;
        var zeroRun = false;
        while (!zeroRun && at + i < finish) {
          ++i;
          if (i + 2 >= end - source)
            throw new InvalidDataException("A Lagarith zero-run line runs past its plane.");
          zeroRun = (data[source + i] | (data[source + i + 1] & mask1) | (data[source + i + 2] & mask2)) == 0;
        }

        if (zeroRun) {
          i += escape;
          if (i > finish - at || i >= end - source)
            throw new InvalidDataException("A Lagarith zero-run line runs past its plane.");
          data.AsSpan(source, i).CopyTo(line[at..]);
          at += i;
          this._zerosRemaining = _ZeroRun(data[source + i]);
          source += i + 1;
          restarted = true;
          break;
        }

        data.AsSpan(source, i).CopyTo(line[at..]);
        source += i;
        at += i;
      }

      if (!restarted)
        return source - origin;
    }
  }

  // ============================================================================================
  // Prediction
  // ============================================================================================

  /// <summary>Turns residues into samples, in place.</summary>
  internal static void Predict(byte[] plane, int width, int height, LagarithPrediction prediction) {
    for (var row = 0; row < height; ++row) {
      var line = row * width;
      if (prediction is LagarithPrediction.Yuy2Luma or LagarithPrediction.Yuy2Chroma) {
        _PredictYuy2(plane, line, width, row, prediction == LagarithPrediction.Yuy2Luma);
        continue;
      }

      if (row == 0) {
        var sum = 0;
        for (var i = 0; i < width; ++i)
          plane[line + i] = (byte)(sum += plane[line + i]);
        continue;
      }

      int left = plane[line - 1];
      int topLeft = row == 1
        ? (prediction == LagarithPrediction.Yv12 ? plane[line - width] : left)
        : plane[line - width - 1];
      for (var i = 0; i < width; ++i) {
        int top = plane[line - width + i];
        left = (byte)(Median(left, top, left + top - topLeft) + plane[line + i]);
        topLeft = top;
        plane[line + i] = (byte)left;
      }
    }
  }

  private static void _PredictYuy2(byte[] plane, int line, int width, int row, bool luma) {
    if (row == 0) {
      var first = plane[line];
      if (luma)
        plane[line] = 0;
      var sum = 0;
      for (var i = 0; i < width; ++i)
        plane[line + i] = (byte)(sum += plane[line + i]);
      if (luma)
        plane[line] = first;
      return;
    }

    if (row == 1) {
      var head = luma ? 4 : 2;
      int left = plane[line - 1];
      int topLeft = plane[line - width + head - 1];
      var i = 0;
      for (; i < head && i < width; ++i) {
        left += plane[line + i];
        plane[line + i] = (byte)left;
      }
      for (; i < width; ++i) {
        int top = plane[line - width + i];
        left = Median(left & 0xFF, top, (left + top - topLeft) & 0xFF) + plane[line + i];
        topLeft = top;
        plane[line + i] = (byte)left;
      }
      return;
    }

    int l = plane[line - 1];
    int tl = plane[line - width - 1];
    for (var i = 0; i < width; ++i) {
      int top = plane[line - width + i];
      l = (byte)(Median(l, top, (l + top - tl) & 0xFF) + plane[line + i]);
      tl = top;
      plane[line + i] = (byte)l;
    }
  }

  /// <summary>The residues the decoder's prediction turns back into <paramref name="plane"/>.</summary>
  internal static byte[] Residues(ReadOnlySpan<byte> plane, int width, int height, LagarithPrediction prediction) {
    var residues = new byte[width * height];
    for (var row = 0; row < height; ++row) {
      var line = row * width;
      var yuy2 = prediction is LagarithPrediction.Yuy2Luma or LagarithPrediction.Yuy2Chroma;
      if (row == 0) {
        for (var i = 0; i < width; ++i)
          residues[line + i] = (byte)(plane[line + i] - (i == 0 ? 0 : plane[line + i - 1]));
        if (yuy2 && prediction == LagarithPrediction.Yuy2Luma && width > 1)
          residues[line + 1] = plane[line + 1];
        continue;
      }

      if (yuy2 && row == 1) {
        var head = prediction == LagarithPrediction.Yuy2Luma ? 4 : 2;
        int previous = plane[line - 1];
        int topLeft = plane[line - width + head - 1];
        var i = 0;
        for (; i < head && i < width; ++i) {
          residues[line + i] = (byte)(plane[line + i] - previous);
          previous = plane[line + i];
        }
        for (; i < width; ++i) {
          int top = plane[line - width + i];
          residues[line + i] = (byte)(plane[line + i] - Median(previous, top, (previous + top - topLeft) & 0xFF));
          topLeft = top;
          previous = plane[line + i];
        }
        continue;
      }

      int left = plane[line - 1];
      int corner = yuy2 || row > 1
        ? plane[line - width - 1]
        : prediction == LagarithPrediction.Yv12 ? plane[line - width] : left;
      for (var i = 0; i < width; ++i) {
        int top = plane[line - width + i];
        var predicted = yuy2 ? Median(left, top, (left + top - corner) & 0xFF) : Median(left, top, left + top - corner);
        residues[line + i] = (byte)(plane[line + i] - predicted);
        corner = top;
        left = plane[line + i];
      }
    }
    return residues;
  }

  /// <summary>
  /// Codes one plane: as a solid value where it is one, otherwise range-coded without zero runs, or
  /// its residues stored where coding them would not make them smaller.
  /// </summary>
  internal static byte[] Encode(ReadOnlySpan<byte> plane, int width, int height, LagarithPrediction prediction) {
    var solid = true;
    for (var i = 1; i < plane.Length && solid; ++i)
      solid = plane[i] == plane[0];
    if (solid)
      return [0xFF, plane[0]];

    var residues = Residues(plane, width, height, prediction);
    Span<long> counts = stackalloc long[256];
    foreach (var residue in residues)
      ++counts[residue];

    var writer = new LagarithBitWriter();
    var probabilities = new uint[258];
    var scale = LagarithEntropy.WriteProbabilities(writer, counts, probabilities);
    var header = writer.ToArray();

    var encoder = new LagarithRangeEncoder(probabilities, scale);
    foreach (var residue in residues)
      encoder.Write(residue);
    var coded = encoder.Finish();

    // A plane the coder cannot shrink — noise — is stored as its residues instead, escape code 4.
    if (1 + header.Length + coded.Length > 1 + residues.Length) {
      var stored = new byte[1 + residues.Length];
      stored[0] = 4;
      residues.CopyTo(stored, 1);
      return stored;
    }

    var result = new byte[1 + header.Length + coded.Length];
    result[0] = 0;
    header.CopyTo(result, 1);
    coded.CopyTo(result, 1 + header.Length);
    return result;
  }
}
