using System;
using System.IO;

namespace FileFormat.Otb;

/// <summary>Reads OTB (Nokia Over-The-Air Bitmap) files from bytes, streams, or file paths.</summary>
public static class OtbReader {

  public static OtbFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("OTB file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static OtbFile FromStream(Stream stream) {
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

  public static OtbFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < OtbHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid OTB file.");

    var header = OtbHeader.ReadFrom(data);

    if (header.InfoField != 0x00)
      throw new InvalidDataException($"Invalid OTB info field: expected 0x00, got 0x{header.InfoField:X2}.");

    if (header.Depth != 0x01)
      throw new InvalidDataException($"Invalid OTB depth: expected 0x01, got 0x{header.Depth:X2}.");

    var width = header.Width;
    var height = header.Height;

    if (width == 0)
      throw new InvalidDataException("Invalid OTB width: must be greater than zero.");
    if (height == 0)
      throw new InvalidDataException("Invalid OTB height: must be greater than zero.");

    var bytesPerRow = (width + 7) / 8;
    var expectedPixelBytes = bytesPerRow * height;

    if (data.Length < OtbHeader.StructSize + expectedPixelBytes)
      throw new InvalidDataException($"Data too small for pixel data: expected {OtbHeader.StructSize + expectedPixelBytes} bytes, got {data.Length}.");

    var pixelData = new byte[expectedPixelBytes];
    data.Slice(OtbHeader.StructSize, expectedPixelBytes).CopyTo(pixelData.AsSpan(0));

    return new OtbFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };
    }

  public static OtbFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < OtbHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid OTB file.");

    var header = OtbHeader.ReadFrom(data.AsSpan());

    if (header.InfoField != 0x00)
      throw new InvalidDataException($"Invalid OTB info field: expected 0x00, got 0x{header.InfoField:X2}.");

    if (header.Depth != 0x01)
      throw new InvalidDataException($"Invalid OTB depth: expected 0x01, got 0x{header.Depth:X2}.");

    var width = header.Width;
    var height = header.Height;

    if (width == 0)
      throw new InvalidDataException("Invalid OTB width: must be greater than zero.");
    if (height == 0)
      throw new InvalidDataException("Invalid OTB height: must be greater than zero.");

    var bytesPerRow = (width + 7) / 8;
    var expectedPixelBytes = bytesPerRow * height;

    if (data.Length < OtbHeader.StructSize + expectedPixelBytes)
      throw new InvalidDataException($"Data too small for pixel data: expected {OtbHeader.StructSize + expectedPixelBytes} bytes, got {data.Length}.");

    var pixelData = new byte[expectedPixelBytes];
    data.AsSpan(OtbHeader.StructSize, expectedPixelBytes).CopyTo(pixelData.AsSpan(0));

    return new OtbFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };
  }
}
