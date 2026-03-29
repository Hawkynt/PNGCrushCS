using System;
using System.IO;

namespace FileFormat.MobileFax;

/// <summary>Reads MobileFax RFA files from bytes, streams, or file paths.</summary>
public static class MobileFaxReader {

  public static MobileFaxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("RFA file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MobileFaxFile FromStream(Stream stream) {
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

  public static MobileFaxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < MobileFaxFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid RFA file (need at least {MobileFaxFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != MobileFaxFile.Magic[0] || data[1] != MobileFaxFile.Magic[1])
      throw new InvalidDataException("Invalid RFA magic bytes.");

    var version = BitConverter.ToUInt16(data, 2);
    var width = BitConverter.ToUInt16(data, 4);
    var height = BitConverter.ToUInt16(data, 6);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid RFA dimensions: {width}x{height}.");

    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    if (data.Length < MobileFaxFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("RFA file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(MobileFaxFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Version = version,
      PixelData = pixelData,
    };
  }
}
