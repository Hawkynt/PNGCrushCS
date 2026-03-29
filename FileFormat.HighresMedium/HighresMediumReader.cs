using System;
using System.IO;

namespace FileFormat.HighresMedium;

/// <summary>Reads Highres Medium (.hrm) files from bytes, streams, or file paths.</summary>
public static class HighresMediumReader {

  public static HighresMediumFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Highres Medium file not found.", file.FullName);

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

  public static HighresMediumFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < HighresMediumFile.FileSize)
      throw new InvalidDataException($"Data too small for a valid Highres Medium file (expected {HighresMediumFile.FileSize} bytes, got {data.Length}).");

    var span = data.AsSpan();

    // Frame 1: palette + planar data
    var header1 = HighresMediumHeader.ReadFrom(span);
    var palette1 = header1.GetPaletteArray();
    var pixelData1 = new byte[32000];
    span.Slice(HighresMediumHeader.StructSize, 32000).CopyTo(pixelData1);

    // Frame 2: palette + planar data
    var frame2Offset = HighresMediumHeader.FrameSize;
    var header2 = HighresMediumHeader.ReadFrom(span.Slice(frame2Offset));
    var palette2 = header2.GetPaletteArray();
    var pixelData2 = new byte[32000];
    span.Slice(frame2Offset + HighresMediumHeader.StructSize, 32000).CopyTo(pixelData2);

    return new HighresMediumFile {
      Palette1 = palette1,
      PixelData1 = pixelData1,
      Palette2 = palette2,
      PixelData2 = pixelData2,
    };
  }
}
