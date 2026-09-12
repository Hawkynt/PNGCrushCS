using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.FliProfi;

/// <summary>Reads FLI Profi (.fpr) files from bytes, streams, or file paths.</summary>
public static class FliProfiReader {

  public static FliProfiFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("FLI Profi file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FliProfiFile FromStream(Stream stream) {
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

  public static FliProfiFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < FliProfiFile.FileSize)
      throw new InvalidDataException(
        $"A FLI Profi picture takes {FliProfiFile.FileSize} bytes; this file is {data.Length}.");

    return new() {
      LoadAddress = BinaryPrimitives.ReadUInt16LittleEndian(data),
      Sprites = data.Slice(FliProfiFile.SpritesOffset, FliProfiFile.SpriteBlockCount * FliProfiFile.SpriteBlockSize).ToArray(),
      SpriteColors = data.Slice(FliProfiFile.SpriteColorsOffset, FliProfiFile.FixedHeight).ToArray(),
      BorderColors = data.Slice(FliProfiFile.BorderColorsOffset, FliProfiFile.FixedHeight).ToArray(),
      FirstSpriteMulticolor = data[FliProfiFile.FirstSpriteMulticolorOffset],
      SecondSpriteMulticolor = data[FliProfiFile.SecondSpriteMulticolorOffset],
      ColorRam = data.Slice(FliProfiFile.ColorRamOffset, Commodore64Fli.ColorRamSize).ToArray(),
      Matrices = data.Slice(FliProfiFile.MatricesOffset, Commodore64Fli.MatrixAreaSize).ToArray(),
      BitmapData = data.Slice(FliProfiFile.BitmapOffset, Commodore64Fli.BitmapSize).ToArray(),
    };
  }

  public static FliProfiFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
