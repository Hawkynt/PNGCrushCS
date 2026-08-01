using System;

namespace FileFormat.CentauriLogoEditor;

/// <summary>Assembles a Centauri logo from a <see cref="CentauriLogoEditorFile"/>.</summary>
public static class CentauriLogoEditorWriter {

  public static byte[] ToBytes(CentauriLogoEditorFile file) {
    var data = file.Data ?? [];
    var result = new byte[CentauriLogoEditorFile.FileSize];
    data.AsSpan(0, Math.Min(data.Length, result.Length)).CopyTo(result);

    return result;
  }
}
