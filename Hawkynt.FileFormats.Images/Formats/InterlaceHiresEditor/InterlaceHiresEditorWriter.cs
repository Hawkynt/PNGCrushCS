using System;
using System.Buffers.Binary;

namespace FileFormat.InterlaceHiresEditor;

/// <summary>Assembles Interlace Hires Editor picture bytes.</summary>
public static class InterlaceHiresEditorWriter {

  public static byte[] ToBytes(InterlaceHiresEditorFile file) {
    ArgumentNullException.ThrowIfNull(file.FirstBitmap);
    ArgumentNullException.ThrowIfNull(file.SecondBitmap);

    var result = new byte[InterlaceHiresEditorFile.ExpectedFileSize];
    BinaryPrimitives.WriteUInt16LittleEndian(result, file.LoadAddress);

    file.FirstBitmap.AsSpan(0, Math.Min(file.FirstBitmap.Length, InterlaceHiresEditorFile.BitmapSize))
      .CopyTo(result.AsSpan(InterlaceHiresEditorFile.FirstBitmapOffset));
    file.SecondBitmap.AsSpan(0, Math.Min(file.SecondBitmap.Length, InterlaceHiresEditorFile.BitmapSize))
      .CopyTo(result.AsSpan(InterlaceHiresEditorFile.SecondBitmapOffset));

    return result;
  }
}
