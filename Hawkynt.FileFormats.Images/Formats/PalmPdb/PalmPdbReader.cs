using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.PalmPdb;

/// <summary>Reads Palm Image Viewer pictures out of a PDB database.</summary>
public static class PalmPdbReader {

  /// <summary>The PDB database header, before the record list.</summary>
  private const int _DATABASE_HEADER_SIZE = 78;

  /// <summary>One entry per record: a four-byte offset, then attributes and a three-byte id.</summary>
  private const int _RECORD_ENTRY_SIZE = 8;

  /// <summary>The descriptor at the head of an image record, before its pixels.</summary>
  internal const int ImageHeaderSize = 58;

  /// <summary>Only two bits a pixel has been seen in the wild, and it is what a writer produces.</summary>
  internal const int BitsPerPixel = 2;

  internal static readonly byte[] ExpectedType = "vIMG"u8.ToArray();
  internal static readonly byte[] ExpectedCreator = "View"u8.ToArray();

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

  public static PalmPdbFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data.AsSpan());
  }

  public static PalmPdbFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _DATABASE_HEADER_SIZE + _RECORD_ENTRY_SIZE)
      throw new InvalidDataException("Data too small for a valid PDB file.");

    var nameEnd = 0;
    while (nameEnd < 32 && data[nameEnd] != 0)
      ++nameEnd;
    var name = Encoding.ASCII.GetString(data[..nameEnd]);

    if (!data.Slice(60, 4).SequenceEqual(ExpectedType))
      throw new InvalidDataException(
        $"Not a Palm Image Viewer database: expected type 'vIMG' at offset 60 but found '{Encoding.ASCII.GetString(data.Slice(60, 4))}'.");

    var recordCount = BinaryPrimitives.ReadUInt16BigEndian(data[76..]);
    if (recordCount < 1)
      throw new InvalidDataException("PDB file contains no records.");

    var recordOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(data[_DATABASE_HEADER_SIZE..]);
    if (recordOffset + ImageHeaderSize > data.Length)
      throw new InvalidDataException("Record offset points beyond file data.");

    var record = data[recordOffset..];

    // The record's own descriptor: 32 bytes of name, then how it is stored and how big it is.
    var version = record[32];
    var type = record[33];
    var width = BinaryPrimitives.ReadInt16BigEndian(record[54..]);
    var height = BinaryPrimitives.ReadInt16BigEndian(record[56..]);

    if (width <= 0)
      throw new InvalidDataException($"Invalid image width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid image height: {height}.");

    // Type 0 is the four-grey picture every writer produces, and the only one there is anything to
    // check against. Naming the others here would be guessing at what their data looks like.
    if (type != 0)
      throw new NotSupportedException($"Palm Image Viewer type {type} is not supported; only type 0 (four greys) is.");

    var stride = ((width * BitsPerPixel) + 7) / 8;
    var expected = stride * height;
    var payload = record[ImageHeaderSize..];

    // Version 1 says the pixels are PackBits compressed; version 0 that they are not.
    var pixelData = (version & 1) != 0
      ? _DecompressPackBits(payload, expected)
      : _Copy(payload, expected);

    return new PalmPdbFile {
      Width = width,
      Height = height,
      Name = name,
      PixelData = pixelData,
    };
  }

  private static byte[] _Copy(ReadOnlySpan<byte> source, int expected) {
    var result = new byte[expected];
    source[..Math.Min(expected, source.Length)].CopyTo(result);
    return result;
  }

  /// <summary>Expands the PackBits runs a compressed Image Viewer record stores its rows as.</summary>
  private static byte[] _DecompressPackBits(ReadOnlySpan<byte> source, int expected) {
    var result = new byte[expected];
    var outIndex = 0;
    var inIndex = 0;

    while (inIndex < source.Length && outIndex < expected) {
      var header = (sbyte)source[inIndex++];

      if (header >= 0) {
        var count = Math.Min(header + 1, Math.Min(expected - outIndex, source.Length - inIndex));
        source.Slice(inIndex, count).CopyTo(result.AsSpan(outIndex));
        outIndex += count;
        inIndex += count;
        continue;
      }

      if (header == -128 || inIndex >= source.Length)
        continue; // -128 is a no-op in PackBits

      var run = Math.Min(1 - header, expected - outIndex);
      result.AsSpan(outIndex, run).Fill(source[inIndex++]);
      outIndex += run;
    }

    return result;
  }
}
