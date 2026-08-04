using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.HereticM8;

/// <summary>Reads Heretic II mipmap textures from bytes, streams, or file paths.</summary>
public static class HereticM8Reader {

  public static HereticM8File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Heretic II texture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HereticM8File FromStream(Stream stream) {
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

  public static HereticM8File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < HereticM8File.PaletteOffset + 768)
      throw new InvalidDataException($"Data too small for a Heretic II texture (got {data.Length} bytes).");

    var version = BinaryPrimitives.ReadInt32LittleEndian(data);
    if (version != HereticM8File.Version)
      throw new InvalidDataException($"A Heretic II texture states version {HereticM8File.Version}; this file states {version}.");

    // The first mipmap level is the full-size picture; the rest are it at halving sizes.
    var width = BinaryPrimitives.ReadInt32LittleEndian(data[HereticM8File.WidthsOffset..]);
    var height = BinaryPrimitives.ReadInt32LittleEndian(data[(HereticM8File.WidthsOffset + HereticM8File.Levels * 4)..]);
    var offset = BinaryPrimitives.ReadInt32LittleEndian(data[(HereticM8File.WidthsOffset + HereticM8File.Levels * 8)..]);

    if (width <= 0 || height <= 0 || width > 0xFFFF || height > 0xFFFF)
      throw new InvalidDataException($"A Heretic II texture of {width}x{height} is no size.");

    if (offset < 0 || offset + width * height > data.Length)
      throw new InvalidDataException($"A Heretic II texture of {width}x{height} needs {width * height} bytes from {offset}; this file is {data.Length}.");

    var palette = new byte[256 * 3];
    data.Slice(HereticM8File.PaletteOffset, palette.Length).CopyTo(palette);

    return new() {
      Width = width,
      Height = height,
      PixelData = data.Slice(offset, width * height).ToArray(),
      Palette = palette,
    };
  }

  public static HereticM8File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
