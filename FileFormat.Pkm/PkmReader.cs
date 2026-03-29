using System;
using System.IO;

namespace FileFormat.Pkm;

/// <summary>Reads PKM files from bytes, streams, or file paths.</summary>
public static class PkmReader {

  public static PkmFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PKM file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PkmFile FromStream(Stream stream) {
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

  public static PkmFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < PkmHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid PKM file.");

    var span = data.AsSpan();
    var header = PkmHeader.ReadFrom(span);

    if (header.Magic1 != (byte)'P' || header.Magic2 != (byte)'K' || header.Magic3 != (byte)'M' || header.Magic4 != (byte)' ')
      throw new InvalidDataException("Invalid PKM magic bytes.");

    var version1 = (char)header.Version1;
    var version2 = (char)header.Version2;
    var version = $"{version1}{version2}";

    if (version is not ("10" or "20"))
      throw new InvalidDataException($"Unsupported PKM version: {version}.");

    var compressedData = new byte[data.Length - PkmHeader.StructSize];
    data.AsSpan(PkmHeader.StructSize, compressedData.Length).CopyTo(compressedData.AsSpan(0));

    return new PkmFile {
      Width = header.Width,
      Height = header.Height,
      PaddedWidth = header.PaddedWidth,
      PaddedHeight = header.PaddedHeight,
      Format = (PkmFormat)header.Format,
      Version = version,
      CompressedData = compressedData
    };
  }
}
