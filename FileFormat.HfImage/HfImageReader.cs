using System;
using System.IO;

namespace FileFormat.HfImage;

/// <summary>Reads HF height field image files from bytes, streams, or file paths.</summary>
public static class HfImageReader {

  public static HfImageFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("HF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HfImageFile FromStream(Stream stream) {
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

  public static HfImageFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < HfImageFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid HF file (need at least {HfImageFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != HfImageFile.Magic[0] || data[1] != HfImageFile.Magic[1])
      throw new InvalidDataException("Invalid HF magic bytes.");

    var width = BitConverter.ToUInt16(data, 2);
    var height = BitConverter.ToUInt16(data, 4);
    var dataType = BitConverter.ToUInt16(data, 6);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid HF dimensions: {width}x{height}.");

    var pixelDataSize = width * height;
    if (data.Length < HfImageFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("HF file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(HfImageFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      DataType = dataType,
      PixelData = pixelData,
    };
  }
}
