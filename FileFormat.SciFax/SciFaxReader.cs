using System;
using System.IO;

namespace FileFormat.SciFax;

/// <summary>Reads SciFax SCF files from bytes, streams, or file paths.</summary>
public static class SciFaxReader {

  public static SciFaxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("SCF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SciFaxFile FromStream(Stream stream) {
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

  public static SciFaxFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static SciFaxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < SciFaxFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid SCF file (need at least {SciFaxFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != SciFaxFile.Magic[0] || data[1] != SciFaxFile.Magic[1])
      throw new InvalidDataException("Invalid SCF magic bytes.");

    var version = BitConverter.ToUInt16(data, 2);
    var width = BitConverter.ToUInt16(data, 4);
    var height = BitConverter.ToUInt16(data, 6);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid SCF dimensions: {width}x{height}.");

    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    if (data.Length < SciFaxFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("SCF file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(SciFaxFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Version = version,
      PixelData = pixelData,
    };
  }
}
