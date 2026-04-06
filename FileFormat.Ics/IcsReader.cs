using System;
using System.IO;
using System.IO.Compression;

namespace FileFormat.Ics;

/// <summary>Reads ICS (Image Cytometry Standard) files from bytes, streams, or file paths.</summary>
public static class IcsReader {

  private const int _MIN_HEADER_SIZE = 15; // "ics_version\t2.0" length

  public static IcsFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ICS file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IcsFile FromStream(Stream stream) {
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

  public static IcsFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static IcsFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_HEADER_SIZE)
      throw new InvalidDataException("Data too small for a valid ICS file.");

    // Verify that the data starts with "ics_version\t" (tab-separated header)
    if (data[0] != (byte)'i' || data[1] != (byte)'c' || data[2] != (byte)'s' || data[3] != (byte)'_')
      throw new InvalidDataException("Invalid ICS header: missing 'ics_version' identifier.");

    var header = IcsHeaderParser.Parse(data);

    // Read pixel data
    var dataOffset = header.DataOffset;
    var remainingBytes = data.Length - dataOffset;

    if (remainingBytes <= 0)
      return new IcsFile {
        Version = header.Version,
        Width = header.Width,
        Height = header.Height,
        Channels = header.Channels,
        BitsPerSample = header.BitsPerSample,
        Compression = header.Compression,
        PixelData = [],
      };

    var rawData = new byte[remainingBytes];
    data.AsSpan(dataOffset, remainingBytes).CopyTo(rawData.AsSpan(0));

    var bytesPerSample = Math.Max(1, header.BitsPerSample / 8);
    var expectedPixelBytes = header.Width * header.Height * header.Channels * bytesPerSample;

    byte[] pixelData;
    if (header.Compression == IcsCompression.Gzip) {
      pixelData = _DecompressGzip(rawData, expectedPixelBytes);
    } else {
      pixelData = new byte[expectedPixelBytes];
      rawData.AsSpan(0, Math.Min(rawData.Length, expectedPixelBytes)).CopyTo(pixelData.AsSpan(0));
    }

    return new IcsFile {
      Version = header.Version,
      Width = header.Width,
      Height = header.Height,
      Channels = header.Channels,
      BitsPerSample = header.BitsPerSample,
      Compression = header.Compression,
      PixelData = pixelData,
    };
  }

  private static byte[] _DecompressGzip(byte[] compressedData, int expectedSize) {
    using var inputStream = new MemoryStream(compressedData);
    using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
    using var outputStream = new MemoryStream();
    gzipStream.CopyTo(outputStream);
    var decompressed = outputStream.ToArray();

    var result = new byte[expectedSize];
    decompressed.AsSpan(0, Math.Min(decompressed.Length, expectedSize)).CopyTo(result.AsSpan(0));
    return result;
  }
}
