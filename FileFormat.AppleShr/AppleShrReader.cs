using System;
using System.IO;

namespace FileFormat.AppleShr;

/// <summary>Reads Apple IIgs Super Hi-Res files from bytes, streams, or file paths.</summary>
public static class AppleShrReader {

  public static AppleShrFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("SHR file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AppleShrFile FromStream(Stream stream) {
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

  public static AppleShrFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < AppleShrFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid SHR file (expected {AppleShrFile.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != AppleShrFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid SHR file size (expected {AppleShrFile.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    var pixelData = new byte[AppleShrFile.PixelDataSize];
    data.AsSpan(offset, AppleShrFile.PixelDataSize).CopyTo(pixelData.AsSpan(0));
    offset += AppleShrFile.PixelDataSize;

    var scb = new byte[AppleShrFile.ScbSize];
    data.AsSpan(offset, AppleShrFile.ScbSize).CopyTo(scb.AsSpan(0));
    offset += AppleShrFile.ScbSize;

    // Skip padding
    offset += AppleShrFile.PaddingSize;

    var palette = new byte[AppleShrFile.PaletteSize];
    data.AsSpan(offset, AppleShrFile.PaletteSize).CopyTo(palette.AsSpan(0));

    return new() {
      PixelData = pixelData,
      ScanlineControl = scb,
      Palette = palette,
    };
  }
}
