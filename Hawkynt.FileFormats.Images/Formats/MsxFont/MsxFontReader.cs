using System;
using System.IO;

namespace FileFormat.MsxFont;

/// <summary>Reads MSX font pattern tables from bytes, streams, or file paths.</summary>
public static class MsxFontReader {

  public static MsxFontFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MSX font file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MsxFontFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static MsxFontFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != MsxFontFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid MSX font data size: expected exactly {MsxFontFile.ExpectedFileSize} bytes, got {data.Length}.");

    var rawData = new byte[MsxFontFile.ExpectedFileSize];
    data.Slice(0, MsxFontFile.ExpectedFileSize).CopyTo(rawData);

    return new MsxFontFile { RawData = rawData };
    }

  public static MsxFontFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != MsxFontFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid MSX font data size: expected exactly {MsxFontFile.ExpectedFileSize} bytes, got {data.Length}.");

    var rawData = new byte[MsxFontFile.ExpectedFileSize];
    data.AsSpan(0, MsxFontFile.ExpectedFileSize).CopyTo(rawData);

    return new MsxFontFile { RawData = rawData };
  }
}
