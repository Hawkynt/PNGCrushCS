using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.AtariGrafik;

/// <summary>Reads Atari Grafik PCP files from bytes, streams, or file paths.</summary>
public static class AtariGrafikReader {

  public static AtariGrafikFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Atari Grafik file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariGrafikFile FromStream(Stream stream) {
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

  public static AtariGrafikFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static AtariGrafikFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != AtariGrafikFile.ExpectedFileSize)
      throw new InvalidDataException($"Atari Grafik file must be exactly {AtariGrafikFile.ExpectedFileSize} bytes, got {data.Length}.");

    var span = data.AsSpan();
    var resolution = BinaryPrimitives.ReadInt16BigEndian(span);

    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = BinaryPrimitives.ReadInt16BigEndian(span[(2 + i * 2)..]);

    var pixelData = new byte[AtariGrafikFile.PixelDataSize];
    data.AsSpan(AtariGrafikFile.HeaderSize, AtariGrafikFile.PixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Resolution = resolution,
      Palette = palette,
      PixelData = pixelData,
    };
  }
}
