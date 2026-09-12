using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.FliDesigner;

/// <summary>Assembles FLI Designer (.fd2) file bytes from a <see cref="FliDesignerFile"/>.</summary>
public static class FliDesignerWriter {

  public static byte[] ToBytes(FliDesignerFile file) {
    ArgumentNullException.ThrowIfNull(file.ColorRam);
    ArgumentNullException.ThrowIfNull(file.Matrices);
    ArgumentNullException.ThrowIfNull(file.BitmapData);

    var result = new byte[file.Padded ? FliDesignerFile.PaddedFileSize : FliDesignerFile.FileSize];
    BinaryPrimitives.WriteUInt16LittleEndian(result, file.LoadAddress);

    _Copy(file.ColorRam, result, FliDesignerFile.ColorRamOffset, Commodore64Fli.ColorRamSize);
    _Copy(file.Matrices, result, FliDesignerFile.MatricesOffset, Commodore64Fli.MatrixAreaSize);
    _Copy(file.BitmapData, result, FliDesignerFile.BitmapOffset, Commodore64Fli.BitmapSize);

    return result;
  }

  private static void _Copy(byte[] source, byte[] destination, int at, int length)
    => source.AsSpan(0, Math.Min(source.Length, length)).CopyTo(destination.AsSpan(at));
}
