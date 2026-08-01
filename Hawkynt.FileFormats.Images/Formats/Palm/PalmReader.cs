using System;
using System.IO;

namespace FileFormat.Palm;

/// <summary>Reads Palm OS Bitmap files from bytes, streams, or file paths.</summary>
public static class PalmReader {

  public static PalmFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Palm bitmap file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PalmFile FromStream(Stream stream) {
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

  public static PalmFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < PalmHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Palm bitmap file.");

    var span = data;
    var header = PalmHeader.ReadFrom(span);

    var width = (int)header.Width;
    var height = (int)header.Height;
    var bitsPerPixel = (int)header.BitsPerPixel;
    var bytesPerRow = (int)header.BytesPerRow;
    var transparentIndex = header.TransparentIndex;
    var compression = (PalmCompression)header.CompressionType;

    if (width <= 0)
      throw new InvalidDataException($"Invalid Palm bitmap width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid Palm bitmap height: {height}.");

    var offset = PalmHeader.StructSize;

    // Read optional color table
    byte[]? palette = null;
    if (_HasUsableColorTable(header, data, offset, bytesPerRow * height)) {
      var numEntries = (data[offset] << 8) | data[offset + 1];
      offset += 2;

      palette = new byte[numEntries * 3];
      for (var i = 0; i < numEntries; ++i) {
        // skip index byte
        offset += 1;
        palette[i * 3] = data[offset++];     // R
        palette[i * 3 + 1] = data[offset++]; // G
        palette[i * 3 + 2] = data[offset++]; // B
      }
    }

    // Read pixel data
    var expectedSize = bytesPerRow * height;
    byte[] pixelData;

    if (header.IsCompressed && compression == PalmCompression.Rle) {
      var remaining = data.Length - offset;
      pixelData = PalmRleCompressor.Decompress(data.Slice(offset, remaining), bytesPerRow, height);
    } else {
      if (offset + expectedSize > data.Length)
        throw new InvalidDataException($"Data too small for pixel data: expected {offset + expectedSize} bytes, got {data.Length}.");

      pixelData = new byte[expectedSize];
      data.Slice(offset, expectedSize).CopyTo(pixelData.AsSpan(0));
    }

    return new PalmFile {
      Width = width,
      Height = height,
      BitsPerPixel = bitsPerPixel,
      Compression = compression,
      TransparentIndex = transparentIndex,
      PixelData = pixelData,
      Palette = palette
    };
    }

  /// <summary>Whether a colour table really follows the header, rather than merely being announced.</summary>
  /// <remarks>
  /// The flag cannot be taken at its word. ImageMagick sets it on every bitmap it writes and then
  /// puts the pixels straight after the header, so believing it reads the first two rows as a count
  /// — which comes out in the tens of thousands and takes the rest of the file with it. A table has
  /// at most 256 entries and has to leave room for the picture, so both are checked before use.
  /// </remarks>
  private static bool _HasUsableColorTable(PalmHeader header, ReadOnlySpan<byte> data, int offset, int pixelBytes) {
    if (!header.HasColorTable || offset + 2 > data.Length)
      return false;

    var entries = (data[offset] << 8) | data[offset + 1];

    return entries is > 0 and <= 256 && offset + 2 + entries * 4 + pixelBytes <= data.Length;
  }

  public static PalmFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < PalmHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Palm bitmap file.");

    var span = data.AsSpan();
    var header = PalmHeader.ReadFrom(span);

    var width = (int)header.Width;
    var height = (int)header.Height;
    var bitsPerPixel = (int)header.BitsPerPixel;
    var bytesPerRow = (int)header.BytesPerRow;
    var transparentIndex = header.TransparentIndex;
    var compression = (PalmCompression)header.CompressionType;

    if (width <= 0)
      throw new InvalidDataException($"Invalid Palm bitmap width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid Palm bitmap height: {height}.");

    var offset = PalmHeader.StructSize;

    // Read optional color table
    byte[]? palette = null;
    if (_HasUsableColorTable(header, data, offset, bytesPerRow * height)) {
      var numEntries = (data[offset] << 8) | data[offset + 1];
      offset += 2;

      palette = new byte[numEntries * 3];
      for (var i = 0; i < numEntries; ++i) {
        // skip index byte
        offset += 1;
        palette[i * 3] = data[offset++];     // R
        palette[i * 3 + 1] = data[offset++]; // G
        palette[i * 3 + 2] = data[offset++]; // B
      }
    }

    // Read pixel data
    var expectedSize = bytesPerRow * height;
    byte[] pixelData;

    if (header.IsCompressed && compression == PalmCompression.Rle) {
      var remaining = data.Length - offset;
      pixelData = PalmRleCompressor.Decompress(data.AsSpan(offset, remaining), bytesPerRow, height);
    } else {
      if (offset + expectedSize > data.Length)
        throw new InvalidDataException($"Data too small for pixel data: expected {offset + expectedSize} bytes, got {data.Length}.");

      pixelData = new byte[expectedSize];
      data.AsSpan(offset, expectedSize).CopyTo(pixelData.AsSpan(0));
    }

    return new PalmFile {
      Width = width,
      Height = height,
      BitsPerPixel = bitsPerPixel,
      Compression = compression,
      TransparentIndex = transparentIndex,
      PixelData = pixelData,
      Palette = palette
    };
  }
}
