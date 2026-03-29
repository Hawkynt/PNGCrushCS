using System;
using System.IO;

namespace FileFormat.ByuSir;

/// <summary>Reads BYU SIR files from bytes, streams, or file paths.</summary>
public static class ByuSirReader {

  public static ByuSirFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("SIR file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ByuSirFile FromStream(Stream stream) {
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

  public static ByuSirFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < ByuSirFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid SIR file (need at least {ByuSirFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != ByuSirFile.Magic[0] || data[1] != ByuSirFile.Magic[1] || data[2] != ByuSirFile.Magic[2] || data[3] != ByuSirFile.Magic[3])
      throw new InvalidDataException("Invalid SIR magic bytes.");

    var width = BitConverter.ToUInt16(data, 4);
    var height = BitConverter.ToUInt16(data, 6);
    var dataType = BitConverter.ToUInt16(data, 8);
    var reserved = BitConverter.ToUInt16(data, 10);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid SIR dimensions: {width}x{height}.");

    var pixelDataSize = width * height;
    if (data.Length < ByuSirFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("SIR file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(ByuSirFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      DataType = dataType,
      Reserved = reserved,
      PixelData = pixelData,
    };
  }
}
