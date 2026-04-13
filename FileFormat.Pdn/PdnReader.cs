using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace FileFormat.Pdn;

/// <summary>Reads PDN files from bytes, streams, or file paths.</summary>
public static class PdnReader {

  /// <summary>Header size: 4 (magic) + 2 (version) + 2 (reserved) + 4 (width) + 4 (height) = 16 bytes.</summary>
  internal const int HEADER_SIZE = 16;

  private static readonly byte[] _MAGIC = "PDN3"u8.ToArray();

  public static PdnFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PDN file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PdnFile FromStream(Stream stream) {
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

  public static PdnFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < HEADER_SIZE)
      throw new InvalidDataException($"Data too small for a valid PDN file: expected at least {HEADER_SIZE} bytes, got {data.Length}.");

    if (data[0] != _MAGIC[0] || data[1] != _MAGIC[1] || data[2] != _MAGIC[2] || data[3] != _MAGIC[3])
      throw new InvalidDataException($"Invalid PDN magic: expected 'PDN3', got '{Encoding.ASCII.GetString(data.Slice(0, 4))}'.");

    var version = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
    // reserved at offset 6
    var width = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
    var height = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);

    if (width <= 0)
      throw new InvalidDataException($"Invalid PDN width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid PDN height: {height}.");

    var expectedPixelBytes = width * height * 4;
    byte[] pixelData;

    if (data.Length > HEADER_SIZE) {
      // GZipStream requires a Stream backed by byte[]
      var bytes = data.ToArray();
      using var compressedStream = new MemoryStream(bytes, HEADER_SIZE, bytes.Length - HEADER_SIZE);
      using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
      using var decompressed = new MemoryStream();
      gzipStream.CopyTo(decompressed);
      pixelData = decompressed.ToArray();

      if (pixelData.Length != expectedPixelBytes)
        throw new InvalidDataException($"Decompressed pixel data size mismatch: expected {expectedPixelBytes} bytes, got {pixelData.Length}.");
    } else {
      pixelData = new byte[expectedPixelBytes];
    }

    return new PdnFile {
      Version = version,
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  
  }

  public static PdnFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
