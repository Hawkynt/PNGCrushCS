using System;

namespace FileFormat.EmcEditor;

/// <summary>Assembles Commodore 64 EMC Editor (.emc) file bytes from an EmcEditorFile.</summary>
public static class EmcEditorWriter {

  public static byte[] ToBytes(EmcEditorFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[EmcEditorFile.LoadAddressSize + file.RawData.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result.AsSpan(EmcEditorFile.LoadAddressSize));

    return result;
  }
}
