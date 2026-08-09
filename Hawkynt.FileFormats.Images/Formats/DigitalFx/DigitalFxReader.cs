using System;
using System.IO;

namespace FileFormat.DigitalFx;

/// <summary>Reads Digital F/X pictures (.tdim) from bytes, streams, or file paths.</summary>
public static class DigitalFxReader {

  public static DigitalFxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Digital F/X picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DigitalFxFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var buffer = new byte[stream.Length - stream.Position];
      stream.ReadExactly(buffer);
      return FromBytes(buffer);
    }

    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromBytes(memory.ToArray());
  }

  public static DigitalFxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static DigitalFxFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 16)
      throw new InvalidDataException($"Data too small for a Digital F/X picture (need at least 16 bytes, got {data.Length}).");

    if (!data[..DigitalFxFile.Magic.Length].SequenceEqual(DigitalFxFile.Magic))
      throw new InvalidDataException("Not a Digital F/X picture: it does not open with 00 02 00 20.");

    var height = _Read16(data, DigitalFxFile.HeightAt);
    var width = _Read16(data, DigitalFxFile.WidthAt);
    if (width is < 1 or > DigitalFxFile.MaximumSide || height is < 1 or > DigitalFxFile.MaximumSide)
      throw new InvalidDataException($"Invalid Digital F/X dimensions: {width}x{height}.");

    var at = _Read32(data, DigitalFxFile.PictureOffsetAt);
    if (at < 16 || at >= data.Length)
      throw new InvalidDataException($"A Digital F/X picture states its picture begins at {at} and the file has {data.Length} bytes.");

    // The most a run can be worth is 128 pixels for its five bytes, so a file this short cannot
    // describe a picture this large however well it codes. Without the check a header stating
    // 32000 by 32000 would ask for four gigabytes before reading its first run.
    var most = (long)(data.Length - at) * 128;
    if ((long)width * height > most)
      throw new InvalidDataException($"A {width}x{height} Digital F/X picture cannot be coded in the {data.Length - at} bytes that follow its header.");

    var count = width * height;
    var pixels = new byte[(long)count * DigitalFxFile.BytesPerPixel];
    var written = 0;

    while (written < count) {
      if (at >= data.Length)
        throw new InvalidDataException($"A {width}x{height} Digital F/X picture ran out of runs after {written} of its {count} pixels.");

      var control = (sbyte)data[at++];
      if (control >= 0) {
        var run = control + 1;
        if (at + DigitalFxFile.BytesPerPixel > data.Length)
          throw new InvalidDataException("A Digital F/X run states a pixel the file does not carry.");

        var pixel = data.Slice(at, DigitalFxFile.BytesPerPixel);
        at += DigitalFxFile.BytesPerPixel;
        if (run > count - written)
          run = count - written;

        for (var i = 0; i < run; ++i)
          pixel.CopyTo(pixels.AsSpan((written + i) * DigitalFxFile.BytesPerPixel));

        written += run;
        continue;
      }

      var literal = (control & 0x7F) + 1;
      if (literal > count - written)
        literal = count - written;

      var length = literal * DigitalFxFile.BytesPerPixel;
      if (at + length > data.Length)
        throw new InvalidDataException("A Digital F/X literal run states more pixels than the file carries.");

      data.Slice(at, length).CopyTo(pixels.AsSpan(written * DigitalFxFile.BytesPerPixel));
      at += length;
      written += literal;
    }

    return new() {
      Width = width,
      Height = height,
      PixelData = pixels,
    };
  }

  private static int _Read16(ReadOnlySpan<byte> data, int at) => (data[at] << 8) | data[at + 1];

  private static int _Read32(ReadOnlySpan<byte> data, int at)
    => (data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3];
}
