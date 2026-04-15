using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.Gbr;

/// <summary>Reads GIMP Brush (GBR) version 2 files from bytes, streams, or file paths.</summary>
public static class GbrReader {

  /// <summary>Minimum header size: 28 bytes (header_size + version + width + height + bpp + magic + spacing).</summary>
  private const int _MIN_HEADER_SIZE = 28;

  /// <summary>Expected magic bytes "GIMP" at offset 20.</summary>
  private static readonly byte[] _MAGIC = "GIMP"u8.ToArray();

  public static GbrFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("GBR file not found.", file.FullName);

    return FromSpan(File.ReadAllBytes(file.FullName));
  }

  public static GbrFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  public static GbrFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static GbrFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _MIN_HEADER_SIZE)
      throw new InvalidDataException($"Data too small for a valid GBR file: expected at least {_MIN_HEADER_SIZE} bytes, got {data.Length}.");

    var headerSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data);

    if (headerSize < _MIN_HEADER_SIZE)
      throw new InvalidDataException($"Invalid GBR header size: {headerSize}.");

    if (data.Length < headerSize)
      throw new InvalidDataException($"Data too small for declared header size: expected at least {headerSize} bytes, got {data.Length}.");

    var header = GbrHeader.ReadFrom(data);
    var version = (int)header.Version;
    if (version != 2)
      throw new InvalidDataException($"Unsupported GBR version: {version} (expected 2).");

    var width = (int)header.Width;
    var height = (int)header.Height;
    var bytesPerPixel = (int)header.BytesPerPixel;

    if (data[20] != _MAGIC[0] || data[21] != _MAGIC[1] || data[22] != _MAGIC[2] || data[23] != _MAGIC[3])
      throw new InvalidDataException("Invalid GBR magic: expected 'GIMP' at offset 20.");

    var spacing = (int)BinaryPrimitives.ReadUInt32BigEndian(data[24..]);

    if (width <= 0)
      throw new InvalidDataException($"Invalid GBR width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid GBR height: {height}.");
    if (bytesPerPixel is not (1 or 4))
      throw new InvalidDataException($"Invalid GBR bytes per pixel: {bytesPerPixel} (expected 1 or 4).");

    var name = string.Empty;
    var nameLength = headerSize - _MIN_HEADER_SIZE;
    if (nameLength > 0) {
      var nameSpan = data.Slice(_MIN_HEADER_SIZE, nameLength);
      var nullIndex = nameSpan.IndexOf((byte)0);
      if (nullIndex >= 0)
        nameSpan = nameSpan[..nullIndex];

      name = Encoding.UTF8.GetString(nameSpan);
    }

    var expectedPixelBytes = width * height * bytesPerPixel;
    if (data.Length - headerSize < expectedPixelBytes)
      throw new InvalidDataException($"Data too small for pixel data: expected {headerSize + expectedPixelBytes} bytes, got {data.Length}.");

    var pixelData = new byte[expectedPixelBytes];
    data.Slice(headerSize, expectedPixelBytes).CopyTo(pixelData.AsSpan(0));

    return new GbrFile {
      Width = width,
      Height = height,
      BytesPerPixel = bytesPerPixel,
      Spacing = spacing,
      Name = name,
      PixelData = pixelData,
    };
  }
}
