using System;
using System.IO;

namespace FileFormat.Ccitt;

/// <summary>Reads CCITT-compressed data from bytes, streams, or file paths.</summary>
public static class CcittReader {

  public static CcittFile FromFile(FileInfo file, int width, int height, CcittFormat format) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CCITT file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName), width, height, format);
  }

  public static CcittFile FromStream(Stream stream, int width, int height, CcittFormat format) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data, width, height, format);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray(), width, height, format);
  }

  public static CcittFile FromSpan(ReadOnlySpan<byte> data, int width, int height, CcittFormat format) {
    if (data.Length < 1)
      throw new InvalidDataException("Data too small for valid CCITT compressed data.");

    if (width <= 0)
      throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");

    if (height <= 0)
      throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");

    // Decoders require byte[], so materialize once
    var bytes = data.ToArray();

    var pixelData = format switch {
      CcittFormat.Group3_1D => CcittG3Decoder.Decode(bytes, width, height),
      CcittFormat.Group4 => CcittG4Decoder.Decode(bytes, width, height),
      _ => throw new NotSupportedException($"CCITT format {format} is not supported.")
    };

    return new CcittFile {
      Width = width,
      Height = height,
      Format = format,
      PixelData = pixelData
    };
  }

  public static CcittFile FromBytes(byte[] data, int width, int height, CcittFormat format) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data, width, height, format);
  }
}
