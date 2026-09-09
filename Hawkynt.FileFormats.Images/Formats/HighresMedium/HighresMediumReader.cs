using System;
using System.IO;

namespace FileFormat.HighresMedium;

/// <summary>Reads HighresMedium (.hrm) files from bytes, streams, or file paths.</summary>
public static class HighresMediumReader {

  public static HighresMediumFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("HighresMedium file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HighresMediumFile FromStream(Stream stream) {
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

  public static HighresMediumFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < HighresMediumFile.FileSize)
      throw new InvalidDataException(
        $"A HighresMedium picture takes {HighresMediumFile.FileSize} bytes; this file is {data.Length}.");

    return new() {
      BitmapData = data[..HighresMediumFile.BitmapSize].ToArray(),
      Palettes = data.Slice(
        HighresMediumFile.PalettesOffset,
        HighresMediumFile.PaletteRowSize * HighresMediumFile.ImageHeight).ToArray(),
    };
  }

  public static HighresMediumFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
