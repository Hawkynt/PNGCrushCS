using System;
using System.IO;

namespace FileFormat.MsxSprite;

/// <summary>Reads MSX sprite pattern tables from bytes, streams, or file paths.</summary>
public static class MsxSpriteReader {

  public static MsxSpriteFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MSX sprite file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MsxSpriteFile FromStream(Stream stream) {
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

  public static MsxSpriteFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static MsxSpriteFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != MsxSpriteFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid MSX sprite data size: expected exactly {MsxSpriteFile.ExpectedFileSize} bytes, got {data.Length}.");

    var rawData = new byte[MsxSpriteFile.ExpectedFileSize];
    data.AsSpan(0, MsxSpriteFile.ExpectedFileSize).CopyTo(rawData);

    return new MsxSpriteFile { RawData = rawData };
  }
}
