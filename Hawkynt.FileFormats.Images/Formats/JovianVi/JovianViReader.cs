using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.JovianVi;

/// <summary>Reads Jovian Logic VI images from bytes, streams, or file paths.</summary>
public static class JovianViReader {

  public static JovianViFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static JovianViFile FromStream(Stream stream) {
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

  public static JovianViFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < JovianViFile.HeaderSize || data[0] != 'V' || data[1] != 'I')
      throw new InvalidDataException("Not a Jovian VI picture.");

    var width = BinaryPrimitives.ReadUInt16LittleEndian(data[3..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(data[5..]);
    if (width == 0 || height == 0)
      throw new InvalidDataException($"A Jovian VI picture states no size: {width}x{height}.");

    // The header says where each part begins, so a file with something between them still reads.
    var paletteOffset = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
    var pixelOffset = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);

    if (paletteOffset + JovianViFile.PaletteSize > data.Length
        || pixelOffset + (long)width * height > data.Length)
      throw new InvalidDataException(
        $"A Jovian VI picture of {width}x{height} needs more than the {data.Length} bytes here.");

    return new() {
      Width = width,
      Height = height,
      Version = data[2],
      Palette = data.Slice(paletteOffset, JovianViFile.PaletteSize).ToArray(),
      PixelData = data.Slice(pixelOffset, width * height).ToArray(),
    };
  }

  public static JovianViFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
