using System;
using System.IO;

namespace FileFormat.AtariImageManager;

/// <summary>Reads Atari Image Manager pictures from bytes, streams, or file paths.</summary>
public static class AtariImageManagerReader {

  public static AtariImageManagerFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariImageManagerFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static AtariImageManagerFile FromSpan(ReadOnlySpan<byte> data) {
    var size = data.Length switch {
      AtariImageManagerFile.SmallSize * AtariImageManagerFile.SmallSize => AtariImageManagerFile.SmallSize,
      AtariImageManagerFile.LargeSize * AtariImageManagerFile.LargeSize => AtariImageManagerFile.LargeSize,
      _ => throw new InvalidDataException($"Not an Atari Image Manager picture: {data.Length} bytes."),
    };

    return new() { PixelData = data.ToArray(), Size = size };
  }

  public static AtariImageManagerFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
