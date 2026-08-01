using System;
using System.IO;

namespace FileFormat.McPainter;

/// <summary>Assembles a McPainter picture: two fields, then the two sets of registers.</summary>
public static class McPainterWriter {

  public static byte[] ToBytes(McPainterFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Data ?? new byte[McPainterFile.FileSize];
  }

  public static void ToFile(McPainterFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
