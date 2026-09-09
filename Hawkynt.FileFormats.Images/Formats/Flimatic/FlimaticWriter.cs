using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.Flimatic;

/// <summary>Assembles Flimatic (.flm) file bytes from a <see cref="FlimaticFile"/>.</summary>
public static class FlimaticWriter {

  public static byte[] ToBytes(FlimaticFile file) {
    ArgumentNullException.ThrowIfNull(file.ColorRam);
    ArgumentNullException.ThrowIfNull(file.Matrices);
    ArgumentNullException.ThrowIfNull(file.BitmapData);

    var result = new byte[FlimaticFile.FileSize];
    BinaryPrimitives.WriteUInt16LittleEndian(result, file.LoadAddress);

    _Copy(file.ColorRam, result, FlimaticFile.ColorRamOffset, Commodore64Fli.ColorRamSize);
    _Copy(file.Matrices, result, FlimaticFile.MatricesOffset, Commodore64Fli.MatrixAreaSize);
    _Copy(file.BitmapData, result, FlimaticFile.BitmapOffset, Commodore64Fli.BitmapSize);
    if (file.Trailer != null)
      _Copy(file.Trailer, result, FlimaticFile.PictureSize, FlimaticFile.FileSize - FlimaticFile.PictureSize);

    result[FlimaticFile.BackgroundOffset] = file.Background;

    return result;
  }

  private static void _Copy(byte[] source, byte[] destination, int at, int length)
    => source.AsSpan(0, Math.Min(source.Length, length)).CopyTo(destination.AsSpan(at));
}
