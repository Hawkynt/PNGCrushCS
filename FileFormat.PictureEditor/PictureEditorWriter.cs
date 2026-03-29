using System;

namespace FileFormat.PictureEditor;

/// <summary>Assembles Picture Editor file bytes from a <see cref="PictureEditorFile"/>.</summary>
public static class PictureEditorWriter {

  public static byte[] ToBytes(PictureEditorFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[PictureEditorFile.ExpectedFileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, PictureEditorFile.ExpectedFileSize)).CopyTo(result);
    return result;
  }
}
