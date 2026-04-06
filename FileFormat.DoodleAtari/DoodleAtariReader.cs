using System;
using System.IO;

namespace FileFormat.DoodleAtari;

/// <summary>Reads Atari ST Doodle monochrome images from bytes, streams, or file paths.</summary>
public static class DoodleAtariReader {

  public static DoodleAtariFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Doodle file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DoodleAtariFile FromStream(Stream stream) {
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

  public static DoodleAtariFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static DoodleAtariFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != DoodleAtariFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Doodle data size: expected exactly {DoodleAtariFile.ExpectedFileSize} bytes, got {data.Length}.");

    var pixelData = new byte[DoodleAtariFile.ExpectedFileSize];
    data.AsSpan(0, DoodleAtariFile.ExpectedFileSize).CopyTo(pixelData);

    return new DoodleAtariFile {
      PixelData = pixelData
    };
  }
}
