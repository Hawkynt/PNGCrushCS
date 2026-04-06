using System;
using System.IO;

namespace FileFormat.AvhrrImage;

/// <summary>Reads AVHRR satellite image files from bytes, streams, or file paths.</summary>
public static class AvhrrImageReader {

  public static AvhrrImageFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("SST file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AvhrrImageFile FromStream(Stream stream) {
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

  public static AvhrrImageFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static AvhrrImageFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < AvhrrImageFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid SST file (need at least {AvhrrImageFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != AvhrrImageFile.Magic[0] || data[1] != AvhrrImageFile.Magic[1] || data[2] != AvhrrImageFile.Magic[2] || data[3] != AvhrrImageFile.Magic[3])
      throw new InvalidDataException("Invalid SST magic bytes.");

    var width = BitConverter.ToUInt16(data, 4);
    var height = BitConverter.ToUInt16(data, 6);
    var bands = BitConverter.ToUInt16(data, 8);
    var dataType = BitConverter.ToUInt16(data, 10);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid SST dimensions: {width}x{height}.");

    var pixelDataSize = width * height;
    if (data.Length < AvhrrImageFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("SST file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(AvhrrImageFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Bands = bands,
      DataType = dataType,
      PixelData = pixelData,
    };
  }
}
