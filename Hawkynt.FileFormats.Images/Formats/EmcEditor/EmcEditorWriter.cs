using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.EmcEditor;

/// <summary>Assembles EMC-editor (.emc) file bytes from an <see cref="EmcEditorFile"/>.</summary>
public static class EmcEditorWriter {

  public static byte[] ToBytes(EmcEditorFile file) {
    ArgumentNullException.ThrowIfNull(file.Matrices);
    ArgumentNullException.ThrowIfNull(file.BitmapData);
    ArgumentNullException.ThrowIfNull(file.ColorRam);

    var result = new byte[EmcEditorFile.FileSize];
    BinaryPrimitives.WriteUInt16LittleEndian(result, file.LoadAddress);

    _Copy(file.Matrices, result, EmcEditorFile.MatricesOffset, Commodore64Fli.MatrixAreaSize);
    _Copy(file.BitmapData, result, EmcEditorFile.BitmapOffset, Commodore64Fli.BitmapSize);
    _Copy(file.ColorRam, result, EmcEditorFile.ColorRamOffset, Commodore64Fli.ColorRamSize);
    result[EmcEditorFile.BackgroundOffset] = file.Background;

    return result;
  }

  private static void _Copy(byte[] source, byte[] destination, int at, int length)
    => source.AsSpan(0, Math.Min(source.Length, length)).CopyTo(destination.AsSpan(at));
}
