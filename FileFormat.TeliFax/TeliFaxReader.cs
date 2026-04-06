using System;
using System.IO;

namespace FileFormat.TeliFax;

/// <summary>Reads TeliFax MH files from bytes, streams, or file paths.</summary>
public static class TeliFaxReader {

  public static TeliFaxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MH file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static TeliFaxFile FromStream(Stream stream) {
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

  public static TeliFaxFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static TeliFaxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < TeliFaxFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid MH file (need at least {TeliFaxFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != TeliFaxFile.Magic[0] || data[1] != TeliFaxFile.Magic[1])
      throw new InvalidDataException("Invalid MH magic bytes.");

    var version = BitConverter.ToUInt16(data, 2);
    var width = BitConverter.ToUInt16(data, 4);
    var height = BitConverter.ToUInt16(data, 6);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid MH dimensions: {width}x{height}.");

    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    if (data.Length < TeliFaxFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("MH file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(TeliFaxFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Version = version,
      PixelData = pixelData,
    };
  }
}
