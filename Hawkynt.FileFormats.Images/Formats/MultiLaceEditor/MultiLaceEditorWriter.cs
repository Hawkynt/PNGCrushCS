using System;
using System.Buffers.Binary;

namespace FileFormat.MultiLaceEditor;

/// <summary>Assembles Multi-Lace Editor (.mle) logo bytes from a <see cref="MultiLaceEditorFile"/>.</summary>
public static class MultiLaceEditorWriter {

  public static byte[] ToBytes(MultiLaceEditorFile file) {
    ArgumentNullException.ThrowIfNull(file.FirstField);
    ArgumentNullException.ThrowIfNull(file.SecondField);

    var result = new byte[MultiLaceEditorFile.FileSize];
    BinaryPrimitives.WriteUInt16LittleEndian(result, file.LoadAddress);

    _Copy(file.SecondField, result, MultiLaceEditorFile.SecondFieldOffset);
    _Copy(file.FirstField, result, MultiLaceEditorFile.FirstFieldOffset);

    return result;
  }

  private static void _Copy(byte[] source, byte[] destination, int at)
    => source.AsSpan(0, Math.Min(source.Length, MultiLaceEditorFile.FieldSize)).CopyTo(destination.AsSpan(at));
}
