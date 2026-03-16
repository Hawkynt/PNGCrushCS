namespace Optimizer.Image;

public sealed partial class ImageOptimizer {
  private readonly record struct ImageStats(int UniqueColors, bool HasAlpha);
}
