using System;
using System.IO;

namespace FileFormat.InterlaceLogoDesigner;

/// <summary>Assembles an Interlace Logo Designer picture: two fields, a page apart.</summary>
public static class InterlaceLogoDesignerWriter {

  public static byte[] ToBytes(InterlaceLogoDesignerFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Data ?? new byte[InterlaceLogoDesignerFile.FileSize];
  }

  public static void ToFile(InterlaceLogoDesignerFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
