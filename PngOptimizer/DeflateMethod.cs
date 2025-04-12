namespace PngOptimizer;

/// <summary>Deflate compression methods/levels</summary>
public enum DeflateMethod {
  Fastest = 0,
  Fast = 1,
  Default = 2,
  Maximum = 3,
  Ultra = 4  // Custom ultra compression with brute force approach
}
