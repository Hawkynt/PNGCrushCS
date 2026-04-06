using System;
using System.IO;

namespace FileFormat.FaxMan;

/// <summary>Reads FaxMan FMF files from bytes, streams, or file paths.</summary>
public static class FaxManReader {

  public static FaxManFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("FMF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FaxManFile FromStream(Stream stream) {
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

  public static FaxManFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static FaxManFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < FaxManFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid FMF file (need at least {FaxManFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != FaxManFile.Magic[0] || data[1] != FaxManFile.Magic[1])
      throw new InvalidDataException("Invalid FMF magic bytes.");

    var width = BitConverter.ToUInt16(data, 2);
    var height = BitConverter.ToUInt16(data, 4);
    var version = BitConverter.ToUInt16(data, 6);
    var flags = BitConverter.ToUInt16(data, 8);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid FMF dimensions: {width}x{height}.");

    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    if (data.Length < FaxManFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("FMF file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(FaxManFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Version = version,
      Flags = flags,
      PixelData = pixelData,
    };
  }
}
