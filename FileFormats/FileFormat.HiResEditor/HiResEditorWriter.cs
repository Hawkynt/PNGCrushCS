using System;
using System.Buffers.Binary;

namespace FileFormat.HiResEditor;

/// <summary>Assembles Hires-Editor picture bytes from a <see cref="HiResEditorFile"/>.</summary>
public static class HiResEditorWriter {

  public static byte[] ToBytes(HiResEditorFile file) {
    var result = new byte[HiResEditorFile.ExpectedFileSize];
    BinaryPrimitives.WriteUInt16LittleEndian(result, file.LoadAddress);

    _Copy(file.ScreenData, result, HiResEditorFile.ScreenDataOffset, HiResEditorFile.ScreenDataSize);
    _Copy(file.BitmapData, result, HiResEditorFile.BitmapDataOffset, HiResEditorFile.BitmapDataSize);

    return result;
  }

  private static void _Copy(byte[]? source, byte[] destination, int offset, int length) {
    var data = source ?? [];
    data.AsSpan(0, Math.Min(data.Length, length)).CopyTo(destination.AsSpan(offset));
  }
}
