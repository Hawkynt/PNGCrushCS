using System;
using System.IO;

namespace FileFormat.PetDraw;

/// <summary>Assembles a PetDraw64 screen: the background, the characters, then their colours.</summary>
public static class PetDrawWriter {

  public static byte[] ToBytes(PetDrawFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Data ?? new byte[PetDrawFile.FileSize];
  }

  public static void ToFile(PetDrawFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
