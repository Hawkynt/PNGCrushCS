using System;

namespace FileFormat.GraphicsMaster;

/// <summary>Assembles Graphics Master file bytes from a <see cref="GraphicsMasterFile"/>.</summary>
public static class GraphicsMasterWriter {

  public static byte[] ToBytes(GraphicsMasterFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[GraphicsMasterFile.ExpectedFileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, GraphicsMasterFile.ExpectedFileSize)).CopyTo(result);
    return result;
  }
}
