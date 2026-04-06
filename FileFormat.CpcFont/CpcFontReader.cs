using System;
using System.IO;

namespace FileFormat.CpcFont;

/// <summary>Reads CPC font files from bytes, streams, or file paths.</summary>
public static class CpcFontReader {

  public static CpcFontFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CPC font file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CpcFontFile FromStream(Stream stream) {
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

  public static CpcFontFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static CpcFontFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != CpcFontFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid CPC font data size: expected exactly {CpcFontFile.ExpectedFileSize} bytes, got {data.Length}.");

    var rawData = new byte[CpcFontFile.ExpectedFileSize];
    data.AsSpan(0, CpcFontFile.ExpectedFileSize).CopyTo(rawData);

    return new CpcFontFile { RawData = rawData };
  }
}
