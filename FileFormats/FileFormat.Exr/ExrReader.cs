using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.Exr;

/// <summary>Reads OpenEXR files from bytes, streams, or file paths.</summary>
public static class ExrReader {

  public static ExrFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("EXR file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ExrFile FromStream(Stream stream) {
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

  public static ExrFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < ExrMagicHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid EXR file.");

    var bytes = data.ToArray();
    var offset = 0;

    // Magic header (8 bytes)
    var header = ExrMagicHeader.ReadFrom(data);
    if (header.Magic != ExrMagicHeader.ExpectedMagic)
      throw new InvalidDataException("Invalid EXR magic number.");

    offset += ExrMagicHeader.StructSize;

    // Parse attributes
    var attributes = new List<ExrAttribute>();
    List<ExrChannel>? channels = null;
    var compression = ExrCompression.None;
    var lineOrder = ExrLineOrder.IncreasingY;
    var dataWindowXMin = 0;
    var dataWindowYMin = 0;
    var dataWindowXMax = 0;
    var dataWindowYMax = 0;

    while (offset < bytes.Length) {
      // Read attribute name (null-terminated)
      var name = _ReadNullTerminatedString(bytes, ref offset);
      if (name.Length == 0)
        break; // End of header

      // Read attribute type name (null-terminated)
      var typeName = _ReadNullTerminatedString(bytes, ref offset);

      // Read attribute value size (int32 LE)
      if (offset + 4 > bytes.Length)
        throw new InvalidDataException("Unexpected end of data reading attribute size.");

      var valueSize = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
      offset += 4;

      if (offset + valueSize > bytes.Length)
        throw new InvalidDataException("Unexpected end of data reading attribute value.");

      var value = new byte[valueSize];
      data.Slice(offset, valueSize).CopyTo(value);

      // Parse well-known attributes
      switch (name) {
        case "channels":
          channels = _ParseChannelList(value);
          break;
        case "compression":
          if (valueSize >= 1)
            compression = (ExrCompression)value[0];
          break;
        case "lineOrder":
          if (valueSize >= 1)
            lineOrder = (ExrLineOrder)value[0];
          break;
        case "dataWindow":
          if (valueSize >= 16) {
            dataWindowXMin = BinaryPrimitives.ReadInt32LittleEndian(value.AsSpan(0));
            dataWindowYMin = BinaryPrimitives.ReadInt32LittleEndian(value.AsSpan(4));
            dataWindowXMax = BinaryPrimitives.ReadInt32LittleEndian(value.AsSpan(8));
            dataWindowYMax = BinaryPrimitives.ReadInt32LittleEndian(value.AsSpan(12));
          }
          break;
      }

      attributes.Add(new ExrAttribute { Name = name, TypeName = typeName, Value = value });
      offset += valueSize;
    }

    var width = dataWindowXMax - dataWindowXMin + 1;
    var height = dataWindowYMax - dataWindowYMin + 1;
    channels ??= [];

    // Read scan line offset table
    var scanlineBlockCount = height; // For None compression, one scanline per block
    if (offset + scanlineBlockCount * 8 > data.Length)
      throw new InvalidDataException("Unexpected end of data reading offset table.");

    var offsets = new long[scanlineBlockCount];
    for (var i = 0; i < scanlineBlockCount; ++i) {
      offsets[i] = BinaryPrimitives.ReadInt64LittleEndian(data[offset..]);
      offset += 8;
    }

    // Calculate bytes per scanline
    var bytesPerScanline = 0;
    foreach (var ch in channels) {
      var bytesPerPixel = ch.PixelType switch {
        ExrPixelType.UInt => 4,
        ExrPixelType.Half => 2,
        ExrPixelType.Float => 4,
        _ => 4
      };
      bytesPerScanline += bytesPerPixel * width;
    }

    // Read pixel data blocks
    var pixelData = new byte[bytesPerScanline * height];
    for (var i = 0; i < scanlineBlockCount; ++i) {
      var blockOffset = (int)offsets[i];
      if (blockOffset + 8 > data.Length)
        throw new InvalidDataException("Scanline block offset out of range.");

      // Y coordinate (int32) + pixel data size (int32)
      var blockDataSize = BinaryPrimitives.ReadInt32LittleEndian(data[(blockOffset + 4)..]);
      var blockDataStart = blockOffset + 8;

      if (blockDataStart + blockDataSize > data.Length)
        throw new InvalidDataException("Scanline block data extends beyond file.");

      var dstOffset = i * bytesPerScanline;
      var copyLen = Math.Min(blockDataSize, bytesPerScanline);
      data.Slice(blockDataStart, copyLen).CopyTo(pixelData.AsSpan(dstOffset));
    }

    return new ExrFile {
      Width = width,
      Height = height,
      Compression = compression,
      LineOrder = lineOrder,
      Channels = channels,
      PixelData = pixelData,
      Attributes = attributes
    };
  }

  public static ExrFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static string _ReadNullTerminatedString(byte[] data, ref int offset) {
    var start = offset;
    while (offset < data.Length && data[offset] != 0)
      ++offset;

    var result = Encoding.ASCII.GetString(data.AsSpan(start, offset - start));
    if (offset < data.Length)
      ++offset; // Skip null terminator

    return result;
  }

  private static List<ExrChannel> _ParseChannelList(byte[] data) {
    var channels = new List<ExrChannel>();
    var offset = 0;

    while (offset < data.Length) {
      // Channel name (null-terminated)
      var nameStart = offset;
      while (offset < data.Length && data[offset] != 0)
        ++offset;

      if (offset == nameStart)
        break; // Empty name = end of channel list

      var name = Encoding.ASCII.GetString(data.AsSpan(nameStart, offset - nameStart));
      ++offset; // Skip null terminator

      if (offset + 16 > data.Length)
        break;

      // pixel type (int32), pLinear (uint8), reserved (3 bytes), xSampling (int32), ySampling (int32)
      var pixelType = (ExrPixelType)BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));
      offset += 4;
      offset += 4; // pLinear (1) + reserved (3)
      var xSampling = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));
      offset += 4;
      var ySampling = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));
      offset += 4;

      channels.Add(new ExrChannel { Name = name, PixelType = pixelType, XSampling = xSampling, YSampling = ySampling });
    }

    return channels;
  }
}
