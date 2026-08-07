using System;
using FileFormat.Core;

namespace FileFormat.GraphSaurus7;

/// <summary>Assembles a Graph Saurus Screen 7 picture from a <see cref="GraphSaurus7File"/>.</summary>
/// <remarks>
/// The picture only. Its palette belongs to a companion <c>.PL7</c> that is a separate file, so a
/// caller wanting the colours kept has to write <see cref="GraphSaurus7File.Palette"/> beside this —
/// which is the same bargain every Graph Saurus mode but Screen 8 makes.
/// </remarks>
public static class GraphSaurus7Writer {

  public static byte[] ToBytes(GraphSaurus7File file) {
    var pixels = file.PixelData ?? [];
    var result = new byte[GraphSaurus7File.MinimumFileSize];

    MsxGraphics.WriteBsaveHeader(result, GraphSaurus7File.BitmapSize - 1);
    pixels
      .AsSpan(0, Math.Min(pixels.Length, GraphSaurus7File.BitmapSize))
      .CopyTo(result.AsSpan(GraphSaurus7File.BitmapOffset));

    return result;
  }
}
