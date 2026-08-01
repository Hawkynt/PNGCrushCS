using System;
using System.IO;

namespace FileFormat.Stellar;

/// <summary>Assembles a Stellar picture, which is two fields of colour blocks and nothing else.</summary>
public static class StellarWriter {

  public static byte[] ToBytes(StellarFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Data ?? new byte[StellarFile.FileSize];
  }

  public static void ToFile(StellarFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
