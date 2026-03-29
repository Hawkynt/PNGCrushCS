using System;
using System.IO;

namespace FileFormat.AtariAnticMode;

/// <summary>Reads Atari ANTIC Mode E/F Screen files from bytes, streams, or file paths.</summary>
public static class AtariAnticModeReader {

  public static AtariAnticModeFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Atari ANTIC Mode file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariAnticModeFile FromStream(Stream stream) {
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

  public static AtariAnticModeFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != AtariAnticModeFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Atari ANTIC Mode data size: expected exactly {AtariAnticModeFile.ExpectedFileSize} bytes, got {data.Length}.");

    var pixelData = new byte[AtariAnticModeFile.ScreenDataSize];
    data.AsSpan(0, AtariAnticModeFile.ScreenDataSize).CopyTo(pixelData);

    var mode = data[AtariAnticModeFile.ScreenDataSize];
    if (mode != AtariAnticModeFile.ModeE && mode != AtariAnticModeFile.ModeF)
      throw new InvalidDataException($"Invalid ANTIC mode byte: expected 0x{AtariAnticModeFile.ModeE:X2} (Mode E) or 0x{AtariAnticModeFile.ModeF:X2} (Mode F), got 0x{mode:X2}.");

    return new AtariAnticModeFile {
      PixelData = pixelData,
      Mode = mode,
    };
  }
}
