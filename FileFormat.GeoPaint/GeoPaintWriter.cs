using System;

namespace FileFormat.GeoPaint;

/// <summary>Assembles GEOS GeoPaint file bytes from pixel data using per-scanline RLE compression.</summary>
public static class GeoPaintWriter {

  public static byte[] ToBytes(GeoPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int height)
    => GeoPaintRleCompressor.Compress(pixelData, height);
}
