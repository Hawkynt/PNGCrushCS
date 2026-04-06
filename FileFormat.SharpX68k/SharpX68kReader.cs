using System;
using System.IO;

namespace FileFormat.SharpX68k;

/// <summary>Parses sharp x68000 16-bit color screen from raw bytes.</summary>
public static class SharpX68kReader {

  public static SharpX68kFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("File not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SharpX68kFile FromStream(Stream stream) {
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

  public static SharpX68kFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static SharpX68kFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < SharpX68kFile.HeaderSize)
      throw new InvalidDataException($"Data too small: {data.Length} bytes, expected at least 8.");

    var width = data[0] | (data[1] << 8);
    var height = data[2] | (data[3] << 8);
    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid dimensions: {width}x{height}");

    var pixelCount = width * height;
    var pixelData = new byte[pixelCount * 3];
    for (var i = 0; i < pixelCount && SharpX68kFile.HeaderSize + i * 2 + 1 < data.Length; ++i) {
      var offset = SharpX68kFile.HeaderSize + i * 2;
      var rgb555 = (ushort)(data[offset] | (data[offset + 1] << 8));
      pixelData[i * 3] = (byte)(((rgb555 >> 10) & 0x1F) << 3);
      pixelData[i * 3 + 1] = (byte)(((rgb555 >> 5) & 0x1F) << 3);
      pixelData[i * 3 + 2] = (byte)((rgb555 & 0x1F) << 3);
    }

    return new SharpX68kFile { Width = width, Height = height, PixelData = pixelData };
  }
}
