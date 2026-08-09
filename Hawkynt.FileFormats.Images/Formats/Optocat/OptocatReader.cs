using System;
using System.IO;

namespace FileFormat.Optocat;

/// <summary>Reads Optocat pictures (.abs) from bytes, streams, or file paths.</summary>
public static class OptocatReader {

  public static OptocatFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Optocat picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static OptocatFile FromStream(Stream stream) {
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

  public static OptocatFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static OptocatFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length <= OptocatFile.MinimumOffset)
      throw new InvalidDataException($"An Optocat picture is longer than {OptocatFile.MinimumOffset} bytes and this is {data.Length}.");

    var littleEndian = data[0] == (byte)'I' && data[1] == (byte)'I';
    if (!littleEndian && !(data[0] == (byte)'M' && data[1] == (byte)'M'))
      throw new InvalidDataException("Not an Optocat picture: it opens with neither II nor MM.");

    var offset = _Word(data, 4, littleEndian);
    var samples = _Word(data, 10, littleEndian);
    var width = _Word(data, 14, littleEndian);
    var height = _Word(data, 16, littleEndian);

    if (offset < OptocatFile.MinimumOffset)
      throw new InvalidDataException($"Optocat: the picture is said to stand at {offset}, which is below the {OptocatFile.MinimumOffset} the reader requires.");

    if (samples is < OptocatFile.MinimumSamples or > OptocatFile.MaximumSamples)
      throw new InvalidDataException($"Optocat: {samples} samples a pixel is outside the {OptocatFile.MinimumSamples} to {OptocatFile.MaximumSamples} that are read.");

    if (width < 1 || height < 1)
      throw new InvalidDataException($"Invalid Optocat dimensions: {width}x{height}.");

    var stride = (width * samples * 8 + 7) / 8;
    var needed = (long)stride * height;
    if (offset > data.Length || data.Length - offset < needed)
      throw new InvalidDataException($"A {width}x{height} Optocat picture of {samples} samples needs {needed} bytes and the file has {Math.Max(0, data.Length - offset)} behind the offset it states.");

    return new() {
      IsLittleEndian = littleEndian,
      Width = width,
      Height = height,
      SamplesPerPixel = samples,
      PixelOffset = offset,
      PixelData = data.Slice(offset, (int)needed).ToArray(),
    };
  }

  private static int _Word(ReadOnlySpan<byte> data, int at, bool littleEndian)
    => littleEndian ? data[at] | (data[at + 1] << 8) : (data[at] << 8) | data[at + 1];
}
