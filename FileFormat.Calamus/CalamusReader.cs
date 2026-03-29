using System;
using System.IO;

namespace FileFormat.Calamus;

/// <summary>Reads Calamus raster image files from bytes, streams, or file paths.</summary>
public static class CalamusReader {

  public static CalamusFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Calamus file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CalamusFile FromStream(Stream stream) {
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

  public static CalamusFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < CalamusFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid Calamus file (need at least {CalamusFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != CalamusFile.Magic[0] || data[1] != CalamusFile.Magic[1] || data[2] != CalamusFile.Magic[2] || data[3] != CalamusFile.Magic[3])
      throw new InvalidDataException("Invalid Calamus magic bytes.");

    var version = BitConverter.ToUInt16(data, 4);
    var width = BitConverter.ToUInt16(data, 6);
    var height = BitConverter.ToUInt16(data, 8);
    var bpp = BitConverter.ToUInt16(data, 10);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid Calamus dimensions: {width}x{height}.");

    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    if (data.Length < CalamusFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("Calamus file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(CalamusFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Version = version,
      Bpp = bpp,
      PixelData = pixelData,
    };
  }
}
