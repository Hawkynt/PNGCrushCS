using System;

namespace FileFormat.Wsq;

/// <summary>Scalar quantization for WSQ subbands.</summary>
internal static class WsqQuantizer {

  /// <summary>Per-subband quantization parameters.</summary>
  public readonly record struct QuantParams(double BinWidth, double ZeroBinCenter);

  /// <summary>Quantizes wavelet coefficients using per-subband bin widths.</summary>
  public static int[] Quantize(double[] coeffs, int width, int height, QuantParams[] subbandParams) {
    var subbands = WsqWavelet.ComputeSubbandLayout(width, height);
    var indices = new int[coeffs.Length];

    for (var sb = 0; sb < subbands.Length; ++sb) {
      var info = subbands[sb];
      var param = subbandParams[sb];
      if (param.BinWidth <= 0)
        continue;

      for (var y = 0; y < info.Height; ++y)
      for (var x = 0; x < info.Width; ++x) {
        var offset = (info.Y + y) * width + (info.X + x);
        var val = coeffs[offset];
        var absVal = Math.Abs(val);
        if (absVal <= param.ZeroBinCenter)
          indices[offset] = 0;
        else
          indices[offset] = (int)(Math.Sign(val) * Math.Floor(absVal / param.BinWidth + 0.5));
      }
    }

    return indices;
  }

  /// <summary>Dequantizes indices back to wavelet coefficients.</summary>
  public static double[] Dequantize(int[] indices, int width, int height, QuantParams[] subbandParams) {
    var subbands = WsqWavelet.ComputeSubbandLayout(width, height);
    var coeffs = new double[indices.Length];

    for (var sb = 0; sb < subbands.Length; ++sb) {
      var info = subbands[sb];
      var param = subbandParams[sb];
      if (param.BinWidth <= 0)
        continue;

      for (var y = 0; y < info.Height; ++y)
      for (var x = 0; x < info.Width; ++x) {
        var offset = (info.Y + y) * width + (info.X + x);
        var idx = indices[offset];
        if (idx == 0)
          coeffs[offset] = 0.0;
        else
          coeffs[offset] = Math.Sign(idx) * (Math.Abs(idx) * param.BinWidth + param.ZeroBinCenter);
      }
    }

    return coeffs;
  }

  /// <summary>Computes quantization parameters for each subband based on quality ratio.</summary>
  public static QuantParams[] ComputeParams(double[] coeffs, int width, int height, double quality) {
    var subbands = WsqWavelet.ComputeSubbandLayout(width, height);
    var result = new QuantParams[subbands.Length];

    // Quality maps inversely to bin width: higher quality = narrower bins
    var scaleFactor = Math.Clamp(1.0 - quality, 0.01, 1.0) * 20.0;

    for (var sb = 0; sb < subbands.Length; ++sb) {
      var info = subbands[sb];
      if (info.Width == 0 || info.Height == 0) {
        result[sb] = new(1.0, 0.5);
        continue;
      }

      // Compute variance of subband
      var sum = 0.0;
      var sumSq = 0.0;
      var count = info.Width * info.Height;
      for (var y = 0; y < info.Height; ++y)
      for (var x = 0; x < info.Width; ++x) {
        var val = coeffs[(info.Y + y) * width + (info.X + x)];
        sum += val;
        sumSq += val * val;
      }

      var mean = sum / count;
      var variance = sumSq / count - mean * mean;
      var stddev = Math.Sqrt(Math.Max(variance, 0.0));

      // Bin width proportional to standard deviation and scale factor
      // LL subband (0) gets the finest quantization
      var binWidth = sb == 0
        ? Math.Max(stddev * scaleFactor * 0.1, 0.5)
        : Math.Max(stddev * scaleFactor * 0.5, 0.5);

      var zeroBinCenter = binWidth * 0.44;
      result[sb] = new(binWidth, zeroBinCenter);
    }

    return result;
  }
}
