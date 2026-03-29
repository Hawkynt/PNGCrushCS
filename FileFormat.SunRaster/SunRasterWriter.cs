using System;
using System.IO;

namespace FileFormat.SunRaster;

/// <summary>Assembles Sun Raster file bytes from pixel data.</summary>
public static class SunRasterWriter {

  public static byte[] ToBytes(SunRasterFile file) => Assemble(
    file.PixelData,
    file.Width,
    file.Height,
    file.Depth,
    file.Compression,
    file.Palette,
    file.PaletteColorCount
  );

  internal static byte[] Assemble(
    byte[] pixelData,
    int width,
    int height,
    int depth,
    SunRasterCompression compression,
    byte[]? palette = null,
    int paletteColorCount = 0
  ) {
    using var ms = new MemoryStream();

    // Build colormap bytes
    var hasPalette = palette != null && paletteColorCount > 0;
    var mapLength = hasPalette ? paletteColorCount * 3 : 0;
    var mapType = hasPalette ? 1 : 0;

    // Prepare pixel data
    byte[] outputPixelData;
    if (compression == SunRasterCompression.Rle)
      outputPixelData = SunRasterRleCompressor.Compress(pixelData);
    else
      outputPixelData = pixelData;

    // Write header
    var header = new SunRasterHeader(
      Magic: SunRasterHeader.MagicValue,
      Width: width,
      Height: height,
      Depth: depth,
      Length: outputPixelData.Length,
      Type: (int)compression,
      MapType: mapType,
      MapLength: mapLength
    );

    var headerBytes = new byte[SunRasterHeader.StructSize];
    header.WriteTo(headerBytes);
    ms.Write(headerBytes);

    // Write colormap (RGB planes: R[n], G[n], B[n])
    if (hasPalette) {
      for (var i = 0; i < paletteColorCount; ++i)
        ms.WriteByte(palette![i * 3]); // R plane

      for (var i = 0; i < paletteColorCount; ++i)
        ms.WriteByte(palette![i * 3 + 1]); // G plane

      for (var i = 0; i < paletteColorCount; ++i)
        ms.WriteByte(palette![i * 3 + 2]); // B plane
    }

    // Write pixel data
    ms.Write(outputPixelData);

    return ms.ToArray();
  }
}
