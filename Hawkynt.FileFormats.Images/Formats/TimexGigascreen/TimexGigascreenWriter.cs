using System;
using System.IO;

namespace FileFormat.TimexGigascreen;

/// <summary>Assembles a gigascreen picture: two screens, each a bitmap and one colour.</summary>
public static class TimexGigascreenWriter {

  public static byte[] ToBytes(TimexGigascreenFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Data ?? new byte[TimexGigascreenFile.FileSize];
  }

  public static void ToFile(TimexGigascreenFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
