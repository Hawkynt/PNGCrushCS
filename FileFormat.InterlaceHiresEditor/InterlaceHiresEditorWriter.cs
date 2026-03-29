using System;

namespace FileFormat.InterlaceHiresEditor;

/// <summary>Assembles Interlace Hires Editor (.ihe) file bytes from an InterlaceHiresEditorFile.</summary>
public static class InterlaceHiresEditorWriter {

  public static byte[] ToBytes(InterlaceHiresEditorFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[InterlaceHiresEditorFile.LoadAddressSize + file.RawData.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result.AsSpan(InterlaceHiresEditorFile.LoadAddressSize));

    return result;
  }
}
