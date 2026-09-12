using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.HiresManager;

/// <summary>Assembles Hires Manager (.him) file bytes from a <see cref="HiresManagerFile"/>.</summary>
/// <remarks>
/// The load address and the not-packed byte are written rather than carried: both are part of what
/// says the file is a Hires Manager, and a picture that states anything else is read as a run-length
/// stream and refused.
/// </remarks>
public static class HiresManagerWriter {

  public static byte[] ToBytes(HiresManagerFile file) {
    ArgumentNullException.ThrowIfNull(file.BitmapData);
    ArgumentNullException.ThrowIfNull(file.Matrices);

    var result = new byte[HiresManagerFile.FileSize];
    BinaryPrimitives.WriteUInt16LittleEndian(result, HiresManagerFile.FixedLoadAddress);
    result[HiresManagerFile.PackingFlagOffset] = HiresManagerFile.NotPacked;

    _Copy(file.BitmapData, result, HiresManagerFile.BitmapOffset, HiresManagerFile.BitmapSize);

    for (var line = 0; line < Commodore64Fli.MatrixCount; ++line)
      file.Matrices.AsSpan(line * Commodore64Fli.MatrixStride, HiresManagerFile.MatrixEntries)
        .CopyTo(result.AsSpan(HiresManagerFile.MatricesOffset + line * Commodore64Fli.MatrixStride));

    return result;
  }

  private static void _Copy(byte[] source, byte[] destination, int at, int length)
    => source.AsSpan(0, Math.Min(source.Length, length)).CopyTo(destination.AsSpan(at));
}
