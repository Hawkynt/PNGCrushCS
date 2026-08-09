using System;
using System.IO;

namespace FileFormat.GeGenesis;

/// <summary>Writes GE Genesis 5.x images (.fre).</summary>
/// <remarks>
/// What goes out is the control header and the samples, uncompressed, with the pointers to the
/// identifier, unpack and compression blocks left at zero because none of those blocks is written.
/// The compression code written is 1, which is the code the file measured here carries for an
/// uncompressed picture.
/// </remarks>
public static class GeGenesisWriter {

  /// <summary>The compression code an uncompressed picture carries.</summary>
  public const int UncompressedCode = 1;

  public static byte[] ToBytes(GeGenesisFile file) {
    if (file.PixelData == null)
      throw new InvalidOperationException("No picture to write.");

    var expected = (long)file.Width * file.Height * (file.Depth / 8);
    if (file.Depth is not (8 or 16))
      throw new InvalidOperationException($"A GE Genesis image is written at 8 or 16 bits, not {file.Depth}.");
    if (file.PixelData.Length != expected)
      throw new InvalidOperationException($"A {file.Width}x{file.Height} picture at {file.Depth} bits needs {expected} bytes and {file.PixelData.Length} were given.");

    var output = new byte[GeGenesisFile.ControlHeaderSize + expected];
    GeGenesisFile.Magic.CopyTo(output);
    _WriteBigEndian(output, 4, GeGenesisFile.ControlHeaderSize);
    _WriteBigEndian(output, 8, file.Width);
    _WriteBigEndian(output, 12, file.Height);
    _WriteBigEndian(output, 16, file.Depth);
    _WriteBigEndian(output, 20, UncompressedCode);
    file.PixelData.CopyTo(output, GeGenesisFile.ControlHeaderSize);
    return output;
  }

  public static void ToStream(GeGenesisFile file, Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    var bytes = ToBytes(file);
    stream.Write(bytes, 0, bytes.Length);
  }

  public static void ToFile(GeGenesisFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }

  private static void _WriteBigEndian(byte[] data, int offset, int value) {
    data[offset] = (byte)(value >> 24);
    data[offset + 1] = (byte)(value >> 16);
    data[offset + 2] = (byte)(value >> 8);
    data[offset + 3] = (byte)value;
  }
}
