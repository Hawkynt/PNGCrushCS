using System;
using System.IO;

namespace FileFormat.HayesJtfax;

/// <summary>Reads Hayes JT Fax files from bytes, streams, or file paths.</summary>
public static class HayesJtfaxReader {

  public static HayesJtfaxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("JTF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HayesJtfaxFile FromStream(Stream stream) {
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

  public static HayesJtfaxFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static HayesJtfaxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < HayesJtfaxFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid JTF file (need at least {HayesJtfaxFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != HayesJtfaxFile.Magic[0] || data[1] != HayesJtfaxFile.Magic[1])
      throw new InvalidDataException("Invalid JTF magic bytes.");

    var version = BitConverter.ToUInt16(data, 2);
    var width = BitConverter.ToUInt16(data, 4);
    var height = BitConverter.ToUInt16(data, 6);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid JTF dimensions: {width}x{height}.");

    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    if (data.Length < HayesJtfaxFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("JTF file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(HayesJtfaxFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Version = version,
      PixelData = pixelData,
    };
  }
}
