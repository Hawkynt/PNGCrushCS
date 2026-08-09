using System;
using System.IO;

namespace FileFormat.MawWareTexture;

/// <summary>Writes Maw-Ware textures (.mtx).</summary>
public static class MawWareTextureWriter {

  public static byte[] ToBytes(MawWareTextureFile file) {
    if (file.PixelData == null)
      throw new InvalidOperationException("No picture to write.");
    if (file.BytesPerPixel is not (1 or 3 or 4))
      throw new InvalidOperationException($"A Maw-Ware texture is written at 1, 3 or 4 bytes a pixel, not {file.BytesPerPixel}.");

    var expected = (long)file.Width * file.Height * file.BytesPerPixel;
    if (file.PixelData.Length != expected)
      throw new InvalidOperationException($"A {file.Width}x{file.Height} texture at {file.BytesPerPixel} bytes a pixel needs {expected} bytes and {file.PixelData.Length} were given.");

    var output = new byte[MawWareTextureFile.HeaderSize + expected];
    _Write(output, 0, MawWareTextureFile.Magic);
    _Write(output, 4, (uint)file.Width);
    _Write(output, 8, (uint)file.Height);
    _Write(output, 12, (uint)file.BytesPerPixel);
    _Write(output, 16, file.Reserved);
    file.PixelData.CopyTo(output, MawWareTextureFile.HeaderSize);
    return output;
  }

  public static void ToStream(MawWareTextureFile file, Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    var bytes = ToBytes(file);
    stream.Write(bytes, 0, bytes.Length);
  }

  public static void ToFile(MawWareTextureFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }

  private static void _Write(byte[] data, int at, uint value) {
    data[at] = (byte)value;
    data[at + 1] = (byte)(value >> 8);
    data[at + 2] = (byte)(value >> 16);
    data[at + 3] = (byte)(value >> 24);
  }
}
