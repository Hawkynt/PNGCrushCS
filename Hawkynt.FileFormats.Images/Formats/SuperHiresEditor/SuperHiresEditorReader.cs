using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.SuperHiresEditor;

/// <summary>Reads Super Hires Editor (.she) logos from bytes, streams, or file paths.</summary>
public static class SuperHiresEditorReader {

  public static SuperHiresEditorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Super Hires Editor file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SuperHiresEditorFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static SuperHiresEditorFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < SuperHiresEditorFile.FileSize)
      throw new InvalidDataException(
        $"A Super Hires Editor logo takes {SuperHiresEditorFile.FileSize} bytes; this file is {data.Length}.");

    return new() {
      LoadAddress = BinaryPrimitives.ReadUInt16LittleEndian(data),
      BitmapData = data.Slice(SuperHiresEditorFile.BitmapOffset, SuperHiresEditorFile.BitmapSize).ToArray(),
      ScreenData = data.Slice(SuperHiresEditorFile.ScreenOffset, SuperHiresEditorFile.ScreenSize).ToArray(),
      Sprites = data.Slice(SuperHiresEditorFile.BackSpritesOffset, SuperHiresEditorFile.SpriteAreaSize).ToArray(),
      BackSpriteColor = data[SuperHiresEditorFile.BackColorOffset],
      FrontSpriteColor = data[SuperHiresEditorFile.FrontColorOffset],
      Trailer = data.Slice(
        SuperHiresEditorFile.FrontColorOffset + 1,
        SuperHiresEditorFile.FileSize - SuperHiresEditorFile.FrontColorOffset - 1).ToArray(),
    };
  }

  public static SuperHiresEditorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
