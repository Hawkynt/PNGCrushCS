using System;
using System.IO;

namespace FileFormat.InterlacedLogoEditor;

/// <summary>Assembles an Interlaced Logo Editor picture: two fields and four colour registers.</summary>
public static class InterlacedLogoEditorWriter {

  public static byte[] ToBytes(InterlacedLogoEditorFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Data ?? new byte[InterlacedLogoEditorFile.FileSize];
  }

  public static void ToFile(InterlacedLogoEditorFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
