using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.FliEditor;

/// <summary>Assembles FLI Editor (.fed) file bytes from a <see cref="FliEditorFile"/>.</summary>
public static class FliEditorWriter {

  public static byte[] ToBytes(FliEditorFile file) {
    ArgumentNullException.ThrowIfNull(file.ColorRam);
    ArgumentNullException.ThrowIfNull(file.Matrices);
    ArgumentNullException.ThrowIfNull(file.BitmapData);

    var result = new byte[FliEditorFile.FileSize];
    BinaryPrimitives.WriteUInt16LittleEndian(result, file.LoadAddress);

    if (file.Backgrounds != null)
      _Copy(file.Backgrounds, result, FliEditorFile.BackgroundsOffset, FliEditorFile.FixedHeight);

    _Copy(file.ColorRam, result, FliEditorFile.ColorRamOffset, Commodore64Fli.ColorRamSize);
    _Copy(file.Matrices, result, FliEditorFile.MatricesOffset, Commodore64Fli.MatrixAreaSize);
    _Copy(file.BitmapData, result, FliEditorFile.BitmapOffset, Commodore64Fli.BitmapSize);

    return result;
  }

  private static void _Copy(byte[] source, byte[] destination, int at, int length)
    => source.AsSpan(0, Math.Min(source.Length, length)).CopyTo(destination.AsSpan(at));
}
