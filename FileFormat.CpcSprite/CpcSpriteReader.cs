using System;
using System.IO;

namespace FileFormat.CpcSprite;

/// <summary>Reads CPC sprite data from bytes, streams, or file paths.</summary>
public static class CpcSpriteReader {

  public static CpcSpriteFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CPC sprite file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CpcSpriteFile FromStream(Stream stream) {
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

  public static CpcSpriteFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != CpcSpriteFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid CPC sprite data size: expected exactly {CpcSpriteFile.ExpectedFileSize} bytes, got {data.Length}.");

    var rawData = new byte[CpcSpriteFile.ExpectedFileSize];
    data.Slice(0, CpcSpriteFile.ExpectedFileSize).CopyTo(rawData);

    return new CpcSpriteFile { RawData = rawData };
    }

  public static CpcSpriteFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != CpcSpriteFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid CPC sprite data size: expected exactly {CpcSpriteFile.ExpectedFileSize} bytes, got {data.Length}.");

    var rawData = new byte[CpcSpriteFile.ExpectedFileSize];
    data.AsSpan(0, CpcSpriteFile.ExpectedFileSize).CopyTo(rawData);

    return new CpcSpriteFile { RawData = rawData };
  }
}
