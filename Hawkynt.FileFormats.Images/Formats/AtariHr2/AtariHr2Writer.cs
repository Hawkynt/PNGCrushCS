using System;
using System.IO;

namespace FileFormat.AtariHr2;

/// <summary>Assembles an HR2 picture: the hires field, the colour field, then the registers of each.</summary>
public static class AtariHr2Writer {

  public static byte[] ToBytes(AtariHr2File file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Data ?? new byte[AtariHr2File.FileSize];
  }

  public static void ToFile(AtariHr2File file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
