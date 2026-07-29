using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.AtariTt;

/// <summary>Reads Atari TT screens from bytes, streams, or file paths.</summary>
public static class AtariTtReader {

  public static AtariTtFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Atari TT screen not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariTtFile FromStream(Stream stream) {
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

  public static AtariTtFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < AtariTtFile.PaletteOffset || data[0] != 0)
      throw new InvalidDataException("Not an Atari TT screen: the leading zero byte is missing.");

    var resolution = (AtariTtResolution)data[1];
    if (resolution is not (AtariTtResolution.Low or AtariTtResolution.Medium or AtariTtResolution.High))
      throw new InvalidDataException($"Screen mode {data[1]} is not one of the three the TT adds.");

    var expected = AtariTtFile.FileSizeFor(resolution);
    if (data.Length != expected)
      throw new InvalidDataException($"An Atari TT screen in mode {data[1]} is {expected} bytes, got {data.Length}.");

    var count = AtariTtFile.PaletteCountFor(resolution);
    var palette = new short[count];
    for (var i = 0; i < count; ++i)
      palette[i] = BinaryPrimitives.ReadInt16BigEndian(data.Slice(AtariTtFile.PaletteOffset + i * 2, 2));

    var bitmap = new byte[AtariTtFile.BitmapDataSize];
    data.Slice(AtariTtFile.BitmapOffsetFor(resolution), AtariTtFile.BitmapDataSize).CopyTo(bitmap);

    return new() { Resolution = resolution, Palette = palette, BitmapData = bitmap };
  }

  public static AtariTtFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
