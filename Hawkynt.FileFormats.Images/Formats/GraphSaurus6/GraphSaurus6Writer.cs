using System;
using FileFormat.Core;

namespace FileFormat.GraphSaurus6;

/// <summary>Assembles a Graph Saurus Screen 6 picture from a <see cref="GraphSaurus6File"/>.</summary>
public static class GraphSaurus6Writer {

  public static byte[] ToBytes(GraphSaurus6File file) {
    var pixels = file.PixelData ?? [];
    var size = GraphSaurus6File.BitmapOffset + file.StoredHeight * GraphSaurus6File.BytesPerRow;
    var result = new byte[size];

    MsxGraphics.WriteBsaveHeader(result, size - GraphSaurus6File.BitmapOffset - 1);
    pixels
      .AsSpan(0, Math.Min(pixels.Length, size - GraphSaurus6File.BitmapOffset))
      .CopyTo(result.AsSpan(GraphSaurus6File.BitmapOffset));

    return result;
  }
}
