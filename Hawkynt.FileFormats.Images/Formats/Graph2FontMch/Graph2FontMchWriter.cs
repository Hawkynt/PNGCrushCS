using System;

namespace FileFormat.Graph2FontMch;

/// <summary>Assembles Graph2Font MCH bytes from a <see cref="Graph2FontMchFile"/>.</summary>
/// <remarks>
/// The file is one array whose width and whether it carries sprites both follow from its length, so
/// the reader keeps it whole and writing it is returning it. The assembling is done where the
/// picture is turned into it.
/// </remarks>
public static class Graph2FontMchWriter {

  public static byte[] ToBytes(Graph2FontMchFile file) {
    ArgumentNullException.ThrowIfNull(file);

    return (file.Data ?? [])[..];
  }
}
