using System;
using System.IO;

namespace FileFormat.HighResAtari;

/// <summary>Reads Atari Hi-Res Paint images from bytes, streams, or file paths.</summary>
public static class HighResAtariReader {

  public static HighResAtariFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Atari Hi-Res Paint file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HighResAtariFile FromStream(Stream stream) {
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

  public static HighResAtariFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != HighResAtariFile.FileSize)
      throw new InvalidDataException($"Invalid Atari Hi-Res Paint data size: expected exactly {HighResAtariFile.FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[HighResAtariFile.FileSize];
    data.AsSpan(0, HighResAtariFile.FileSize).CopyTo(pixelData);

    return new HighResAtariFile { PixelData = pixelData };
  }
}
