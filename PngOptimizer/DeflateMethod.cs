namespace PngOptimizer;

/// <summary>Deflate compression methods/levels</summary>
public enum DeflateMethod {
  Fastest = 0,
  Fast = 1,
  Default = 2,
  Maximum = 3,
  Ultra = 4, // DP optimal parsing with 2-pass refinement
  Hyper = 5 // Full Zopfli-class with iterative refinement and block splitting
}
