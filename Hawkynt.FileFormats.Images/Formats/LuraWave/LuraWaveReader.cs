using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.LuraWave;

/// <summary>Reads LuraWave files from bytes, streams, or file paths.</summary>
public static class LuraWaveReader {

  public static LuraWaveFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("LuraWave file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static LuraWaveFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static LuraWaveFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  /// <summary>Parses the header and decodes every stripe.</summary>
  /// <exception cref="InvalidDataException">Not a LuraWave stream, a malformed header, or coded data shorter than declared.</exception>
  public static LuraWaveFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    var header = LuraWaveHeader.Parse(data);
    return new() {
      Width = header.Width,
      Height = header.Height,
      Mode = header.Mode,
      Header = header,
      PixelData = LuraWaveCodec.Decode(data, header),
    };
  }
}
