using System;

namespace FileFormat.MuifliEditor;

/// <summary>Assembles MUIFLI Editor (.muf/.mui/.mup) file bytes from a MuifliEditorFile.</summary>
public static class MuifliEditorWriter {

  public static byte[] ToBytes(MuifliEditorFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[MuifliEditorFile.LoadAddressSize + file.RawData.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result.AsSpan(MuifliEditorFile.LoadAddressSize));

    return result;
  }
}
