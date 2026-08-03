using System;
using System.IO;

namespace FileFormat.TilezTexture;

/// <summary>Reads Tilez textures from bytes, streams, or file paths.</summary>
public static class TilezTextureReader {

  public static TilezTextureFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Tilez texture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static TilezTextureFile FromStream(Stream stream) {
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

  public static TilezTextureFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length <= TilezTextureFile.HeaderSize + 3)
      throw new InvalidDataException($"Data too small for a Tilez texture (got {data.Length} bytes).");

    if (!data[..TilezTextureFile.Magic.Length].SequenceEqual(TilezTextureFile.Magic))
      throw new InvalidDataException("Not a Tilez texture: it does not open with QDB.");

    if (data[TilezTextureFile.HeaderSize] != 0xFF || data[TilezTextureFile.HeaderSize + 1] != 0xD8)
      throw new InvalidDataException("A Tilez texture carries a JPEG eight bytes in; this file does not.");

    return new() { Embedded = data[TilezTextureFile.HeaderSize..].ToArray() };
  }

  public static TilezTextureFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
