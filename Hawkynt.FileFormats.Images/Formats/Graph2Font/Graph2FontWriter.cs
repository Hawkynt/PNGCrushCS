using System;
using System.IO;

namespace FileFormat.Graph2Font;

/// <summary>Writes a Graph2Font project back out.</summary>
/// <remarks>
/// Uncompressed, which is the form the editor itself reads and the form the reader unpacks a
/// compressed project into. Compressing it would be a saving of disk and a loss of the one property
/// that matters here: what is written is byte for byte the project that was read.
/// </remarks>
public static class Graph2FontWriter {

  public static byte[] ToBytes(Graph2FontFile file) {
    var data = file.Data;
    if (data == null || data.Length == 0)
      throw new InvalidDataException("Nothing to write: a Graph2Font project is its tables.");

    return data.AsSpan().ToArray();
  }
}
