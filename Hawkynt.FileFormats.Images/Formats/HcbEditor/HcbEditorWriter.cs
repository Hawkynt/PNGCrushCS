using System;

namespace FileFormat.HcbEditor;

/// <summary>Assembles an HCB picture from a <see cref="HcbEditorFile"/>.</summary>
public static class HcbEditorWriter {

  public static byte[] ToBytes(HcbEditorFile file) {
    var data = file.Data ?? [];
    var result = new byte[HcbEditorFile.FileSize];
    data.AsSpan(0, Math.Min(data.Length, result.Length)).CopyTo(result);

    return result;
  }
}
