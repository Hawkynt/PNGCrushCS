using System;
using System.IO;

namespace FileFormat.DuoMedium;

/// <summary>Reads medium-resolution Duo pictures from bytes, streams, or file paths.</summary>
public static class DuoMediumReader {

  public static DuoMediumFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Duo picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DuoMediumFile FromStream(Stream stream) {
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

  public static DuoMediumFile FromSpan(ReadOnlySpan<byte> data) {
    // Either exactly the palette and two bitmaps, or that padded out; nothing else identifies it.
    if (data.Length != DuoMediumFile.MinFileSize && data.Length != DuoMediumFile.PaddedFileSize)
      throw new InvalidDataException(
        $"A medium-resolution Duo picture is {DuoMediumFile.MinFileSize} or {DuoMediumFile.PaddedFileSize} bytes, got {data.Length}.");

    return new() { Data = data.ToArray() };
  }

  public static DuoMediumFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
