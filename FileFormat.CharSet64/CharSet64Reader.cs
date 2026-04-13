using System;
using System.IO;

namespace FileFormat.CharSet64;

/// <summary>Reads C64 character set files from bytes, streams, or file paths.</summary>
public static class CharSet64Reader {

  public static CharSet64File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("C64 character set file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CharSet64File FromStream(Stream stream) {
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

  public static CharSet64File FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < CharSet64File.ExpectedFileSize)
      throw new InvalidDataException($"C64 character set file too small (got {data.Length} bytes, expected {CharSet64File.ExpectedFileSize}).");

    var charData = new byte[CharSet64File.ExpectedFileSize];
    data.Slice(0, CharSet64File.ExpectedFileSize).CopyTo(charData.AsSpan(0));

    return new() { CharData = charData };
    }

  public static CharSet64File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < CharSet64File.ExpectedFileSize)
      throw new InvalidDataException($"C64 character set file too small (got {data.Length} bytes, expected {CharSet64File.ExpectedFileSize}).");

    var charData = new byte[CharSet64File.ExpectedFileSize];
    data.AsSpan(0, CharSet64File.ExpectedFileSize).CopyTo(charData.AsSpan(0));

    return new() { CharData = charData };
  }
}
