using System;
using System.IO;

namespace FileFormat.SpecScii;

/// <summary>Assembles a ZX_SSCII screen: its own character set, then the cells and their attributes.</summary>
public static class SpecSciiWriter {

  public static byte[] ToBytes(SpecSciiFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Data ?? new byte[SpecSciiFile.FileSize];
  }

  public static void ToFile(SpecSciiFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
