using System;
using System.IO;

namespace FileFormat.NokiaPictureMessage;

/// <summary>Reads Nokia Picture Message (.npm) files from bytes, streams, or file paths.</summary>
public static class NokiaPictureMessageReader {

  public static NokiaPictureMessageFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("NPM file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static NokiaPictureMessageFile FromStream(Stream stream) {
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

  public static NokiaPictureMessageFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < NokiaPictureMessageHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid NPM file.");

    var header = NokiaPictureMessageHeader.ReadFrom(data);

    if (header.Type != 0x00)
      throw new InvalidDataException($"Invalid NPM type field: expected 0x00, got 0x{header.Type:X2}.");

    if (header.Depth != 0x01)
      throw new InvalidDataException($"Invalid NPM depth: expected 0x01, got 0x{header.Depth:X2}.");

    var width = (int)header.Width;
    var height = (int)header.Height;

    if (width == 0)
      throw new InvalidDataException("Invalid NPM width: must be greater than zero.");
    if (height == 0)
      throw new InvalidDataException("Invalid NPM height: must be greater than zero.");

    var bytesPerRow = (width + 7) / 8;
    var expectedPixelBytes = bytesPerRow * height;

    if (data.Length < NokiaPictureMessageHeader.StructSize + expectedPixelBytes)
      throw new InvalidDataException($"Data too small for pixel data: expected {NokiaPictureMessageHeader.StructSize + expectedPixelBytes} bytes, got {data.Length}.");

    var pixelData = new byte[expectedPixelBytes];
    data.Slice(NokiaPictureMessageHeader.StructSize, expectedPixelBytes).CopyTo(pixelData.AsSpan(0));

    return new NokiaPictureMessageFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };
    }

  public static NokiaPictureMessageFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < NokiaPictureMessageHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid NPM file.");

    var header = NokiaPictureMessageHeader.ReadFrom(data.AsSpan());

    if (header.Type != 0x00)
      throw new InvalidDataException($"Invalid NPM type field: expected 0x00, got 0x{header.Type:X2}.");

    if (header.Depth != 0x01)
      throw new InvalidDataException($"Invalid NPM depth: expected 0x01, got 0x{header.Depth:X2}.");

    var width = (int)header.Width;
    var height = (int)header.Height;

    if (width == 0)
      throw new InvalidDataException("Invalid NPM width: must be greater than zero.");
    if (height == 0)
      throw new InvalidDataException("Invalid NPM height: must be greater than zero.");

    var bytesPerRow = (width + 7) / 8;
    var expectedPixelBytes = bytesPerRow * height;

    if (data.Length < NokiaPictureMessageHeader.StructSize + expectedPixelBytes)
      throw new InvalidDataException($"Data too small for pixel data: expected {NokiaPictureMessageHeader.StructSize + expectedPixelBytes} bytes, got {data.Length}.");

    var pixelData = new byte[expectedPixelBytes];
    data.AsSpan(NokiaPictureMessageHeader.StructSize, expectedPixelBytes).CopyTo(pixelData.AsSpan(0));

    return new NokiaPictureMessageFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };
  }
}
