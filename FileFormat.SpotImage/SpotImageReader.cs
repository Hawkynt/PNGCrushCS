using System;
using System.IO;

namespace FileFormat.SpotImage;

/// <summary>Reads SPOT satellite imagery from bytes, streams, or file paths.</summary>
public static class SpotImageReader {

  public static SpotImageFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("SPOT image file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SpotImageFile FromStream(Stream stream) {
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

  public static SpotImageFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < SpotImageFile.HeaderSize)
      throw new InvalidDataException($"SPOT data too small: expected at least {SpotImageFile.HeaderSize} bytes, got {data.Length}.");

    // Validate magic
    for (var i = 0; i < SpotImageFile.Magic.Length; ++i)
      if (data[i] != SpotImageFile.Magic[i])
        throw new InvalidDataException("Invalid SPOT magic bytes.");

    var width = data[4] | (data[5] << 8);
    var height = data[6] | (data[7] << 8);
    var bpp = data[8] | (data[9] << 8);

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid SPOT dimensions: {width}x{height}.");

    var bytesPerPixel = bpp / 8;
    if (bytesPerPixel < 1)
      bytesPerPixel = 1;
    var pixelDataSize = width * height * bytesPerPixel;
    var pixelData = new byte[pixelDataSize];
    var available = Math.Min(data.Length - SpotImageFile.HeaderSize, pixelDataSize);
    if (available > 0)
      data.AsSpan(SpotImageFile.HeaderSize, available).CopyTo(pixelData.AsSpan(0));

    return new() { Width = width, Height = height, BitsPerPixel = bpp, PixelData = pixelData };
  }
}
