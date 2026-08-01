using System;
using FileFormat.Core;

namespace FileFormat.GraphSaurus;

/// <summary>Assembles Graph Saurus file bytes from a <see cref="GraphSaurusFile"/>.</summary>
public static class GraphSaurusWriter {

  public static byte[] ToBytes(GraphSaurusFile file) {
    var bitmap = file.PixelData ?? [];
    var length = GraphSaurusFile.FixedHeight * file.Stride;
    var result = new byte[GraphSaurusFile.HeaderSize + length];

    // Where the screen sits in video memory, which is what a BSAVE header says and what tells a
    // reader the file is one rather than a raw dump.
    MsxGraphics.WriteBsaveHeader(result, length - 1);
    bitmap.AsSpan(0, Math.Min(bitmap.Length, length)).CopyTo(result.AsSpan(GraphSaurusFile.HeaderSize));

    return result;
  }
}
