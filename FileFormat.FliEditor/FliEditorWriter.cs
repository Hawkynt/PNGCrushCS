using System;

namespace FileFormat.FliEditor;

/// <summary>Assembles FLI Editor (.fed) file bytes from a FliEditorFile.</summary>
public static class FliEditorWriter {

  public static byte[] ToBytes(FliEditorFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[FliEditorFile.LoadAddressSize + file.RawData.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result.AsSpan(FliEditorFile.LoadAddressSize));

    return result;
  }
}
