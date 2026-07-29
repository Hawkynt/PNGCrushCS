using System;

namespace FileFormat.EciGraphicEditor;

/// <summary>Assembles ECI Graphic Editor (.eci/.ecp) file bytes from an EciGraphicEditorFile.</summary>
public static class EciGraphicEditorWriter {

  public static byte[] ToBytes(EciGraphicEditorFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[EciGraphicEditorFile.LoadAddressSize + file.RawData.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result.AsSpan(EciGraphicEditorFile.LoadAddressSize));

    return result;
  }
}
