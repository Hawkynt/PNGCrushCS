using System;
using System.IO;

namespace FileFormat.ZeissLsm;

/// <summary>Reads Zeiss LSM images from bytes, streams, or file paths (simplified TIFF-like parsing).</summary>
public static class ZeissLsmReader {

  // TIFF tag IDs
  private const ushort TagImageWidth = 256;
  private const ushort TagImageLength = 257;
  private const ushort TagStripOffsets = 273;
  private const ushort TagStripByteCounts = 279;
  private const ushort TagSamplesPerPixel = 277;

  public static ZeissLsmFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Zeiss LSM file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ZeissLsmFile FromStream(Stream stream) {
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

  public static ZeissLsmFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static ZeissLsmFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < 8)
      throw new InvalidDataException($"Zeiss LSM data too small: expected at least 8 bytes, got {data.Length}.");

    // Verify TIFF header
    var byteOrder = (ushort)(data[0] | (data[1] << 8));
    if (byteOrder != ZeissLsmFile.ByteOrderLE)
      throw new InvalidDataException("Only little-endian LSM files are supported.");

    var magic = _ReadUInt16LE(data, 2);
    if (magic != ZeissLsmFile.TiffMagicLE)
      throw new InvalidDataException($"Invalid TIFF magic: expected {ZeissLsmFile.TiffMagicLE}, got {magic}.");

    var ifdOffset = _ReadUInt32LE(data, 4);
    if (ifdOffset + 2 > data.Length)
      throw new InvalidDataException("IFD offset out of range.");

    var entryCount = _ReadUInt16LE(data, (int)ifdOffset);
    var width = 0;
    var height = 0;
    var channels = 1;
    var stripOffset = 0u;
    var stripByteCount = 0u;

    for (var i = 0; i < entryCount; ++i) {
      var entryPos = (int)ifdOffset + 2 + i * 12;
      if (entryPos + 12 > data.Length)
        break;

      var tag = _ReadUInt16LE(data, entryPos);
      var value = _ReadUInt32LE(data, entryPos + 8);

      switch (tag) {
        case TagImageWidth:
          width = (int)value;
          break;
        case TagImageLength:
          height = (int)value;
          break;
        case TagStripOffsets:
          stripOffset = value;
          break;
        case TagStripByteCounts:
          stripByteCount = value;
          break;
        case TagSamplesPerPixel:
          channels = (int)value;
          break;
      }
    }

    if (width == 0 || height == 0)
      throw new InvalidDataException("Could not find ImageWidth/ImageLength tags.");

    // Extract pixel data from strip
    var pixelDataSize = stripByteCount > 0 ? (int)stripByteCount : width * height * channels;
    var pixelData = new byte[pixelDataSize];
    if (stripOffset > 0 && stripOffset + pixelDataSize <= data.Length)
      data.AsSpan((int)stripOffset, pixelDataSize).CopyTo(pixelData);

    return new() { Width = width, Height = height, Channels = channels, PixelData = pixelData };
  }

  private static ushort _ReadUInt16LE(byte[] data, int offset) =>
    (ushort)(data[offset] | (data[offset + 1] << 8));

  private static uint _ReadUInt32LE(byte[] data, int offset) =>
    (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
}
