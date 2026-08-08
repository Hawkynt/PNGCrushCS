using System;

namespace FileFormat.LudekMaker;

/// <summary>Assembles Ludek Maker sheet bytes from a <see cref="LudekMakerFile"/>.</summary>
/// <remarks>
/// The sheet is one array from its signature to its last shape, and the reader keeps it whole
/// because every area sits at a fixed offset. So writing it is returning it, and the assembling is
/// done where the picture is turned into it.
/// </remarks>
public static class LudekMakerWriter {

  public static byte[] ToBytes(LudekMakerFile file) {
    ArgumentNullException.ThrowIfNull(file);

    return (file.Data ?? [])[..];
  }
}
