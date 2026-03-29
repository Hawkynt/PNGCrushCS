using System;
using System.IO;

namespace FileFormat.FunPainter;

/// <summary>Reads Fun Painter II (.fp2/.fun) files from bytes, streams, or file paths.</summary>
public static class FunPainterReader {

  public static FunPainterFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Fun Painter file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FunPainterFile FromStream(Stream stream) {
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

  public static FunPainterFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < FunPainterFile.LoadAddressSize + FunPainterFile.MinBitmapSize)
      throw new InvalidDataException($"Data too small for a valid Fun Painter file (expected at least {FunPainterFile.LoadAddressSize + FunPainterFile.MinBitmapSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - FunPainterFile.LoadAddressSize];
    data.AsSpan(FunPainterFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
  }
}
