using System;
using System.IO;
using System.IO.Compression;

namespace FileFormat.Xyz;

/// <summary>Reads RPG Maker XYZ files from bytes, streams, or file paths.</summary>
public static class XyzReader {

  private const int _HEADER_SIZE = 8;
  private const int _PALETTE_SIZE = 768;

  public static XyzFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("XYZ file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static XyzFile FromStream(Stream stream) {
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

  public static XyzFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _HEADER_SIZE)
      throw new InvalidDataException("Data too small for a valid XYZ file.");

    // Validate magic "XYZ1"
    if (data[0] != (byte)'X' || data[1] != (byte)'Y' || data[2] != (byte)'Z' || data[3] != (byte)'1')
      throw new InvalidDataException("Invalid XYZ signature; expected 'XYZ1'.");

    // Read dimensions (uint16 LE)
    var width = (int)(data[4] | (data[5] << 8));
    var height = (int)(data[6] | (data[7] << 8));

    if (width == 0 || height == 0)
      throw new InvalidDataException("XYZ image dimensions must be non-zero.");

    // Decompress zlib data (skip 2-byte zlib header, use DeflateStream)
    var compressedSpan = data.AsSpan(_HEADER_SIZE);
    if (compressedSpan.Length < 2)
      throw new InvalidDataException("Data too small: missing compressed data.");

    // Skip zlib header (2 bytes) and decompress
    using var compressedStream = new MemoryStream(data, _HEADER_SIZE + 2, data.Length - _HEADER_SIZE - 2);
    using var deflateStream = new DeflateStream(compressedStream, CompressionMode.Decompress);
    using var decompressed = new MemoryStream();
    deflateStream.CopyTo(decompressed);
    var raw = decompressed.ToArray();

    var expectedSize = _PALETTE_SIZE + width * height;
    if (raw.Length < expectedSize)
      throw new InvalidDataException($"Decompressed data too small: expected {expectedSize} bytes, got {raw.Length}.");

    // First 768 bytes = palette
    var palette = new byte[_PALETTE_SIZE];
    raw.AsSpan(0, _PALETTE_SIZE).CopyTo(palette.AsSpan(0));

    // Remainder = pixel data
    var pixelData = new byte[width * height];
    raw.AsSpan(_PALETTE_SIZE, pixelData.Length).CopyTo(pixelData.AsSpan(0));

    return new XyzFile {
      Width = width,
      Height = height,
      Palette = palette,
      PixelData = pixelData,
    };
  }
}
