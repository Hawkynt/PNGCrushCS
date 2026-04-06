using System;
using System.IO;

namespace FileFormat.LogoPainter;

/// <summary>Reads Logo Painter 3 (.lp3) files from bytes, streams, or file paths.</summary>
public static class LogoPainterReader {

  public static LogoPainterFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Logo Painter file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static LogoPainterFile FromStream(Stream stream) {
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

  public static LogoPainterFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static LogoPainterFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < LogoPainterFile.LoadAddressSize + LogoPainterFile.MinPayloadSize)
      throw new InvalidDataException($"Data too small for a valid Logo Painter file (expected at least {LogoPainterFile.LoadAddressSize + LogoPainterFile.MinPayloadSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - LogoPainterFile.LoadAddressSize];
    data.AsSpan(LogoPainterFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
  }
}
