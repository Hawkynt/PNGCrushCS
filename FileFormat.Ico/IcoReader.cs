using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Ico;

/// <summary>Reads ICO files from bytes, streams, or file paths.</summary>
public static class IcoReader {

  private static readonly byte[] _PngSignature = [0x89, 0x50, 0x4E, 0x47];

  public static IcoFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ICO file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IcoFile FromStream(Stream stream) {
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

  public static IcoFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return _Parse(data, IcoFileType.Icon);
  }

  internal static IcoFile _Parse(byte[] data, IcoFileType expectedType) {
    if (data.Length < IcoHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid ICO file.");

    var header = IcoHeader.ReadFrom(data);

    if (header.Reserved != 0)
      throw new InvalidDataException($"Invalid ICO reserved field: expected 0, got {header.Reserved}.");

    if (header.Type != (ushort)expectedType)
      throw new InvalidDataException($"Invalid ICO type field: expected {(ushort)expectedType}, got {header.Type}.");

    var count = header.Count;
    var directoryEnd = IcoHeader.StructSize + count * IcoDirectoryEntry.StructSize;
    if (data.Length < directoryEnd)
      throw new InvalidDataException("Data too small to contain all directory entries.");

    var images = new List<IcoImage>(count);
    for (var i = 0; i < count; ++i) {
      var entry = IcoDirectoryEntry.ReadFrom(data.AsSpan(IcoHeader.StructSize + i * IcoDirectoryEntry.StructSize));
      var width = entry.Width == 0 ? 256 : entry.Width;
      var height = entry.Height == 0 ? 256 : entry.Height;
      var bitCount = entry.Field5;
      var dataSize = entry.DataSize;
      var dataOffset = entry.DataOffset;

      if (dataSize < 0 || dataOffset < 0)
        throw new InvalidDataException($"Invalid directory entry {i}: negative size or offset.");

      if (dataOffset + dataSize > data.Length)
        throw new InvalidDataException($"Directory entry {i} references data beyond end of file.");

      var embeddedData = new byte[dataSize];
      data.AsSpan(dataOffset, dataSize).CopyTo(embeddedData.AsSpan(0));

      var isPng = _IsPngSignature(embeddedData);
      if (isPng) {
        _ParsePngDimensions(embeddedData, out var pngWidth, out var pngHeight, out var pngBpp);
        images.Add(new IcoImage {
          Width = pngWidth,
          Height = pngHeight,
          BitsPerPixel = pngBpp,
          Format = IcoImageFormat.Png,
          Data = embeddedData
        });
      } else {
        var dibBpp = _ReadDibBitsPerPixel(embeddedData, bitCount);
        images.Add(new IcoImage {
          Width = width,
          Height = height,
          BitsPerPixel = dibBpp,
          Format = IcoImageFormat.Bmp,
          Data = embeddedData
        });
      }
    }

    return new IcoFile { Images = images };
  }

  private static bool _IsPngSignature(byte[] data) {
    if (data.Length < 4)
      return false;

    for (var i = 0; i < 4; ++i)
      if (data[i] != _PngSignature[i])
        return false;

    return true;
  }

  private static void _ParsePngDimensions(byte[] pngData, out int width, out int height, out int bitsPerPixel) {
    // PNG: 8-byte signature, then IHDR chunk: 4-byte length, 4-byte type, 13-byte data
    // IHDR data: width(4) height(4) bitDepth(1) colorType(1) ...
    if (pngData.Length < 8 + 4 + 4 + 13)
      throw new InvalidDataException("Embedded PNG too small to contain IHDR.");

    width = BinaryPrimitives.ReadInt32BigEndian(pngData.AsSpan(16));
    height = BinaryPrimitives.ReadInt32BigEndian(pngData.AsSpan(20));
    var bitDepth = pngData[24];
    var colorType = pngData[25];

    var samplesPerPixel = colorType switch {
      0 => 1, // Grayscale
      2 => 3, // RGB
      3 => 1, // Palette (indexed)
      4 => 2, // Grayscale+Alpha
      6 => 4, // RGBA
      _ => 1
    };

    bitsPerPixel = bitDepth * samplesPerPixel;
  }

  private static int _ReadDibBitsPerPixel(byte[] dibData, int directoryBitCount) {
    // BMP DIB: BITMAPINFOHEADER starts at offset 0
    // Offset 14 in the DIB header = biBitCount (2 bytes LE)
    if (dibData.Length >= 16) {
      var dibBpp = BinaryPrimitives.ReadUInt16LittleEndian(dibData.AsSpan(14));
      if (dibBpp > 0)
        return dibBpp;
    }

    return directoryBitCount > 0 ? directoryBitCount : 32;
  }
}
