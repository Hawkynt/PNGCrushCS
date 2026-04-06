using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Avs;

/// <summary>Reads AVS files from bytes, streams, or file paths.</summary>
public static class AvsReader {

  private const int _HEADER_SIZE = 8;

  public static AvsFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("AVS file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AvsFile FromStream(Stream stream) {
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

  public static AvsFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static AvsFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _HEADER_SIZE)
      throw new InvalidDataException("Data too small for a valid AVS file.");

    var width = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0));
    var height = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4));

    if (width <= 0)
      throw new InvalidDataException($"Invalid AVS width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid AVS height: {height}.");

    var expectedPixelBytes = width * height * 4;
    if (data.Length - _HEADER_SIZE != expectedPixelBytes)
      throw new InvalidDataException($"Invalid AVS data size: expected {_HEADER_SIZE + expectedPixelBytes} bytes, got {data.Length}.");

    var pixelData = new byte[expectedPixelBytes];
    data.AsSpan(_HEADER_SIZE, expectedPixelBytes).CopyTo(pixelData.AsSpan(0));

    return new AvsFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };
  }
}
