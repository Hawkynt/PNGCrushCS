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

  public static DoodleAtariFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static DoodleAtariFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != DoodleAtariFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Doodle data size: expected exactly {DoodleAtariFile.ExpectedFileSize} bytes, got {data.Length}.");

    var pixelData = new byte[DoodleAtariFile.ExpectedFileSize];
    data.Slice(0, DoodleAtariFile.ExpectedFileSize).CopyTo(pixelData);

    return new DoodleAtariFile {
      PixelData = pixelData
    };
    }

  public static DoodleAtariFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
