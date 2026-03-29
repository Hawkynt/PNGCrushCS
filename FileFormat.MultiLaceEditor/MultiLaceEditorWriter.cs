using System;

namespace FileFormat.MultiLaceEditor;

/// <summary>Assembles Multi-Lace Editor (.mle) file bytes from a MultiLaceEditorFile.</summary>
public static class MultiLaceEditorWriter {

  public static byte[] ToBytes(MultiLaceEditorFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[MultiLaceEditorFile.LoadAddressSize + file.RawData.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result.AsSpan(MultiLaceEditorFile.LoadAddressSize));

    return result;
  }
}
