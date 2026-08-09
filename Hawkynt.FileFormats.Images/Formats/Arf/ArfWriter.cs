using System;
using System.IO;

namespace FileFormat.Arf;

/// <summary>Writes ARF pictures (.arf).</summary>
/// <remarks>
/// The header goes out with the picture standing directly behind it, at type 0 and version 2, which
/// is the shape XnView reads. The two words the reader does not use are written as zero because
/// nothing is known about what belongs in them.
/// </remarks>
public static class ArfWriter {

  public static byte[] ToBytes(ArfFile file) {
    if (file.PixelData == null)
      throw new InvalidOperationException("No picture to write.");
    if (file.Width is < 1 or > ArfFile.MaximumSide || file.Height is < 1 or > ArfFile.MaximumSide)
      throw new InvalidOperationException($"An ARF picture of {file.Width}x{file.Height} cannot be written.");

    var expected = (long)file.Width * file.Height;
    if (file.PixelData.Length != expected)
      throw new InvalidOperationException($"A {file.Width}x{file.Height} picture needs {expected} bytes and {file.PixelData.Length} were given.");

    var output = new byte[ArfFile.HeaderSize + expected];
    ArfFile.Magic.CopyTo(output);
    _Write(output, 4, ArfFile.SupportedVersion);
    _Write(output, 8, (uint)file.Height);
    _Write(output, 12, (uint)file.Width);
    _Write(output, 16, 0);
    _Write(output, 24, ArfFile.HeaderSize);
    file.PixelData.CopyTo(output, ArfFile.HeaderSize);
    return output;
  }

  public static void ToStream(ArfFile file, Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    var bytes = ToBytes(file);
    stream.Write(bytes, 0, bytes.Length);
  }

  public static void ToFile(ArfFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }

  private static void _Write(byte[] data, int at, uint value) {
    data[at] = (byte)(value >> 24);
    data[at + 1] = (byte)(value >> 16);
    data[at + 2] = (byte)(value >> 8);
    data[at + 3] = (byte)value;
  }
}
