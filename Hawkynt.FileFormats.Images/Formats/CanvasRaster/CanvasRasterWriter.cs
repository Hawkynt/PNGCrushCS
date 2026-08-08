namespace FileFormat.CanvasRaster;

/// <summary>Assembles a Canvas raster picture from a <see cref="CanvasRasterFile"/>.</summary>
public static class CanvasRasterWriter {

  /// <summary>Writes the file, which is already whole because the palettes are addressed absolutely.</summary>
  public static byte[] ToBytes(CanvasRasterFile file) => (byte[])(file.Data ?? []).Clone();
}
