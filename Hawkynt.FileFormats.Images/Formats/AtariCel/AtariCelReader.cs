using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AtariCel;

/// <summary>Reads Atari ST CEL pictures from bytes, streams, or file paths.</summary>
public static class AtariCelReader {

  public static AtariCelFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariCelFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  public static AtariCelFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < AtariCelFile.HeaderSize || !AtariCelFile.MatchesSignature(data))
      throw new InvalidDataException("Not an Atari ST CEL picture.");

    var width = BinaryPrimitives.ReadUInt16BigEndian(data[AtariCelFile.WidthOffset..]);
    var height = BinaryPrimitives.ReadUInt16BigEndian(data[AtariCelFile.HeightOffset..]);
    if (width == 0 || height == 0)
      throw new InvalidDataException($"An Atari CEL states no size: {width}x{height}.");

    // The length follows from the size, so it is what tells one of these from a file that merely
    // begins the same way.
    var stride = AtariStGraphics.BytesPerRow(width, AtariCelFile.Planes);
    if (data.Length != AtariCelFile.HeaderSize + stride * height)
      throw new InvalidDataException(
        $"{width}x{height} is {AtariCelFile.HeaderSize + stride * height} bytes; this file is {data.Length}.");

    return new() {
      Width = width,
      Height = height,
      Palette = AtariStGraphics.ReadPalette(data, AtariCelFile.PaletteOffset, AtariCelFile.PaletteColors),
      PixelData = AtariStGraphics.UnpackBitplanes(
        data, AtariCelFile.HeaderSize, stride, AtariCelFile.Planes, width, height),
    };
  }

  public static AtariCelFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
