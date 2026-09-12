using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.FliDesigner2;

/// <summary>Assembles FLI Designer 2 (.fd2) file bytes from a <see cref="FliDesigner2File"/>.</summary>
/// <remarks>
/// Always the full 17409: the length is what tells the reference decoder which of the two things a
/// .fd2 can be it is holding, so a file stopping at the last byte of the bitmap would be the other
/// one.
/// </remarks>
public static class FliDesigner2Writer {

  public static byte[] ToBytes(FliDesigner2File file) {
    ArgumentNullException.ThrowIfNull(file.ColorRam);
    ArgumentNullException.ThrowIfNull(file.Matrices);
    ArgumentNullException.ThrowIfNull(file.BitmapData);

    var result = new byte[FliDesigner2File.FileSize];
    BinaryPrimitives.WriteUInt16LittleEndian(result, file.LoadAddress);

    _Copy(file.ColorRam, result, FliDesigner2File.ColorRamOffset, Commodore64Fli.ColorRamSize);
    _Copy(file.Matrices, result, FliDesigner2File.MatricesOffset, Commodore64Fli.MatrixAreaSize);
    _Copy(file.BitmapData, result, FliDesigner2File.BitmapOffset, Commodore64Fli.BitmapSize);
    if (file.Trailer != null)
      _Copy(file.Trailer, result, FliDesigner2File.PictureSize, FliDesigner2File.FileSize - FliDesigner2File.PictureSize);

    return result;
  }

  private static void _Copy(byte[] source, byte[] destination, int at, int length)
    => source.AsSpan(0, Math.Min(source.Length, length)).CopyTo(destination.AsSpan(at));
}
