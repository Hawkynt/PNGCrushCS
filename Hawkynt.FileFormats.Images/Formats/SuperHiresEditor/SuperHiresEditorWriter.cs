using System;
using System.Buffers.Binary;

namespace FileFormat.SuperHiresEditor;

/// <summary>Assembles Super Hires Editor (.she) logo bytes from a <see cref="SuperHiresEditorFile"/>.</summary>
public static class SuperHiresEditorWriter {

  public static byte[] ToBytes(SuperHiresEditorFile file) {
    ArgumentNullException.ThrowIfNull(file.BitmapData);
    ArgumentNullException.ThrowIfNull(file.ScreenData);

    var result = new byte[SuperHiresEditorFile.FileSize];
    BinaryPrimitives.WriteUInt16LittleEndian(result, file.LoadAddress);

    _Copy(file.BitmapData, result, SuperHiresEditorFile.BitmapOffset, SuperHiresEditorFile.BitmapSize);
    _Copy(file.ScreenData, result, SuperHiresEditorFile.ScreenOffset, SuperHiresEditorFile.ScreenSize);
    if (file.Sprites != null)
      _Copy(file.Sprites, result, SuperHiresEditorFile.BackSpritesOffset, SuperHiresEditorFile.SpriteAreaSize);

    result[SuperHiresEditorFile.BackColorOffset] = file.BackSpriteColor;
    result[SuperHiresEditorFile.FrontColorOffset] = file.FrontSpriteColor;

    if (file.Trailer != null)
      _Copy(
        file.Trailer, result, SuperHiresEditorFile.FrontColorOffset + 1,
        SuperHiresEditorFile.FileSize - SuperHiresEditorFile.FrontColorOffset - 1);

    return result;
  }

  private static void _Copy(byte[] source, byte[] destination, int at, int length)
    => source.AsSpan(0, Math.Min(source.Length, length)).CopyTo(destination.AsSpan(at));
}
