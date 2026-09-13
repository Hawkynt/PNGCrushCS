using System;
using System.IO;

namespace FileFormat.VidiChrome;

/// <summary>Reads VidiChrome screen dumps from bytes, streams, or file paths.</summary>
public static class VidiChromeReader {

  /// <summary>The exact file size of a valid VidiChrome screen dump (320 x 240 x 2 bytes).</summary>
  private const int _EXPECTED_SIZE = VidiChromeFile.ExpectedFileSize;

  public static VidiChromeFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("VidiChrome file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static VidiChromeFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static VidiChromeFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != _EXPECTED_SIZE)
      throw new InvalidDataException($"Invalid VidiChrome data size: expected exactly {_EXPECTED_SIZE} bytes, got {data.Length}.");

    var pixelData = new byte[_EXPECTED_SIZE];
    data.Slice(0, _EXPECTED_SIZE).CopyTo(pixelData);

    return new VidiChromeFile {
      PixelData = pixelData
    };
    }

  public static VidiChromeFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
