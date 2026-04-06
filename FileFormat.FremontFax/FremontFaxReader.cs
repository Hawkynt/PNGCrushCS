using System;
using System.IO;

namespace FileFormat.FremontFax;

/// <summary>Reads Fremont Fax F96 files from bytes, streams, or file paths.</summary>
public static class FremontFaxReader {

  public static FremontFaxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("F96 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FremontFaxFile FromStream(Stream stream) {
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

  public static FremontFaxFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static FremontFaxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < FremontFaxFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid F96 file (need at least {FremontFaxFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != FremontFaxFile.Magic[0] || data[1] != FremontFaxFile.Magic[1] || data[2] != FremontFaxFile.Magic[2] || data[3] != FremontFaxFile.Magic[3])
      throw new InvalidDataException("Invalid F96 magic bytes.");

    var width = BitConverter.ToUInt16(data, 4);
    var height = BitConverter.ToUInt16(data, 6);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid F96 dimensions: {width}x{height}.");

    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    if (data.Length < FremontFaxFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("F96 file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(FremontFaxFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }
}
