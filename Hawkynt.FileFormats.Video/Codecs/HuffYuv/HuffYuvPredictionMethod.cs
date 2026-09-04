namespace FileFormat.Codecs;

/// <summary>How a HuffYUV encoder predicts each sample from the ones it has already coded.</summary>
/// <remarks>
/// The three the format names, with the numbers the stream description carries them as. Left is the
/// cheapest and the reference encoder's default; median usually codes a natural picture smallest.
/// Median is not available for the packed colour layout, whose reference encoder refuses it too.
/// </remarks>
public enum HuffYuvPredictionMethod {

  /// <summary>The same component of the pixel to the left.</summary>
  Left = 0,

  /// <summary>Left plus above minus above-left — a plane through the three known corners.</summary>
  Gradient = 1,

  /// <summary>The median of left, above, and left plus above minus above-left.</summary>
  Median = 2,
}
