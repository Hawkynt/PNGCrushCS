using System;
using System.IO;

namespace FileFormat.AtariPi8;

/// <summary>Reads Atari 8-bit PI8 pictures from bytes, streams, or file paths.</summary>
public static class AtariPi8Reader {

  public static AtariPi8File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariPi8File FromStream(Stream stream) {
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

  /// <summary>
  /// The six bytes an Atari executable starts with, when the block they declare accounts for the
  /// rest of the file exactly and so cannot be picture data that happens to look like a header.
  /// </summary>
  public static int ExecutableOffset(ReadOnlySpan<byte> data) {
    if (data.Length < 7 || data[0] != 255 || data[1] != 255)
      return 0;

    var start = data[2] | (data[3] << 8);
    var end = data[4] | (data[5] << 8);
    var length = end - start + 1;

    return length > 0 && 6 + length == data.Length ? 6 : 0;
  }

  public static AtariPi8File FromSpan(ReadOnlySpan<byte> data) {
    var monochrome = data.Length switch {
      AtariPi8File.ColorSize => false,
      AtariPi8File.MonochromeSize => true,
      _ => throw new InvalidDataException($"Not a PI8 picture: {data.Length} bytes."),
    };

    var offset = monochrome ? ExecutableOffset(data) : 0;
    var height = (data.Length - offset) / AtariPi8File.Stride;
    if (height == 0 || height > 240)
      throw new InvalidDataException($"A PI8 picture is 1 to 240 rows, not {height}.");

    return new() { Data = data.ToArray(), BitmapOffset = offset, Height = height, IsMonochrome = monochrome };
  }

  public static AtariPi8File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
