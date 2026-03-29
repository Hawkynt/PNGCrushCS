using System;
using System.IO;

namespace FileFormat.WorldportFax;

/// <summary>Reads WorldportFax WPF files from bytes, streams, or file paths.</summary>
public static class WorldportFaxReader {

  public static WorldportFaxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("WPF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static WorldportFaxFile FromStream(Stream stream) {
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

  public static WorldportFaxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < WorldportFaxFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid WPF file (need at least {WorldportFaxFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != WorldportFaxFile.Magic[0] || data[1] != WorldportFaxFile.Magic[1] || data[2] != WorldportFaxFile.Magic[2] || data[3] != WorldportFaxFile.Magic[3])
      throw new InvalidDataException("Invalid WPF magic bytes.");

    var width = BitConverter.ToUInt16(data, 4);
    var height = BitConverter.ToUInt16(data, 6);
    var flags = BitConverter.ToUInt16(data, 8);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid WPF dimensions: {width}x{height}.");

    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    if (data.Length < WorldportFaxFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("WPF file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(WorldportFaxFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Flags = flags,
      PixelData = pixelData,
    };
  }
}
