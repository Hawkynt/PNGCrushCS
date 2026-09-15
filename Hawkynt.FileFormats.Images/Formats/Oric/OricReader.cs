using System;
using System.IO;

namespace FileFormat.Oric;

/// <summary>Reads Oric hi-res screen dumps from bytes, streams, or file paths.</summary>
public static class OricReader {

  /// <summary>The exact file size of a valid Oric hi-res screen dump (40 bytes/row x 200 rows).</summary>
  private const int _EXPECTED_SIZE = 40 * 200;

  public static OricFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Oric file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static OricFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static OricFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != _EXPECTED_SIZE)
      throw new InvalidDataException($"Invalid Oric data size: expected exactly {_EXPECTED_SIZE} bytes, got {data.Length}.");

    var screenData = new byte[_EXPECTED_SIZE];
    data.Slice(0, _EXPECTED_SIZE).CopyTo(screenData);

    return new OricFile {
      ScreenData = screenData
    };
    }

  public static OricFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
