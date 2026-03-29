using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.Gaf;

/// <summary>Reads GAF (Total Annihilation) texture archive files from bytes, streams, or file paths.</summary>
public static class GafReader {

  /// <summary>GAF magic: version 0x00010100 stored as LE bytes 0x00, 0x01, 0x01, 0x00.</summary>
  private const uint _MAGIC = 0x00010100;

  /// <summary>Minimum file size: 12-byte header + at least 1 entry pointer (4 bytes).</summary>
  private const int _MIN_FILE_SIZE = 16;

  public static GafFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("GAF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static GafFile FromStream(Stream stream) {
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

  public static GafFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_FILE_SIZE)
      throw new InvalidDataException("Data too small for a valid GAF file.");

    var span = data.AsSpan();

    var version = BinaryPrimitives.ReadUInt32LittleEndian(span);
    if (version != _MAGIC)
      throw new InvalidDataException($"Invalid GAF magic: expected 0x{_MAGIC:X8}, got 0x{version:X8}.");

    var entryCount = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
    // bytes 8..11 are reserved/unknown

    if (entryCount == 0)
      throw new InvalidDataException("GAF file contains no entries.");

    // Read first entry pointer
    var entryPointersOffset = 12;
    if (data.Length < entryPointersOffset + 4)
      throw new InvalidDataException("Data too small for entry pointer table.");

    var entryOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[entryPointersOffset..]);

    // Parse entry header (40 bytes): frame_count(u16), unknown(u16), unknown(u32), name(32 bytes)
    const int entryHeaderSize = 40;
    if (data.Length < entryOffset + entryHeaderSize)
      throw new InvalidDataException("Data too small for entry header.");

    var entrySpan = span[entryOffset..];
    var frameCount = BinaryPrimitives.ReadUInt16LittleEndian(entrySpan);
    // bytes 2..3: unknown u16
    // bytes 4..7: unknown u32

    var nameBytes = entrySpan.Slice(8, 32);
    var nameLength = nameBytes.IndexOf((byte)0);
    var name = nameLength < 0
      ? Encoding.ASCII.GetString(nameBytes)
      : Encoding.ASCII.GetString(nameBytes[..nameLength]);

    if (frameCount == 0)
      throw new InvalidDataException("GAF entry contains no frames.");

    // Frame pointer table follows the entry header
    var framePointersOffset = entryOffset + entryHeaderSize;
    if (data.Length < framePointersOffset + 4)
      throw new InvalidDataException("Data too small for frame pointer table.");

    var frameOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[framePointersOffset..]);

    // Parse frame header (20 bytes)
    const int frameHeaderSize = 20;
    if (data.Length < frameOffset + frameHeaderSize)
      throw new InvalidDataException("Data too small for frame header.");

    var frameSpan = span[frameOffset..];
    var width = BinaryPrimitives.ReadUInt16LittleEndian(frameSpan);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(frameSpan[2..]);
    var xOffset = BinaryPrimitives.ReadInt16LittleEndian(frameSpan[4..]);
    var yOffset = BinaryPrimitives.ReadInt16LittleEndian(frameSpan[6..]);
    var transparencyIndex = frameSpan[8];
    var compressed = frameSpan[9];
    var subframeCount = BinaryPrimitives.ReadUInt16LittleEndian(frameSpan[10..]);
    var dataOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(frameSpan[12..]);
    // bytes 16..19: unknown u32

    if (width == 0 || height == 0)
      throw new InvalidDataException("GAF frame dimensions must be greater than zero.");

    // If subframeCount > 0, this is a composite frame; follow the first subframe
    if (subframeCount > 0) {
      // dataOffset points to a subframe pointer table
      if (data.Length < dataOffset + 4)
        throw new InvalidDataException("Data too small for subframe pointer table.");

      var subFrameOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[dataOffset..]);
      if (data.Length < subFrameOffset + frameHeaderSize)
        throw new InvalidDataException("Data too small for subframe header.");

      var subFrameSpan = span[subFrameOffset..];
      width = BinaryPrimitives.ReadUInt16LittleEndian(subFrameSpan);
      height = BinaryPrimitives.ReadUInt16LittleEndian(subFrameSpan[2..]);
      xOffset = BinaryPrimitives.ReadInt16LittleEndian(subFrameSpan[4..]);
      yOffset = BinaryPrimitives.ReadInt16LittleEndian(subFrameSpan[6..]);
      transparencyIndex = subFrameSpan[8];
      compressed = subFrameSpan[9];
      dataOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(subFrameSpan[12..]);
    }

    var pixelCount = width * height;
    byte[] pixelData;

    if (compressed == 0)
      pixelData = _ReadUncompressed(data, dataOffset, pixelCount);
    else
      pixelData = _ReadRle(data, dataOffset, width, height, transparencyIndex);

    return new GafFile {
      Width = width,
      Height = height,
      Name = name,
      TransparencyIndex = transparencyIndex,
      XOffset = xOffset,
      YOffset = yOffset,
      PixelData = pixelData,
    };
  }

  private static byte[] _ReadUncompressed(byte[] data, int offset, int count) {
    if (data.Length < offset + count)
      throw new InvalidDataException("Data too small for uncompressed pixel data.");

    var pixels = new byte[count];
    data.AsSpan(offset, count).CopyTo(pixels.AsSpan(0));
    return pixels;
  }

  private static byte[] _ReadRle(byte[] data, int offset, int width, int height, byte transparencyIndex) {
    var pixels = new byte[width * height];
    var pos = offset;

    for (var y = 0; y < height; ++y) {
      if (pos + 2 > data.Length)
        throw new InvalidDataException("Unexpected end of RLE data reading scanline length.");

      var lineBytes = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos));
      pos += 2;

      var lineEnd = pos + lineBytes;
      var x = 0;

      while (pos < lineEnd && x < width) {
        if (pos >= data.Length)
          throw new InvalidDataException("Unexpected end of RLE data.");

        var control = data[pos];
        ++pos;

        if ((control & 0x80) != 0) {
          // Transparent run: (control & 0x7F) pixels of transparencyIndex
          var count = control & 0x7F;
          for (var i = 0; i < count && x < width; ++i)
            pixels[y * width + x++] = transparencyIndex;
        } else {
          // Literal run: control pixels of literal data
          var count = control;
          for (var i = 0; i < count && x < width; ++i) {
            if (pos >= data.Length)
              throw new InvalidDataException("Unexpected end of RLE literal data.");

            pixels[y * width + x++] = data[pos];
            ++pos;
          }
        }
      }

      // Fill remaining columns with transparency
      while (x < width)
        pixels[y * width + x++] = transparencyIndex;

      pos = lineEnd;
    }

    return pixels;
  }
}
