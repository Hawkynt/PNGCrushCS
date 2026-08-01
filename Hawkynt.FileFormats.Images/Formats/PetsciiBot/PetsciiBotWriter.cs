using System;
using System.IO;

namespace FileFormat.PetsciiBot;

/// <summary>Assembles a PETSCII BOT picture: the cell colours, then their characters.</summary>
public static class PetsciiBotWriter {

  public static byte[] ToBytes(PetsciiBotFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Data ?? [];
  }

  public static void ToFile(PetsciiBotFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
