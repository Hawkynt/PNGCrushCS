using System;

namespace FileFormat.GraphLogo;

/// <summary>Assembles Graph picture bytes from a <see cref="GraphLogoFile"/>.</summary>
/// <remarks>
/// The file is one array from its bank numbers to its colour registers, and the reader keeps it
/// whole because every area is found by counting from one end or the other. So writing it is
/// returning it, and the assembling is done where the picture is turned into it.
/// </remarks>
public static class GraphLogoWriter {

  public static byte[] ToBytes(GraphLogoFile file) {
    ArgumentNullException.ThrowIfNull(file);

    return (file.Data ?? [])[..];
  }
}
