using System;

namespace TiffOptimizer;

public readonly record struct TiffOptimizationResult(
  TiffCompression Compression,
  TiffPredictor Predictor,
  TiffColorMode ColorMode,
  int StripRowCount,
  long CompressedSize,
  TimeSpan ProcessingTime,
  byte[] FileContents,
  int TileWidth = 0,
  int TileHeight = 0
) {
  public bool IsTiled => this.TileWidth > 0 && this.TileHeight > 0;

  public override string ToString() {
    return $"Size: {this.CompressedSize} bytes, " +
           $"Compression: {this.Compression}, " +
           $"Predictor: {this.Predictor}, " +
           $"Color: {this.ColorMode}, " +
           (this.IsTiled ? $"Tile: {this.TileWidth}x{this.TileHeight}, " : $"StripRows: {this.StripRowCount}, ") +
           $"Time: {this.ProcessingTime.TotalMilliseconds:F0}ms";
  }
}
