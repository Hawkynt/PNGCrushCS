using System;

namespace FileFormat.LarkaObjectEditor;

/// <summary>Assembles a Larka object from a <see cref="LarkaObjectEditorFile"/>.</summary>
public static class LarkaObjectEditorWriter {

  public static byte[] ToBytes(LarkaObjectEditorFile file) {
    var data = file.Data ?? [];
    var result = new byte[LarkaObjectEditorFile.FileSize];
    data.AsSpan(0, Math.Min(data.Length, result.Length)).CopyTo(result);

    return result;
  }
}
