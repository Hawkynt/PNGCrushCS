using System;
using System.IO;

namespace FileFormat.VertiZontalInterlacing;

/// <summary>Assembles a VertiZontal Interlacing picture: two Graphics 9 fields, back to back.</summary>
public static class VertiZontalInterlacingWriter {

  public static byte[] ToBytes(VertiZontalInterlacingFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Data ?? new byte[VertiZontalInterlacingFile.FileSize];
  }

  public static void ToFile(VertiZontalInterlacingFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
