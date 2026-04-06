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

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static GbrFile FromStream(Stream stream) {
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

  public static GbrFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static GbrFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_HEADER_SIZE)
      throw new InvalidDataException($"Data too small for a valid GBR file: expected at least {_MIN_HEADER_SIZE} bytes, got {data.Length}.");

    var span = data.AsSpan();
    var headerSize = (int)BinaryPrimitives.ReadUInt32BigEndian(span);

    if (headerSize < _MIN_HEADER_SIZE)
      throw new InvalidDataException($"Invalid GBR header size: {headerSize}.");

    if (data.Length < headerSize)
      throw new InvalidDataException($"Data too small for declared header size: expected at least {headerSize} bytes, got {data.Length}.");

    var version = (int)BinaryPrimitives.ReadUInt32BigEndian(span[4..]);
    if (version != 2)
      throw new InvalidDataException($"Unsupported GBR version: {version} (expected 2).");

    var width = (int)BinaryPrimitives.ReadUInt32BigEndian(span[8..]);
    var height = (int)BinaryPrimitives.ReadUInt32BigEndian(span[12..]);
    var bytesPerPixel = (int)BinaryPrimitives.ReadUInt32BigEndian(span[16..]);

    if (span[20] != _MAGIC[0] || span[21] != _MAGIC[1] || span[22] != _MAGIC[2] || span[23] != _MAGIC[3])
      throw new InvalidDataException("Invalid GBR magic: expected 'GIMP' at offset 20.");

    var spacing = (int)BinaryPrimitives.ReadUInt32BigEndian(span[24..]);

    if (width <= 0)
      throw new InvalidDataException($"Invalid GBR width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid GBR height: {height}.");
    if (bytesPerPixel is not (1 or 4))
      throw new InvalidDataException($"Invalid GBR bytes per pixel: {bytesPerPixel} (expected 1 or 4).");

    var name = string.Empty;
    var nameLength = headerSize - _MIN_HEADER_SIZE;
    if (nameLength > 0) {
      var nameSpan = span.Slice(_MIN_HEADER_SIZE, nameLength);
      var nullIndex = nameSpan.IndexOf((byte)0);
      if (nullIndex >= 0)
        nameSpan = nameSpan[..nullIndex];

      name = Encoding.UTF8.GetString(nameSpan);
    }

    var expectedPixelBytes = width * height * bytesPerPixel;
    if (data.Length - headerSize < expectedPixelBytes)
      throw new InvalidDataException($"Data too small for pixel data: expected {headerSize + expectedPixelBytes} bytes, got {data.Length}.");

    var pixelData = new byte[expectedPixelBytes];
    data.AsSpan(headerSize, expectedPixelBytes).CopyTo(pixelData.AsSpan(0));

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
