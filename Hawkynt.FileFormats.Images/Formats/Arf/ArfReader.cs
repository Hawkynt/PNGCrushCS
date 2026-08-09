using System;
using System.IO;

namespace FileFormat.Arf;

/// <summary>Reads ARF pictures (.arf) from bytes, streams, or file paths.</summary>
public static class ArfReader {

  /// <summary>The largest type code the reader takes; XnView refuses anything above it by name.</summary>
  private const int _MaximumImageType = 2;

  public static ArfFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ARF picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ArfFile FromStream(Stream stream) {
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

  public static ArfFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static ArfFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < ArfFile.HeaderSize)
      throw new InvalidDataException($"Data too small for an ARF picture (need at least {ArfFile.HeaderSize} bytes, got {data.Length}).");

    if (!data[..ArfFile.Magic.Length].SequenceEqual(ArfFile.Magic))
      throw new InvalidDataException("Not an ARF picture: the four bytes it opens with are not the ones this format uses.");

    var version = _Read(data, 4);
    if (version != ArfFile.SupportedVersion)
      throw new InvalidDataException($"An ARF picture of version {version} is not read; version {ArfFile.SupportedVersion} is.");

    var height = _Read(data, 8);
    var width = _Read(data, 12);
    var imageType = _Read(data, 16);
    var pixelOffset = _Read(data, 24);

    if (width is < 1 or > ArfFile.MaximumSide || height is < 1 or > ArfFile.MaximumSide)
      throw new InvalidDataException($"Invalid ARF dimensions: {width}x{height}.");

    if (imageType > _MaximumImageType)
      throw new InvalidDataException($"ARF: image type {imageType} is not supported.");

    if (pixelOffset < ArfFile.HeaderSize || pixelOffset > data.Length)
      throw new InvalidDataException($"The header states the picture stands at {pixelOffset}, which is not inside a file of {data.Length} bytes.");

    var needed = (long)width * height;
    if (data.Length - pixelOffset < needed)
      throw new InvalidDataException($"A {width}x{height} ARF picture needs {needed} bytes and the file has {data.Length - pixelOffset} behind the offset it states.");

    var pixels = new byte[needed];
    data.Slice((int)pixelOffset, (int)needed).CopyTo(pixels);

    return new() {
      Width = (int)width,
      Height = (int)height,
      ImageType = (int)imageType,
      PixelData = pixels,
    };
  }

  private static uint _Read(ReadOnlySpan<byte> data, int at)
    => (uint)((data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3]);
}
