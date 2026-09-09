using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.Ffli;

/// <summary>Assembles Flash FLI (.ffl, .ffli) file bytes from an <see cref="FfliFile"/>.</summary>
public static class FfliWriter {

  public static byte[] ToBytes(FfliFile file) {
    ArgumentNullException.ThrowIfNull(file.ColorRam);
    ArgumentNullException.ThrowIfNull(file.BitmapData);
    ArgumentNullException.ThrowIfNull(file.FirstMatrices);
    ArgumentNullException.ThrowIfNull(file.SecondMatrices);

    var result = new byte[FfliFile.FileSize];
    BinaryPrimitives.WriteUInt16LittleEndian(result, file.LoadAddress);
    result[FfliFile.SignatureOffset] = FfliFile.Signature;

    if (file.FirstBackgrounds != null)
      _Copy(file.FirstBackgrounds, result, FfliFile.FirstBackgroundsOffset, FfliFile.FixedHeight);
    if (file.SecondBackgrounds != null)
      _Copy(file.SecondBackgrounds, result, FfliFile.SecondBackgroundsOffset, FfliFile.FixedHeight);

    _Copy(file.ColorRam, result, FfliFile.ColorRamOffset, Commodore64Fli.ColorRamSize);
    _Copy(file.FirstMatrices, result, FfliFile.FirstMatricesOffset, Commodore64Fli.MatrixAreaSize);
    _Copy(file.BitmapData, result, FfliFile.BitmapOffset, Commodore64Fli.BitmapSize);
    _Copy(file.SecondMatrices, result, FfliFile.SecondMatricesOffset, Commodore64Fli.MatrixAreaSize);

    return result;
  }

  private static void _Copy(byte[] source, byte[] destination, int at, int length)
    => source.AsSpan(0, Math.Min(source.Length, length)).CopyTo(destination.AsSpan(at));
}
