using System;
using System.IO;

namespace FileFormat.GraphSaurusInterlaced;

/// <summary>Assembles a Graph Saurus interlaced picture, which is rows of nibbles and nothing else.</summary>
public static class GraphSaurusInterlacedWriter {

  public static byte[] ToBytes(GraphSaurusInterlacedFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.PixelData ?? new byte[GraphSaurusInterlacedFile.FileSize];
  }

  public static void ToFile(GraphSaurusInterlacedFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
