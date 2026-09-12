using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.HiresFliCrest;

/// <summary>Assembles Hires FLI Designer (.hfc, .hfd) file bytes from a <see cref="HiresFliCrestFile"/>.</summary>
public static class HiresFliCrestWriter {

  public static byte[] ToBytes(HiresFliCrestFile file) {
    ArgumentNullException.ThrowIfNull(file.BitmapData);
    ArgumentNullException.ThrowIfNull(file.Matrices);

    var result = new byte[HiresFliCrestFile.FileSize];
    BinaryPrimitives.WriteUInt16LittleEndian(result, file.LoadAddress);

    _Copy(file.BitmapData, result, HiresFliCrestFile.BitmapOffset, HiresFliCrestFile.BitmapAreaSize);
    _Copy(file.Matrices, result, HiresFliCrestFile.MatricesOffset, Commodore64Fli.MatrixAreaSize);

    return result;
  }

  private static void _Copy(byte[] source, byte[] destination, int at, int length)
    => source.AsSpan(0, Math.Min(source.Length, length)).CopyTo(destination.AsSpan(at));
}
