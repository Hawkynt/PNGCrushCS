using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.AtariPi5;

/// <summary>Reads .pi5 pictures from bytes, streams, or file paths.</summary>
public static class AtariPi5Reader {

  public static AtariPi5File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariPi5File FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static AtariPi5File FromSpan(ReadOnlySpan<byte> data) {
    // Nothing states the size, so the length is what identifies one.
    if (data.Length != AtariPi5File.FileSize)
      throw new InvalidDataException(
        $"A 320x240 sixteen-colour Atari picture is {AtariPi5File.FileSize} bytes, got {data.Length}.");

    var palette = new ushort[AtariPi5File.ColorCount];
    for (var i = 0; i < palette.Length; ++i)
      palette[i] = BinaryPrimitives.ReadUInt16BigEndian(data[(AtariPi5File.PaletteOffset + i * 2)..]);

    return new() {
      Mode = BinaryPrimitives.ReadUInt16BigEndian(data),
      Palette = palette,
      BitmapData = data.Slice(AtariPi5File.BitmapOffset, AtariPi5File.BitmapSize).ToArray(),
    };
  }

  public static AtariPi5File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
