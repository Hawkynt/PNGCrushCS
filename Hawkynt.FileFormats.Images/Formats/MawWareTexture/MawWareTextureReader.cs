using System;
using System.IO;

namespace FileFormat.MawWareTexture;

/// <summary>Reads Maw-Ware textures (.mtx) from bytes, streams, or file paths.</summary>
public static class MawWareTextureReader {

  public static MawWareTextureFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Maw-Ware texture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MawWareTextureFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var buffer = new byte[stream.Length - stream.Position];
      stream.ReadExactly(buffer);
      return FromBytes(buffer);
    }

    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromBytes(memory.ToArray());
  }

  public static MawWareTextureFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static MawWareTextureFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < MawWareTextureFile.HeaderSize)
      throw new InvalidDataException($"Data too small for a Maw-Ware texture (need at least {MawWareTextureFile.HeaderSize} bytes, got {data.Length}).");

    var magic = _Read(data, 0);
    if (magic != MawWareTextureFile.Magic)
      throw new InvalidDataException($"Not a Maw-Ware texture: it opens with 0x{magic:X} where one opens with 0x{MawWareTextureFile.Magic:X}.");

    var width = _Read(data, 4);
    var height = _Read(data, 8);
    var bytesPerPixel = _Read(data, 12);
    var reserved = _Read(data, 16);

    if (width is 0 or > ushort.MaxValue || height is 0 or > ushort.MaxValue)
      throw new InvalidDataException($"Invalid Maw-Ware texture dimensions: {width}x{height}.");

    if (bytesPerPixel is not (1 or 3 or 4))
      throw new InvalidDataException(
        bytesPerPixel == 2
          ? "A Maw-Ware texture of two bytes a pixel is not read: what XnView converts out of one bears no relation to what is in it, so there is nothing to read it against."
          : $"A Maw-Ware texture of {bytesPerPixel} bytes a pixel is not one this reads; 1, 3 and 4 are.");

    var expected = (long)width * height * bytesPerPixel;
    var available = data.Length - MawWareTextureFile.HeaderSize;

    // Four bytes of constant is not enough on its own to say what a file is, so the length is what
    // decides: a texture accounts for itself exactly and a foreign file under this name does not.
    if (available != expected)
      throw new InvalidDataException($"A {width}x{height} texture at {bytesPerPixel} bytes a pixel needs {expected} bytes and the file has {available} behind its header.");

    var pixels = new byte[expected];
    data.Slice(MawWareTextureFile.HeaderSize, (int)expected).CopyTo(pixels);

    return new() {
      Width = (int)width,
      Height = (int)height,
      BytesPerPixel = (int)bytesPerPixel,
      Reserved = reserved,
      PixelData = pixels,
    };
  }

  private static uint _Read(ReadOnlySpan<byte> data, int at)
    => (uint)(data[at] | (data[at + 1] << 8) | (data[at + 2] << 16) | (data[at + 3] << 24));
}
