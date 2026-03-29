using System;
using System.IO;

namespace FileFormat.SmartFax;

/// <summary>Reads SmartFax SMF files from bytes, streams, or file paths.</summary>
public static class SmartFaxReader {

  public static SmartFaxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("SMF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SmartFaxFile FromStream(Stream stream) {
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

  public static SmartFaxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < SmartFaxFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid SMF file (need at least {SmartFaxFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != SmartFaxFile.Magic[0] || data[1] != SmartFaxFile.Magic[1] || data[2] != SmartFaxFile.Magic[2] || data[3] != SmartFaxFile.Magic[3])
      throw new InvalidDataException("Invalid SMF magic bytes.");

    var width = BitConverter.ToUInt16(data, 4);
    var height = BitConverter.ToUInt16(data, 6);
    var flags = BitConverter.ToUInt16(data, 8);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid SMF dimensions: {width}x{height}.");

    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    if (data.Length < SmartFaxFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("SMF file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(SmartFaxFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Flags = flags,
      PixelData = pixelData,
    };
  }
}
