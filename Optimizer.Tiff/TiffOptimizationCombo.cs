using FileFormat.Tiff;

namespace Optimizer.Tiff;

public readonly record struct TiffOptimizationCombo(
  TiffCompression Compression,
  TiffPredictor Predictor,
  TiffColorMode ColorMode,
  int StripRowCount,
  int TileWidth = 0,
  int TileHeight = 0
) {
  public bool IsTiled => this.TileWidth > 0 && this.TileHeight > 0;
}
