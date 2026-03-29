using System;

namespace FileFormat.UfliEditor;

/// <summary>Assembles UFLI Editor (.ufl) file bytes from a UfliEditorFile.</summary>
public static class UfliEditorWriter {

  public static byte[] ToBytes(UfliEditorFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[UfliEditorFile.LoadAddressSize + file.RawData.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result.AsSpan(UfliEditorFile.LoadAddressSize));

    return result;
  }
}
