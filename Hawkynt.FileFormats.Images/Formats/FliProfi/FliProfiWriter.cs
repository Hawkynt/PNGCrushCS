using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.FliProfi;

/// <summary>Assembles FLI Profi (.fpr) file bytes from a <see cref="FliProfiFile"/>.</summary>
public static class FliProfiWriter {

  public static byte[] ToBytes(FliProfiFile file) {
    ArgumentNullException.ThrowIfNull(file.ColorRam);
    ArgumentNullException.ThrowIfNull(file.Matrices);
    ArgumentNullException.ThrowIfNull(file.BitmapData);

    var result = new byte[FliProfiFile.FileSize];
    BinaryPrimitives.WriteUInt16LittleEndian(result, file.LoadAddress);

    if (file.Sprites != null)
      _Copy(file.Sprites, result, FliProfiFile.SpritesOffset, FliProfiFile.SpriteBlockCount * FliProfiFile.SpriteBlockSize);
    if (file.SpriteColors != null)
      _Copy(file.SpriteColors, result, FliProfiFile.SpriteColorsOffset, FliProfiFile.FixedHeight);
    if (file.BorderColors != null)
      _Copy(file.BorderColors, result, FliProfiFile.BorderColorsOffset, FliProfiFile.FixedHeight);

    result[FliProfiFile.FirstSpriteMulticolorOffset] = file.FirstSpriteMulticolor;
    result[FliProfiFile.SecondSpriteMulticolorOffset] = file.SecondSpriteMulticolor;

    _Copy(file.ColorRam, result, FliProfiFile.ColorRamOffset, Commodore64Fli.ColorRamSize);
    _Copy(file.Matrices, result, FliProfiFile.MatricesOffset, Commodore64Fli.MatrixAreaSize);
    _Copy(file.BitmapData, result, FliProfiFile.BitmapOffset, Commodore64Fli.BitmapSize);

    return result;
  }

  private static void _Copy(byte[] source, byte[] destination, int at, int length)
    => source.AsSpan(0, Math.Min(source.Length, length)).CopyTo(destination.AsSpan(at));
}
