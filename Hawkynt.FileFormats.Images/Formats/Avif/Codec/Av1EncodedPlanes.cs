using System;

namespace FileFormat.Avif.Codec;

/// <summary>
/// The source picture as the encoder sees it: one plane per colour component, padded out to the
/// mode-info grid by edge replication. AV1 codes whole 4x4 blocks, so an image whose dimensions are
/// not a multiple of eight has samples the encoder must invent; replicating the edge keeps the
/// invented residual small and never affects the visible picture.
/// </summary>
internal sealed class Av1EncodedPlanes {

  public Av1EncodedPlanes(int planeCount, int width, int height, int miCols, int miRows, int subX, int subY) {
    this.Planes = new short[planeCount][];
    this.Strides = new int[planeCount];
    this.Widths = new int[planeCount];
    this.Heights = new int[planeCount];

    for (var plane = 0; plane < planeCount; ++plane) {
      var sx = plane > 0 ? subX : 0;
      var sy = plane > 0 ? subY : 0;
      this.Widths[plane] = (miCols * 4) >> sx;
      this.Heights[plane] = (miRows * 4) >> sy;
      this.Strides[plane] = this.Widths[plane];
      this.Planes[plane] = new short[this.Strides[plane] * this.Heights[plane]];
    }

    this.Width = width;
    this.Height = height;
  }

  public short[][] Planes { get; }
  public int[] Strides { get; }
  public int[] Widths { get; }
  public int[] Heights { get; }
  public int Width { get; }
  public int Height { get; }

  /// <summary>Fills a plane from a source raster, replicating the last row and column outwards.</summary>
  public void FillPlane(int plane, ReadOnlySpan<byte> source, int sourceStride, int sourceOffset, int sampleStride) {
    var target = this.Planes[plane];
    var stride = this.Strides[plane];
    var width = this.Widths[plane];
    var height = this.Heights[plane];

    for (var y = 0; y < height; ++y) {
      var sourceY = Math.Min(y, this.Height - 1);
      for (var x = 0; x < width; ++x) {
        var sourceX = Math.Min(x, this.Width - 1);
        target[y * stride + x] = source[sourceY * sourceStride + sourceX * sampleStride + sourceOffset];
      }
    }
  }
}
