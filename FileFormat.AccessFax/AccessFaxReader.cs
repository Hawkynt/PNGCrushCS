using System;
using System.IO;

namespace FileFormat.AccessFax;

/// <summary>Reads AccessFax G4 files from bytes, streams, or file paths.</summary>
public static class AccessFaxReader {

  public static AccessFaxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("AccessFax file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AccessFaxFile FromStream(Stream stream) {
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

  public static AccessFaxFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < AccessFaxFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid AccessFax file (need at least {AccessFaxFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != AccessFaxFile.Magic[0] || data[1] != AccessFaxFile.Magic[1])
      throw new InvalidDataException("Invalid AccessFax magic bytes.");

    var header = AccessFaxHeader.ReadFrom(data);
    var width = header.Width;
    var height = header.Height;
    var flags = header.Flags;

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid AccessFax dimensions: {width}x{height}.");

    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    if (data.Length < AccessFaxFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("AccessFax file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.Slice(AccessFaxFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Flags = flags,
      PixelData = pixelData,
    };
  }

  public static AccessFaxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
