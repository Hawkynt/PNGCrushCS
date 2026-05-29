namespace Optimizer.Gif;

public enum PaletteReorderStrategy {
  Original = 0,
  FrequencySorted = 1,
  LuminanceSorted = 2,
  SpatialLocality = 3,
  LzwRunAware = 4,
  HilbertCurve = 5,
  CompressionOptimized = 6
}
