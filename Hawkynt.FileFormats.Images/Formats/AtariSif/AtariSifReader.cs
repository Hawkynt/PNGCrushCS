using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.AtariSif;

public static class AtariSifReader {

  private const int _HEADER_SIZE = 10;
  private static readonly byte[] _Magic = [0x53, 0x49, 0x46, 0x00];

  public static AtariSifFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Atari SIF file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariSifFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static AtariSifFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static AtariSifFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _HEADER_SIZE)
      throw new InvalidDataException("Data too small for a valid Atari SIF file.");
    if (!data[..4].SequenceEqual(_Magic))
      throw new InvalidDataException("Missing 'SIF\\0' magic at start of file.");

    var width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(4, 2));
    var height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(6, 2));
    var mode = data[8];
    if (mode is not (8 or 9 or 15))
      throw new InvalidDataException($"Unsupported Atari ANTIC mode {mode} (expected 8, 9, or 15).");
    if (width == 0 || height == 0)
      throw new InvalidDataException($"Atari SIF reports zero geometry {width}x{height}.");

    var bpp = mode == 9 ? 1 : 2;
    var rowBytes = (width * bpp + 7) >> 3;
    var expected = rowBytes * height;
    if (data.Length < _HEADER_SIZE + expected)
      throw new InvalidDataException($"Atari SIF payload too small for {width}x{height} mode {mode}.");

    return new AtariSifFile {
      Width = width,
      Height = height,
      AnticMode = mode,
      PixelData = data.Slice(_HEADER_SIZE, expected).ToArray(),
    };
  }
}
