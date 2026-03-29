using System;
using System.IO;

namespace FileFormat.ImagingFax;

/// <summary>Reads ImagingFax G3N files from bytes, streams, or file paths.</summary>
public static class ImagingFaxReader {

  public static ImagingFaxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("G3N file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ImagingFaxFile FromStream(Stream stream) {
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

  public static ImagingFaxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < ImagingFaxFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid G3N file (need at least {ImagingFaxFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != ImagingFaxFile.Magic[0] || data[1] != ImagingFaxFile.Magic[1] || data[2] != ImagingFaxFile.Magic[2] || data[3] != ImagingFaxFile.Magic[3])
      throw new InvalidDataException("Invalid G3N magic bytes.");

    var width = BitConverter.ToUInt16(data, 4);
    var height = BitConverter.ToUInt16(data, 6);
    var encoding = BitConverter.ToUInt16(data, 8);
    var flags = BitConverter.ToUInt16(data, 10);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid G3N dimensions: {width}x{height}.");

    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    if (data.Length < ImagingFaxFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("G3N file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(ImagingFaxFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Encoding = encoding,
      Flags = flags,
      PixelData = pixelData,
    };
  }
}
