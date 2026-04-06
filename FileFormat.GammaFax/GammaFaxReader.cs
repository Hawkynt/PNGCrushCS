using System;
using System.IO;

namespace FileFormat.GammaFax;

/// <summary>Reads GammaFax GMF files from bytes, streams, or file paths.</summary>
public static class GammaFaxReader {

  public static GammaFaxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("GMF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static GammaFaxFile FromStream(Stream stream) {
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

  public static GammaFaxFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static GammaFaxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < GammaFaxFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid GMF file (need at least {GammaFaxFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != GammaFaxFile.Magic[0] || data[1] != GammaFaxFile.Magic[1])
      throw new InvalidDataException("Invalid GMF magic bytes.");

    var version = BitConverter.ToUInt16(data, 2);
    var width = BitConverter.ToUInt16(data, 4);
    var height = BitConverter.ToUInt16(data, 6);
    var compression = BitConverter.ToUInt16(data, 8);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid GMF dimensions: {width}x{height}.");

    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    if (data.Length < GammaFaxFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("GMF file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(GammaFaxFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Version = version,
      Compression = compression,
      PixelData = pixelData,
    };
  }
}
