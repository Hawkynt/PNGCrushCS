using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.ColorStarObject;

/// <summary>Reads ColorSTar objects from bytes, streams, or file paths.</summary>
public static class ColorStarObjectReader {

  /// <summary>Colours a coloured object's palette holds.</summary>
  private const int _COLOR_COUNT = 16;

  /// <summary>The largest a palette entry can be: seven in each of three three-bit channels.</summary>
  private const int _MAX_PALETTE_VALUE = 1911;

  public static ColorStarObjectFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Object not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ColorStarObjectFile FromStream(Stream stream) {
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

  public static ColorStarObjectFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 8)
      throw new InvalidDataException("Not an object: too short for a header.");

    // A monochrome object says so in the two bytes that a coloured one spends on its first palette
    // entry, which is why the mono form has to be recognised before the palette is parsed.
    if (data[4] == 0 && data[5] == 1) {
      var monoWidth = (data[0] << 8) + data[1] + 1;
      var monoHeight = (data[2] << 8) + data[3] + 1;
      var monoStride = (monoWidth + 15) >> 4 << 1;
      if (data.Length != 6 + monoStride * monoHeight)
        throw new InvalidDataException($"A monochrome object of {monoWidth}x{monoHeight} is not {data.Length} bytes.");

      return new() {
        Data = data.ToArray(),
        Width = monoWidth,
        Height = monoHeight,
        BitmapOffset = 6,
        Bitplanes = 1,
        Palette = [255, 255, 255, 0, 0, 0],
      };
    }

    var at = 0;
    var palette = new byte[_COLOR_COUNT * 3];
    for (var i = 0; i < _COLOR_COUNT; ++i) {
      var value = _ParseLine(data, ref at);

      // Three bits a channel, packed four bits apart so the number reads as three digits in print.
      palette[i * 3] = ChannelScaling.Expand3((value >> 8) & 7);
      palette[i * 3 + 1] = ChannelScaling.Expand3((value >> 4) & 7);
      palette[i * 3 + 2] = ChannelScaling.Expand3(value & 7);
    }

    if (at + 6 >= data.Length || data[at + 2] != 0 || data[at + 4] != 0 || data[at + 5] != 4)
      throw new InvalidDataException("An object's palette is not followed by a four-plane header.");

    var width = (data[at] << 8) + data[at + 1] + 1;
    var height = data[at + 3] + 1;
    var stride = (width + 15) >> 4 << 3;
    if (at + 6 + height * stride != data.Length)
      throw new InvalidDataException($"An object of {width}x{height} is not {data.Length} bytes.");

    return new() {
      Data = data.ToArray(),
      Width = width,
      Height = height,
      BitmapOffset = at + 6,
      Bitplanes = 4,
      Palette = palette,
    };
  }

  /// <summary>Reads one decimal palette entry and the line ending that has to follow it.</summary>
  private static int _ParseLine(ReadOnlySpan<byte> data, ref int at) {
    var value = 0;
    var digits = 0;

    while (at < data.Length && data[at] is >= (byte)'0' and <= (byte)'9') {
      value = value * 10 + (data[at++] - '0');
      if (value > _MAX_PALETTE_VALUE)
        throw new InvalidDataException($"A palette entry is larger than {_MAX_PALETTE_VALUE}.");

      ++digits;
    }

    if (digits == 0 || at + 1 >= data.Length || data[at] != '\r' || data[at + 1] != '\n')
      throw new InvalidDataException("A palette entry is not a number on a line of its own.");

    at += 2;

    return value;
  }

  public static ColorStarObjectFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
