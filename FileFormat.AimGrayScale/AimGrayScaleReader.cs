using System;
using System.IO;

namespace FileFormat.AimGrayScale;

/// <summary>Reads AIM grayscale image files from bytes, streams, or file paths.</summary>
public static class AimGrayScaleReader {

  public static AimGrayScaleFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("AIM file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AimGrayScaleFile FromStream(Stream stream) {
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

  public static AimGrayScaleFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < AimGrayScaleFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid AIM file (need at least {AimGrayScaleFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != AimGrayScaleFile.Magic[0] || data[1] != AimGrayScaleFile.Magic[1] || data[2] != AimGrayScaleFile.Magic[2] || data[3] != AimGrayScaleFile.Magic[3])
      throw new InvalidDataException("Invalid AIM magic bytes.");

    var width = BitConverter.ToUInt16(data, 4);
    var height = BitConverter.ToUInt16(data, 6);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid AIM dimensions: {width}x{height}.");

    var pixelDataSize = data.Length - AimGrayScaleFile.HeaderSize;
    var pixelData = new byte[pixelDataSize];
    if (pixelDataSize > 0)
      data.AsSpan(AimGrayScaleFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }
}
