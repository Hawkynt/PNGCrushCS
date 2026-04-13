using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.PalmPdb;

/// <summary>Reads Palm PDB image files from bytes, streams, or file paths.</summary>
public static class PalmPdbReader {

  /// <summary>PDB header is 78 bytes, plus at least one 8-byte record entry.</summary>
  private const int _HEADER_SIZE = 78;
  private const int _RECORD_ENTRY_SIZE = 8;
  private const int _MIN_FILE_SIZE = _HEADER_SIZE + _RECORD_ENTRY_SIZE;

  /// <summary>Expected type field: "Img " at offset 60.</summary>
  private static ReadOnlySpan<byte> _ExpectedType => "Img "u8;

  /// <summary>Image record header: 2 bytes width + 2 bytes height (all big-endian).</summary>
  private const int _IMAGE_RECORD_HEADER_SIZE = 4;

  public static PalmPdbFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PDB file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PalmPdbFile FromStream(Stream stream) {
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

  public static PalmPdbFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _MIN_FILE_SIZE)
      throw new InvalidDataException("Data too small for a valid PDB file.");

    // Read name (32 bytes, null-terminated ASCII)
    var nameEnd = 0;
    while (nameEnd < 32 && data[nameEnd] != 0)
      ++nameEnd;
    var name = Encoding.ASCII.GetString(data.Slice(0, nameEnd));

    // Validate type field at offset 60
    var typeSpan = data.Slice(60, 4);
    if (!typeSpan.SequenceEqual(_ExpectedType))
      throw new InvalidDataException($"Invalid PDB type: expected 'Img ' but got '{Encoding.ASCII.GetString(data.Slice(60, 4))}'.");

    // Record count at offset 76
    var recordCount = BinaryPrimitives.ReadUInt16BigEndian(data[76..]);
    if (recordCount < 1)
      throw new InvalidDataException("PDB file contains no records.");

    // First record entry at offset 78
    var recordOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(data[_HEADER_SIZE..]);

    if (recordOffset + _IMAGE_RECORD_HEADER_SIZE > data.Length)
      throw new InvalidDataException("Record offset points beyond file data.");

    // Image record: uint16 BE width, uint16 BE height, then RGB24 pixels
    var width = (int)BinaryPrimitives.ReadUInt16BigEndian(data[recordOffset..]);
    var height = (int)BinaryPrimitives.ReadUInt16BigEndian(data[(recordOffset + 2)..]);

    if (width <= 0)
      throw new InvalidDataException($"Invalid image width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid image height: {height}.");

    var expectedPixelBytes = width * height * 3;
    var pixelStart = recordOffset + _IMAGE_RECORD_HEADER_SIZE;

    if (pixelStart + expectedPixelBytes > data.Length)
      throw new InvalidDataException($"Insufficient pixel data: expected {expectedPixelBytes} bytes at offset {pixelStart}.");

    var pixelData = new byte[expectedPixelBytes];
    data.Slice(pixelStart, expectedPixelBytes).CopyTo(pixelData.AsSpan(0));

    return new PalmPdbFile {
      Width = width,
      Height = height,
      Name = name,
      PixelData = pixelData,
    };
  }

  public static PalmPdbFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
