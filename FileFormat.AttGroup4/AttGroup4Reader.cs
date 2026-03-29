using System;
using System.IO;

namespace FileFormat.AttGroup4;

/// <summary>Reads AT&amp;T Group 4 fax files from bytes, streams, or file paths.</summary>
public static class AttGroup4Reader {

  public static AttGroup4File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ATT file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AttGroup4File FromStream(Stream stream) {
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

  public static AttGroup4File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < AttGroup4File.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid ATT file (need at least {AttGroup4File.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != AttGroup4File.Magic[0] || data[1] != AttGroup4File.Magic[1] || data[2] != AttGroup4File.Magic[2] || data[3] != AttGroup4File.Magic[3])
      throw new InvalidDataException("Invalid ATT magic bytes.");

    var width = BitConverter.ToUInt16(data, 4);
    var height = BitConverter.ToUInt16(data, 6);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid ATT dimensions: {width}x{height}.");

    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    if (data.Length < AttGroup4File.HeaderSize + pixelDataSize)
      throw new InvalidDataException("ATT file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(AttGroup4File.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }
}
