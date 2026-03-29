using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.Pat;

/// <summary>Reads GIMP Pattern (PAT) files from bytes, streams, or file paths.</summary>
public static class PatReader {

  /// <summary>Minimum header size: 24 bytes fixed fields + at least 1 byte for null-terminated name.</summary>
  private const int _MIN_HEADER_SIZE = 24;

  /// <summary>Magic bytes "GPAT" at offset 20.</summary>
  private static readonly byte[] _MAGIC = "GPAT"u8.ToArray();

  public static PatFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PAT file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PatFile FromStream(Stream stream) {
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

  public static PatFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_HEADER_SIZE)
      throw new InvalidDataException($"Data too small for a valid PAT file: expected at least {_MIN_HEADER_SIZE} bytes, got {data.Length}.");

    var headerSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0));
    if (headerSize < _MIN_HEADER_SIZE)
      throw new InvalidDataException($"Invalid PAT header size: {headerSize}.");

    var version = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4));
    if (version != 1)
      throw new InvalidDataException($"Unsupported PAT version: {version}.");

    var width = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8));
    var height = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12));
    var bytesPerPixel = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(16));

    if (width <= 0)
      throw new InvalidDataException($"Invalid PAT width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid PAT height: {height}.");
    if (bytesPerPixel is < 1 or > 4)
      throw new InvalidDataException($"Invalid PAT bytes per pixel: {bytesPerPixel}.");

    // Validate magic at offset 20
    if (data[20] != _MAGIC[0] || data[21] != _MAGIC[1] || data[22] != _MAGIC[2] || data[23] != _MAGIC[3])
      throw new InvalidDataException("Invalid PAT magic: expected 'GPAT'.");

    if (data.Length < headerSize)
      throw new InvalidDataException($"Data too small: header says {headerSize} bytes but only got {data.Length}.");

    // Read null-terminated UTF-8 name from offset 24 to headerSize-1
    var nameLength = 0;
    for (var i = 24; i < headerSize; ++i) {
      if (data[i] == 0)
        break;
      ++nameLength;
    }

    var name = nameLength > 0 ? Encoding.UTF8.GetString(data, 24, nameLength) : string.Empty;

    var expectedPixelBytes = width * height * bytesPerPixel;
    if (data.Length - headerSize < expectedPixelBytes)
      throw new InvalidDataException($"Insufficient pixel data: expected {expectedPixelBytes} bytes after header, got {data.Length - headerSize}.");

    var pixelData = new byte[expectedPixelBytes];
    data.AsSpan(headerSize, expectedPixelBytes).CopyTo(pixelData.AsSpan(0));

    return new PatFile {
      Width = width,
      Height = height,
      BytesPerPixel = bytesPerPixel,
      Name = name,
      PixelData = pixelData
    };
  }
}
