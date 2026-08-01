using System;
using System.IO;

namespace FileFormat.InterlaceGraphicsEditor;

/// <summary>Assembles an Interlace Graphics Editor picture: two register sets, then two bitmaps.</summary>
public static class InterlaceGraphicsEditorWriter {

  public static byte[] ToBytes(InterlaceGraphicsEditorFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Data ?? new byte[InterlaceGraphicsEditorFile.FileSize];
  }

  public static void ToFile(InterlaceGraphicsEditorFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
