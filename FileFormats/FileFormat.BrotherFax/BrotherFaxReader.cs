using System;
using System.IO;

namespace FileFormat.BrotherFax;

/// <summary>Reads Brother fax UNI files from bytes, streams, or file paths.</summary>
public static class BrotherFaxReader {

  public static BrotherFaxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("UNI file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static BrotherFaxFile FromStream(Stream stream) {
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

  public static BrotherFaxFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < BrotherFaxFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid UNI file (need at least {BrotherFaxFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != BrotherFaxFile.Magic[0] || data[1] != BrotherFaxFile.Magic[1])
      throw new InvalidDataException("Invalid UNI magic bytes.");

    var header = BrotherFaxHeader.ReadFrom(data);
    var version = header.Version;
    var width = header.Width;
    var height = header.Height;
    var compression = header.Compression;

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid UNI dimensions: {width}x{height}.");

    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    if (data.Length < BrotherFaxFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("UNI file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.Slice(BrotherFaxFile.HeaderSize, pixelDataSize).CopyTo(pixelData);

    return new() {
      Width = width,
      Height = height,
      Version = version,
      Compression = compression,
      PixelData = pixelData,
    };
  }

  public static BrotherFaxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
