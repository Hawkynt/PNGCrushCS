using System;
using System.IO;

namespace FileFormat.Sprite64;

/// <summary>Reads C64 sprite data files from bytes, streams, or file paths.</summary>
public static class Sprite64Reader {

  public static Sprite64File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("C64 sprite file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Sprite64File FromStream(Stream stream) {
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

  public static Sprite64File FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < Sprite64File.ExpectedFileSize)
      throw new InvalidDataException($"C64 sprite file too small (got {data.Length} bytes, expected {Sprite64File.ExpectedFileSize}).");

    // Sprite pixel data (63 bytes)
    var spriteData = new byte[Sprite64File.SpriteDataSize];
    data.Slice(0, Sprite64File.SpriteDataSize).CopyTo(spriteData.AsSpan(0));

    // Mode byte (byte 63)
    var modeByte = data[Sprite64File.SpriteDataSize];

    return new() {
      SpriteData = spriteData,
      ModeByte = modeByte,
    };
    }

  public static Sprite64File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < Sprite64File.ExpectedFileSize)
      throw new InvalidDataException($"C64 sprite file too small (got {data.Length} bytes, expected {Sprite64File.ExpectedFileSize}).");

    // Sprite pixel data (63 bytes)
    var spriteData = new byte[Sprite64File.SpriteDataSize];
    data.AsSpan(0, Sprite64File.SpriteDataSize).CopyTo(spriteData.AsSpan(0));

    // Mode byte (byte 63)
    var modeByte = data[Sprite64File.SpriteDataSize];

    return new() {
      SpriteData = spriteData,
      ModeByte = modeByte,
    };
  }
}
