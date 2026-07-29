using System;
using System.IO;

namespace FileFormat.TrsPix;

public static class TrsPixReader {

  private const int _HEADER_SIZE = 5;
  private static readonly byte[] _Magic = [0x50, 0x49, 0x58, 0x00];

  public static TrsPixFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("TRS-80 PIX file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static TrsPixFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static TrsPixFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static TrsPixFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _HEADER_SIZE)
      throw new InvalidDataException("Data too small for a valid TRS-80 PIX file.");
    if (!data[..4].SequenceEqual(_Magic))
      throw new InvalidDataException("Missing 'PIX\\0' magic at start of file.");

    var mode = data[4];
    if (mode > 3)
      throw new InvalidDataException($"Unknown TRS-80 PIX HSCREEN mode {mode} (expected 0-3).");

    var (w, bpp) = mode switch {
      0 => (320, 1),
      1 => (320, 2),
      2 => (640, 1),
      3 => (640, 2),
      _ => (0, 0),
    };
    var rowBytes = (w * bpp + 7) >> 3;
    var expected = rowBytes * 192;
    if (data.Length < _HEADER_SIZE + expected)
      throw new InvalidDataException($"TRS-80 PIX payload {data.Length - _HEADER_SIZE} bytes < expected {expected} for mode {mode}.");

    return new TrsPixFile {
      Mode = mode,
      PixelData = data.Slice(_HEADER_SIZE, expected).ToArray(),
    };
  }
}
