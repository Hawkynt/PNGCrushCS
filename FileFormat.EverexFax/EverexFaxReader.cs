using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.EverexFax;

/// <summary>Reads Everex Fax EFX files from bytes, streams, or file paths.</summary>
public static class EverexFaxReader {

  public static EverexFaxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("EFX file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static EverexFaxFile FromStream(Stream stream) {
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

  public static EverexFaxFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < EverexFaxFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid EFX file (need at least {EverexFaxFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != EverexFaxFile.Magic[0] || data[1] != EverexFaxFile.Magic[1] || data[2] != EverexFaxFile.Magic[2] || data[3] != EverexFaxFile.Magic[3])
      throw new InvalidDataException("Invalid EFX magic bytes.");

    var version = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
    var width = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
    var pages = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]);
    var compression = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
    var reserved = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid EFX dimensions: {width}x{height}.");

    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    if (data.Length < EverexFaxFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("EFX file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.Slice(EverexFaxFile.HeaderSize, pixelDataSize).CopyTo(pixelData);

    return new() {
      Width = width,
      Height = height,
      Version = version,
      Pages = pages,
      Compression = compression,
      Reserved = reserved,
      PixelData = pixelData,
    };
  }

  public static EverexFaxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
