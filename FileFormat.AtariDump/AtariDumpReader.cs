using System;
using System.IO;

namespace FileFormat.AtariDump;

/// <summary>Reads generic Atari 8-bit screen dumps from bytes, streams, or file paths.</summary>
public static class AtariDumpReader {

  public static AtariDumpFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Atari screen dump file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariDumpFile FromStream(Stream stream) {
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

  public static AtariDumpFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < AtariDumpFile.MinFileSize)
      throw new InvalidDataException($"Invalid Atari screen dump data size: expected at least {AtariDumpFile.MinFileSize} bytes, got {data.Length}.");

    int width;
    int height;
    byte anticMode;

    if (data.Length == AtariDumpFile.DefaultFileSize) {
      // Standard Graphics 8: 320x192 at 1bpp
      width = AtariDumpFile.DefaultWidth;
      height = AtariDumpFile.DefaultHeight;
      anticMode = AtariDumpFile.DefaultAnticMode;
    } else {
      // Generic: assume 40 bytes per line and compute height
      width = AtariDumpFile.DefaultWidth;
      height = data.Length / AtariDumpFile.DefaultBytesPerLine;
      anticMode = AtariDumpFile.DefaultAnticMode;

      if (height < 1)
        throw new InvalidDataException($"Invalid Atari screen dump: data too small to form a single scanline.");
    }

    var pixelData = new byte[data.Length];
    data.AsSpan(0, data.Length).CopyTo(pixelData);

    return new AtariDumpFile {
      Width = width,
      Height = height,
      AnticMode = anticMode,
      PixelData = pixelData,
    };
  }
}
