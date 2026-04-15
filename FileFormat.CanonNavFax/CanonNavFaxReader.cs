using System;
using System.IO;

namespace FileFormat.CanonNavFax;

/// <summary>Reads Canon Navigator Fax CAN files from bytes, streams, or file paths.</summary>
public static class CanonNavFaxReader {

  public static CanonNavFaxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CAN file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CanonNavFaxFile FromStream(Stream stream) {
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

  public static CanonNavFaxFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < CanonNavFaxFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid CAN file (need at least {CanonNavFaxFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != CanonNavFaxFile.Magic[0] || data[1] != CanonNavFaxFile.Magic[1] || data[2] != CanonNavFaxFile.Magic[2] || data[3] != CanonNavFaxFile.Magic[3])
      throw new InvalidDataException("Invalid CAN magic bytes.");

    var header = CanonNavFaxHeader.ReadFrom(data);
    var width = header.Width;
    var height = header.Height;
    var resolution = header.Resolution;
    var encoding = header.Encoding;

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid CAN dimensions: {width}x{height}.");

    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    if (data.Length < CanonNavFaxFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("CAN file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.Slice(CanonNavFaxFile.HeaderSize, pixelDataSize).CopyTo(pixelData);

    return new() {
      Width = width,
      Height = height,
      Resolution = resolution,
      Encoding = encoding,
      PixelData = pixelData,
    };
  }

  public static CanonNavFaxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
