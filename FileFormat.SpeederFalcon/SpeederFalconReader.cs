using System;
using System.IO;

namespace FileFormat.SpeederFalcon;

/// <summary>Reads Speeder Falcon screen dumps from bytes, streams, or file paths.</summary>
public static class SpeederFalconReader {

  /// <summary>The exact file size of a valid Speeder Falcon screen dump (320 x 240 x 2 bytes).</summary>
  private const int _EXPECTED_SIZE = SpeederFalconFile.ExpectedFileSize;

  public static SpeederFalconFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Speeder Falcon file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SpeederFalconFile FromStream(Stream stream) {
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

  public static SpeederFalconFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != _EXPECTED_SIZE)
      throw new InvalidDataException($"Invalid Speeder Falcon data size: expected exactly {_EXPECTED_SIZE} bytes, got {data.Length}.");

    var pixelData = new byte[_EXPECTED_SIZE];
    data.AsSpan(0, _EXPECTED_SIZE).CopyTo(pixelData);

    return new SpeederFalconFile {
      PixelData = pixelData
    };
  }
}
