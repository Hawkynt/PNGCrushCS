using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.PrintTechnik;

/// <summary>Reads Print-Technik greyscale scans from bytes, streams, or file paths.</summary>
public static class PrintTechnikReader {

  public static PrintTechnikFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Print-Technik scan not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PrintTechnikFile FromStream(Stream stream) {
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

  public static PrintTechnikFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length <= PrintTechnikFile.HeaderSize)
      throw new InvalidDataException($"Data too small for a Print-Technik scan: got {data.Length} bytes.");

    var width = BinaryPrimitives.ReadUInt16BigEndian(data[PrintTechnikFile.WidthAt..]);
    var height = BinaryPrimitives.ReadUInt16BigEndian(data[PrintTechnikFile.HeightAt..]);

    if (width < 1 || height < 1)
      throw new InvalidDataException($"Invalid Print-Technik size: {width}x{height}.");

    // No signature, so the size stated has to account for the file exactly.
    var needed = PrintTechnikFile.HeaderSize + width * height;
    if (data.Length != needed)
      throw new InvalidDataException($"A {width}x{height} Print-Technik scan is {needed} bytes, got {data.Length}.");

    return new() {
      Width = width,
      Height = height,
      Header = data[..PrintTechnikFile.HeaderSize].ToArray(),
      PixelData = data[PrintTechnikFile.HeaderSize..].ToArray(),
    };
  }

  public static PrintTechnikFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
