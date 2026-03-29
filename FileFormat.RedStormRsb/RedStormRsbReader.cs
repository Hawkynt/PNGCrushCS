using System;
using System.IO;

namespace FileFormat.RedStormRsb;

/// <summary>Reads Red Storm RSB files from bytes, streams, or file paths.</summary>
public static class RedStormRsbReader {

  public static RedStormRsbFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("RSB file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static RedStormRsbFile FromStream(Stream stream) {
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

  public static RedStormRsbFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < RedStormRsbFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid RSB file (need at least {RedStormRsbFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != RedStormRsbFile.Magic[0] || data[1] != RedStormRsbFile.Magic[1] || data[2] != RedStormRsbFile.Magic[2] || data[3] != RedStormRsbFile.Magic[3])
      throw new InvalidDataException("Invalid RSB magic bytes.");

    var version = BitConverter.ToUInt16(data, 4);
    var width = BitConverter.ToUInt16(data, 6);
    var height = BitConverter.ToUInt16(data, 8);
    var bpp = BitConverter.ToUInt16(data, 10);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid RSB dimensions: {width}x{height}.");

    var bytesPerPixel = bpp / 8;
    if (bytesPerPixel == 0)
      bytesPerPixel = 3;

    var pixelDataSize = width * height * bytesPerPixel;
    if (data.Length < RedStormRsbFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("RSB file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(RedStormRsbFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Version = version,
      Bpp = bpp,
      PixelData = pixelData,
    };
  }
}
