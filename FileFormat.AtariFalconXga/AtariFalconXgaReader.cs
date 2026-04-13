using System;
using System.IO;

namespace FileFormat.AtariFalconXga;

/// <summary>Reads Atari Falcon XGA 16-bit true color files from bytes, streams, or file paths.</summary>
public static class AtariFalconXgaReader {

  public static AtariFalconXgaFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Atari Falcon XGA file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariFalconXgaFile FromStream(Stream stream) {
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

  public static AtariFalconXgaFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < AtariFalconXgaHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Atari Falcon XGA file.");

    var header = AtariFalconXgaHeader.ReadFrom(data);
    var width = (int)header.Width;
    var height = (int)header.Height;

    if (width == 0 || height == 0)
      throw new InvalidDataException("Atari Falcon XGA image dimensions must be non-zero.");

    var expectedPixelBytes = width * height * 2;
    var available = data.Length - AtariFalconXgaHeader.StructSize;
    if (available < expectedPixelBytes)
      throw new InvalidDataException($"Data too small for pixel data: expected {AtariFalconXgaHeader.StructSize + expectedPixelBytes} bytes, got {data.Length}.");

    var pixelData = new byte[expectedPixelBytes];
    data.Slice(AtariFalconXgaHeader.StructSize, expectedPixelBytes).CopyTo(pixelData);

    return new AtariFalconXgaFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
    }

  public static AtariFalconXgaFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < AtariFalconXgaHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Atari Falcon XGA file.");

    var header = AtariFalconXgaHeader.ReadFrom(data.AsSpan());
    var width = (int)header.Width;
    var height = (int)header.Height;

    if (width == 0 || height == 0)
      throw new InvalidDataException("Atari Falcon XGA image dimensions must be non-zero.");

    var expectedPixelBytes = width * height * 2;
    var available = data.Length - AtariFalconXgaHeader.StructSize;
    if (available < expectedPixelBytes)
      throw new InvalidDataException($"Data too small for pixel data: expected {AtariFalconXgaHeader.StructSize + expectedPixelBytes} bytes, got {data.Length}.");

    var pixelData = new byte[expectedPixelBytes];
    data.AsSpan(AtariFalconXgaHeader.StructSize, expectedPixelBytes).CopyTo(pixelData);

    return new AtariFalconXgaFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }
}
