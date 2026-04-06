using System;
using System.IO;
using FileFormat.Ecw.Codec;

namespace FileFormat.Ecw;

/// <summary>Reads Enhanced Compressed Wavelet files from bytes, streams, or file paths.</summary>
public static class EcwReader {

  public static EcwFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Ecw file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static EcwFile FromStream(Stream stream) {
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

  public static EcwFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static EcwFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < EcwFile.HeaderSize)
      throw new InvalidDataException("Data too small for a valid Ecw file.");

    // Try ECW codec decoding first (handles "ecw\0" magic)
    var fallbackWidth = _ParseFallbackWidth(data);
    var fallbackHeight = _ParseFallbackHeight(data);

    var (pixelData, width, height) = EcwDecoder.Decode(data, fallbackWidth, fallbackHeight);

    return new() {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }

  /// <summary>Parses width from the legacy header format for fallback.</summary>
  private static int _ParseFallbackWidth(byte[] data) {
    var width = data[0] | (data[1] << 8);
    if (width == 0)
      width = data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24);
    if (width <= 0 || width > 65535)
      width = 256;
    return width;
  }

  /// <summary>Parses height from the legacy header format for fallback.</summary>
  private static int _ParseFallbackHeight(byte[] data) {
    var height = data[4] | (data[5] << 8);
    if (height <= 0 || height > 65535)
      height = 256;
    return height;
  }
}
