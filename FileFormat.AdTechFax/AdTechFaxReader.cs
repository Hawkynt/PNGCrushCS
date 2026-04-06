using System;
using System.IO;

namespace FileFormat.AdTechFax;

/// <summary>Reads AdTech fax files from bytes, streams, or file paths.</summary>
public static class AdTechFaxReader {

  public static AdTechFaxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ADT file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AdTechFaxFile FromStream(Stream stream) {
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

  public static AdTechFaxFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static AdTechFaxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < AdTechFaxFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid ADT file (need at least {AdTechFaxFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != AdTechFaxFile.Magic[0] || data[1] != AdTechFaxFile.Magic[1] || data[2] != AdTechFaxFile.Magic[2] || data[3] != AdTechFaxFile.Magic[3])
      throw new InvalidDataException("Invalid ADT magic bytes.");

    var width = BitConverter.ToUInt16(data, 4);
    var height = BitConverter.ToUInt16(data, 6);
    var resolution = BitConverter.ToUInt16(data, 8);
    var reserved = BitConverter.ToUInt16(data, 10);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid ADT dimensions: {width}x{height}.");

    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    if (data.Length < AdTechFaxFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("ADT file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(AdTechFaxFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Resolution = resolution,
      Reserved = reserved,
      PixelData = pixelData,
    };
  }
}
