using System;
using System.IO;

namespace FileFormat.Hrz;

/// <summary>Reads HRZ files from spans, bytes, streams, or file paths.</summary>
public static class HrzReader {

  private const int _EXPECTED_SIZE = 256 * 240 * 3;

  public static HrzFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != _EXPECTED_SIZE)
      throw new InvalidDataException($"Invalid HRZ data size: expected exactly {_EXPECTED_SIZE} bytes, got {data.Length}.");

    return new HrzFile { PixelData = data[.._EXPECTED_SIZE].ToArray() };
  }

  public static HrzFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static HrzFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("HRZ file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HrzFile FromStream(Stream stream) {
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
}
