using System;
using System.IO;

namespace FileFormat.RicohFax;

/// <summary>Reads RicohFax RIC files from bytes, streams, or file paths.</summary>
public static class RicohFaxReader {

  public static RicohFaxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("RIC file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static RicohFaxFile FromStream(Stream stream) {
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

  public static RicohFaxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < RicohFaxFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid RIC file (need at least {RicohFaxFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != RicohFaxFile.Magic[0] || data[1] != RicohFaxFile.Magic[1] || data[2] != RicohFaxFile.Magic[2] || data[3] != RicohFaxFile.Magic[3])
      throw new InvalidDataException("Invalid RIC magic bytes.");

    var width = BitConverter.ToUInt16(data, 4);
    var height = BitConverter.ToUInt16(data, 6);
    var resolution = BitConverter.ToUInt16(data, 8);
    var compression = BitConverter.ToUInt16(data, 10);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid RIC dimensions: {width}x{height}.");

    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    if (data.Length < RicohFaxFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("RIC file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(RicohFaxFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Resolution = resolution,
      Compression = compression,
      PixelData = pixelData,
    };
  }
}
