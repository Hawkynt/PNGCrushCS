using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Ccitt;

namespace FileFormat.CImage;

/// <summary>Reads CImage document images from bytes, streams, or file paths.</summary>
public static class CImageReader {

  public static CImageFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("DSI file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CImageFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    return FromBytes(buffer.ToArray());
  }

  public static CImageFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static CImageFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < CImageFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid DSI file (need at least {CImageFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != CImageFile.Magic[0] || data[1] != CImageFile.Magic[1])
      throw new InvalidDataException("Invalid DSI magic bytes.");

    var horizontalResolution = BinaryPrimitives.ReadUInt16LittleEndian(data[CImageFile.HorizontalResolutionOffset..]);
    var verticalResolution = BinaryPrimitives.ReadUInt16LittleEndian(data[CImageFile.VerticalResolutionOffset..]);
    var isGroup4 = data[CImageFile.CompressionOffset] != 0;
    var width = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[CImageFile.WidthOffset..]);
    var height = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[(CImageFile.WidthOffset + 4)..]);

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid DSI dimensions: {width}x{height}.");

    var payload = data[CImageFile.HeaderSize..].ToArray();
    var pixelData = isGroup4
      ? CcittG4Decoder.Decode(payload, width, height)
      : _ReadUncompressed(payload, width, height)
      ;

    return new() {
      Width = width,
      Height = height,
      IsGroup4 = isGroup4,
      HorizontalResolution = horizontalResolution,
      VerticalResolution = verticalResolution,
      PixelData = pixelData,
    };
  }

  /// <summary>Reads packed scan lines, complementing them because a set bit is white on disk.</summary>
  private static byte[] _ReadUncompressed(byte[] payload, int width, int height) {
    var bytesPerRow = (width + 7) / 8;
    var needed = bytesPerRow * height;
    if (payload.Length < needed)
      throw new InvalidDataException("DSI file truncated: not enough pixel data.");

    var result = new byte[needed];
    for (var i = 0; i < needed; ++i)
      result[i] = (byte)~payload[i];

    return result;
  }

}
