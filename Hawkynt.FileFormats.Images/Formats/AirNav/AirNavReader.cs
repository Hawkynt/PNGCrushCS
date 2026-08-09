using System;
using System.IO;

namespace FileFormat.AirNav;

/// <summary>Reads AirNav pictures (.anv) from bytes, streams, or file paths.</summary>
public static class AirNavReader {

  /// <summary>Where the information header states its own size.</summary>
  private const int _InformationHeaderSizeAt = 14;

  /// <summary>The only information header size a picture of this shape has.</summary>
  private const int _InformationHeaderSize = 40;

  /// <summary>Where the width, height and depth stand, as a Windows bitmap keeps them.</summary>
  private const int _WidthAt = 18;
  private const int _HeightAt = 22;
  private const int _DepthAt = 28;

  public static AirNavFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("AirNav picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AirNavFile FromStream(Stream stream) {
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

  public static AirNavFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static AirNavFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < AirNavFile.PixelOffset)
      throw new InvalidDataException($"Data too small for an AirNav picture (need at least {AirNavFile.PixelOffset} bytes, got {data.Length}).");

    if (!data[..AirNavFile.Magic.Length].SequenceEqual(AirNavFile.Magic))
      throw new InvalidDataException("Not an AirNav picture: it does not open with AN.");

    var informationHeader = _Read32(data, _InformationHeaderSizeAt);
    if (informationHeader != _InformationHeaderSize)
      throw new InvalidDataException($"An AirNav picture states an information header of {_InformationHeaderSize} bytes and this one states {informationHeader}.");

    var depth = data[_DepthAt] | (data[_DepthAt + 1] << 8);
    if (depth != 8)
      throw new InvalidDataException($"An AirNav picture is eight bits a pixel and this one states {depth}, which its fixed colour table and picture offsets would not fit.");

    var width = _Read32(data, _WidthAt);
    var height = _Read32(data, _HeightAt);
    if (width is < 1 or > AirNavFile.MaximumSide || height is < 1 or > AirNavFile.MaximumSide)
      throw new InvalidDataException($"Invalid AirNav dimensions: {width}x{height}.");

    var stride = (width + 3) & ~3;
    var needed = (long)AirNavFile.PixelOffset + (long)stride * height;
    if (data.Length < needed)
      throw new InvalidDataException($"A {width}x{height} AirNav picture needs {needed} bytes and the file has {data.Length}.");

    var palette = new byte[AirNavFile.PaletteEntries * 3];
    for (var i = 0; i < AirNavFile.PaletteEntries; ++i) {
      var at = AirNavFile.PaletteOffset + i * 4;
      palette[i * 3] = data[at + 2];
      palette[i * 3 + 1] = data[at + 1];
      palette[i * 3 + 2] = data[at];
    }

    var pixels = new byte[(long)width * height];
    for (var y = 0; y < height; ++y) {
      var source = AirNavFile.PixelOffset + (height - 1 - y) * stride;
      data.Slice(source, width).CopyTo(pixels.AsSpan(y * width));
    }

    return new() {
      Width = width,
      Height = height,
      PixelData = pixels,
      Palette = palette,
    };
  }

  private static int _Read32(ReadOnlySpan<byte> data, int at)
    => data[at] | (data[at + 1] << 8) | (data[at + 2] << 16) | (data[at + 3] << 24);
}
