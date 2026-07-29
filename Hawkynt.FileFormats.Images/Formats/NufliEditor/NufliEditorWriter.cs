using System;

namespace FileFormat.NufliEditor;

/// <summary>Assembles NUFLI Editor (.nuf/.nup) file bytes from a NufliEditorFile.</summary>
public static class NufliEditorWriter {

  public static byte[] ToBytes(NufliEditorFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[NufliEditorFile.LoadAddressSize + file.RawData.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result.AsSpan(NufliEditorFile.LoadAddressSize));

    return result;
  }
}
