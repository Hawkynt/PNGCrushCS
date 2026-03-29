using System;
using System.IO;

namespace FileFormat.ZeissBivas;

/// <summary>Reads Zeiss BIVAS microscopy images from bytes, streams, or file paths.</summary>
public static class ZeissBivasReader {

  public static ZeissBivasFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Zeiss BIVAS file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ZeissBivasFile FromStream(Stream stream) {
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

  public static ZeissBivasFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < ZeissBivasFile.HeaderSize)
      throw new InvalidDataException($"Zeiss BIVAS data too small: expected at least {ZeissBivasFile.HeaderSize} bytes, got {data.Length}.");

    var width = (int)_ReadUInt32LE(data, 0);
    var height = (int)_ReadUInt32LE(data, 4);
    var bpp = (int)_ReadUInt32LE(data, 8);

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid dimensions: {width}x{height}.");

    var bytesPerPixel = Math.Max(1, bpp / 8);
    var pixelDataSize = width * height * bytesPerPixel;
    var pixelData = new byte[pixelDataSize];
    var available = Math.Min(data.Length - ZeissBivasFile.HeaderSize, pixelDataSize);
    if (available > 0)
      data.AsSpan(ZeissBivasFile.HeaderSize, available).CopyTo(pixelData.AsSpan(0));

    return new() { Width = width, Height = height, BitsPerPixel = bpp, PixelData = pixelData };
  }

  private static uint _ReadUInt32LE(byte[] data, int offset) =>
    (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
}
