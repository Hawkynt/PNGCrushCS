using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Fl32;

/// <summary>Reads FL32 (FilmLight 32-bit float) files from bytes, streams, or file paths.</summary>
public static class Fl32Reader {

  public static Fl32File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("FL32 file not found.", file.FullName);

    return FromSpan(File.ReadAllBytes(file.FullName));
  }

  public static Fl32File FromStream(Stream stream) {
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

  public static Fl32File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < Fl32File.HeaderSize)
      throw new InvalidDataException("Data too small for a valid FL32 file.");

    var magic = BinaryPrimitives.ReadUInt32LittleEndian(data);
    if (magic != Fl32File.Magic)
      throw new InvalidDataException($"Invalid FL32 magic: 0x{magic:X8}. Expected 0x{Fl32File.Magic:X8}.");

    var header = Fl32Header.ReadFrom(data);
    var width = header.Width;
    var height = header.Height;
    var channels = header.Channels;

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid FL32 dimensions: {width}x{height}.");

    if (channels is not (1 or 3 or 4))
      throw new InvalidDataException($"Invalid FL32 channel count: {channels}. Expected 1, 3, or 4.");

    var totalFloats = width * height * channels;
    var expectedDataBytes = totalFloats * 4;

    if (data.Length - Fl32File.HeaderSize < expectedDataBytes)
      throw new InvalidDataException("Data too small for the declared image dimensions.");

    var pixelData = new float[totalFloats];
    for (var i = 0; i < totalFloats; ++i)
      pixelData[i] = BinaryPrimitives.ReadSingleLittleEndian(data[(Fl32File.HeaderSize + i * 4)..]);

    return new() {
      Width = width,
      Height = height,
      Channels = channels,
      PixelData = pixelData,
    };
  }

  public static Fl32File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
