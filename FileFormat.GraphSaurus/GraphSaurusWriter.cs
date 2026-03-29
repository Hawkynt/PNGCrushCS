using System;

namespace FileFormat.GraphSaurus;

/// <summary>Assembles Graph Saurus file bytes from a <see cref="GraphSaurusFile"/>.</summary>
public static class GraphSaurusWriter {

  public static byte[] ToBytes(GraphSaurusFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[GraphSaurusFile.ExpectedFileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, GraphSaurusFile.ExpectedFileSize)).CopyTo(result);
    return result;
  }
}
