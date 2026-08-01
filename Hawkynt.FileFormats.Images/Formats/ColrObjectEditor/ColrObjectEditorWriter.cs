using System;
using System.IO;

namespace FileFormat.ColrObjectEditor;

/// <summary>Assembles a COLR Object Editor drawing, which is bitplanes and nothing else.</summary>
/// <remarks>
/// The colours are not in this file. They live beside it, and nothing will open the drawing without
/// them — see the companion the format writes alongside.
/// </remarks>
public static class ColrObjectEditorWriter {

  public static byte[] ToBytes(ColrObjectEditorFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Data ?? new byte[ColrObjectEditorFile.FileSize];
  }

  public static void ToFile(ColrObjectEditorFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
