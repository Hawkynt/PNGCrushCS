using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Aai;

/// <summary>Reads AAI (Dune HD) files from bytes, streams, or file paths.</summary>
public static class AaiReader {

  private const int _HEADER_SIZE = 8;

  public static AaiFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("AAI file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AaiFile FromStream(Stream stream) {
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

  public static AaiFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < _HEADER_SIZE)
      throw new InvalidDataException("Data too small for a valid AAI file.");

    var width = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[0..]);
    var height = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);

    if (width <= 0)
      throw new InvalidDataException($"Invalid AAI width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid AAI height: {height}.");

    var expectedPixelBytes = width * height * 4;
    if (data.Length - _HEADER_SIZE != expectedPixelBytes)
      throw new InvalidDataException($"Invalid AAI data size: expected {_HEADER_SIZE + expectedPixelBytes} bytes, got {data.Length}.");

    var pixelData = new byte[expectedPixelBytes];
    data.Slice(_HEADER_SIZE, expectedPixelBytes).CopyTo(pixelData.AsSpan(0));

    return new AaiFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };
    }

  public static AaiFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _HEADER_SIZE)
      throw new InvalidDataException("Data too small for a valid AAI file.");

    var width = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0));
    var height = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));

    if (width <= 0)
      throw new InvalidDataException($"Invalid AAI width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid AAI height: {height}.");

    var expectedPixelBytes = width * height * 4;
    if (data.Length - _HEADER_SIZE != expectedPixelBytes)
      throw new InvalidDataException($"Invalid AAI data size: expected {_HEADER_SIZE + expectedPixelBytes} bytes, got {data.Length}.");

    var pixelData = new byte[expectedPixelBytes];
    data.AsSpan(_HEADER_SIZE, expectedPixelBytes).CopyTo(pixelData.AsSpan(0));

    return new AaiFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };
  }
}
