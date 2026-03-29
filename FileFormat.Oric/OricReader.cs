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

  public static OricFile FromStream(Stream stream) {
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

  public static OricFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != _EXPECTED_SIZE)
      throw new InvalidDataException($"Invalid Oric data size: expected exactly {_EXPECTED_SIZE} bytes, got {data.Length}.");

    var screenData = new byte[_EXPECTED_SIZE];
    data.AsSpan(0, _EXPECTED_SIZE).CopyTo(screenData);

    return new OricFile {
      ScreenData = screenData
    };
  }
}
