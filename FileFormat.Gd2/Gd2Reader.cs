using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Gd2;

/// <summary>Reads GD2 files from bytes, streams, or file paths.</summary>
public static class Gd2Reader {

  public static Gd2File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("GD2 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Gd2File FromStream(Stream stream) {
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

  public static Gd2File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static Gd2File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < Gd2File.HeaderSize)
      throw new InvalidDataException($"Data too small for a valid GD2 file: expected at least {Gd2File.HeaderSize} bytes, got {data.Length}.");

    if (!data[..4].SequenceEqual(Gd2File.Signature))
      throw new InvalidDataException("Invalid GD2 signature.");

    var version = BinaryPrimitives.ReadUInt16BigEndian(data[4..]);
    var width = BinaryPrimitives.ReadUInt16BigEndian(data[6..]);
    var height = BinaryPrimitives.ReadUInt16BigEndian(data[8..]);
    var chunkSize = BinaryPrimitives.ReadUInt16BigEndian(data[10..]);
    var format = BinaryPrimitives.ReadUInt16BigEndian(data[12..]);
    var xChunkCount = BinaryPrimitives.ReadUInt16BigEndian(data[14..]);
    var yChunkCount = BinaryPrimitives.ReadUInt16BigEndian(data[16..]);

    if (width == 0 || height == 0)
      throw new InvalidDataException("GD2 image dimensions must be non-zero.");

    if (format != 1)
      throw new InvalidDataException($"Only raw truecolor GD2 format (1) is supported, got {format}.");

    var expectedPixelBytes = width * height * 4;
    var available = data.Length - Gd2File.HeaderSize;

    if (available < expectedPixelBytes)
      throw new InvalidDataException($"Not enough pixel data: expected {expectedPixelBytes} bytes, got {available}.");

    var pixelData = new byte[expectedPixelBytes];
    data.Slice(Gd2File.HeaderSize, expectedPixelBytes).CopyTo(pixelData.AsSpan(0));

    return new Gd2File {
      Width = width,
      Height = height,
      Version = version,
      ChunkSize = chunkSize,
      Format = format,
      PixelData = pixelData,
    };
  }
}
