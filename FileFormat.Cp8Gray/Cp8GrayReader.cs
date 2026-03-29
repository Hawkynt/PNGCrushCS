using System;
using System.IO;

namespace FileFormat.Cp8Gray;

/// <summary>Reads CP8 grayscale image files from bytes, streams, or file paths.</summary>
public static class Cp8GrayReader {

  private const int _MIN_FILE_SIZE = 1;

  public static Cp8GrayFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CP8 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Cp8GrayFile FromStream(Stream stream) {
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

  public static Cp8GrayFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_FILE_SIZE)
      throw new InvalidDataException($"Data too small for a valid CP8 file: expected at least {_MIN_FILE_SIZE} byte, got {data.Length}.");

    var side = (int)Math.Sqrt(data.Length);
    if (side * side != data.Length)
      throw new InvalidDataException($"CP8 file size {data.Length} is not a perfect square.");

    var pixelData = new byte[data.Length];
    data.AsSpan(0, data.Length).CopyTo(pixelData);

    return new Cp8GrayFile {
      Width = side,
      Height = side,
      PixelData = pixelData,
    };
  }
}
