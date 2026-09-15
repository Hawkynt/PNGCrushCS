using System;

namespace FileFormat.Codecs.CineForm;

/// <summary>Quantises and entropy-codes one CineForm highpass subband.</summary>
internal static class CineFormEntropyEncoder {
  private static readonly int[] _ZeroRuns = [320, 180, 100, 60, 32, 20, 12, 1];
  private static readonly byte[] _MagnitudeForCompanded = _BuildMagnitudeLookup();

  internal readonly record struct EncodedBand(byte[] Data, int Quantization);

  internal static EncodedBand Encode(ReadOnlySpan<int> coefficients, int width, int height) {
    if (width <= 0 || height <= 0 || coefficients.Length < width * height)
      throw new ArgumentException("A CineForm highpass band must contain width*height coefficients.");

    var maximum = 0;
    for (var i = 0; i < width * height; ++i) {
      var magnitude = Math.Abs(coefficients[i]);
      if (magnitude > maximum)
        maximum = magnitude;
    }

    // Annex F's largest companded codebook magnitude is 1023 at symbol 255. Choose the smallest
    // divisor that makes the complete band representable rather than clipping a coefficient.
    var quantization = Math.Max(1, (maximum + 1022) / 1023);
    if (quantization > ushort.MaxValue)
      throw new NotSupportedException($"A CineForm highpass band needs quantisation {quantization}, which does not fit the sixteen-bit tag.");

    var paddedWidth = (width + 7) & ~7;
    var writer = new CineFormBitWriter();
    var zeroCount = 0;

    for (var y = 0; y < height; ++y) {
      var row = y * width;
      for (var x = 0; x < paddedWidth; ++x) {
        var coefficient = x < width ? coefficients[row + x] : 0;
        var magnitude = coefficient < 0 ? -coefficient : coefficient;
        var scaled = magnitude == 0 ? 0 : Math.Min(1023, (magnitude + (quantization >> 1)) / quantization);
        var symbol = _MagnitudeForCompanded[scaled];

        if (symbol == 0) {
          ++zeroCount;
          continue;
        }

        _WriteZeroRun(writer, ref zeroCount);
        if (!CineFormCodebook.TryGetCodeword(1, symbol, out var code, out var length))
          throw new InvalidOperationException($"CineForm Annex C has no codeword for magnitude {symbol}.");

        writer.WriteBits(code, length);
        writer.WriteBits(coefficient < 0 ? 1u : 0u, 1);
      }
    }

    _WriteZeroRun(writer, ref zeroCount);
    if (!CineFormCodebook.TryGetCodeword(0, CineFormCodebook.BandEndMarkerValue, out var endCode, out var endLength))
      throw new InvalidOperationException("CineForm Annex C's band-end marker is missing from its own codebook.");

    writer.WriteBits(endCode, endLength);
    return new(writer.ToSegmentAlignedArray(), quantization);
  }

  private static void _WriteZeroRun(CineFormBitWriter writer, ref int count) {
    while (count > 0) {
      foreach (var run in _ZeroRuns) {
        if (run > count)
          continue;

        if (!CineFormCodebook.TryGetCodeword(run, 0, out var code, out var length))
          throw new InvalidOperationException($"CineForm Annex C has no codeword for a zero run of {run}.");

        writer.WriteBits(code, length);
        count -= run;
        break;
      }
    }
  }

  private static byte[] _BuildMagnitudeLookup() {
    var result = new byte[1024];
    var symbol = 0;

    for (var target = 0; target < result.Length; ++target) {
      while (symbol < 255) {
        var here = CineFormWavelet.CompandedMagnitude(symbol);
        var next = CineFormWavelet.CompandedMagnitude(symbol + 1);
        if (target - here <= next - target)
          break;
        ++symbol;
      }

      result[target] = (byte)symbol;
    }

    return result;
  }
}
