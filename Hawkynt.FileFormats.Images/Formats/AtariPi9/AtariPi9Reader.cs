using System;
using System.IO;
using FileFormat.AtariPi8;

namespace FileFormat.AtariPi9;

/// <summary>Reads PI9 pictures from bytes, streams, or file paths.</summary>
public static class AtariPi9Reader {

  public static AtariPi9File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariPi9File FromStream(Stream stream) {
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

  public static AtariPi9File FromSpan(ReadOnlySpan<byte> data) {
    switch (data.Length) {
      // Three lengths, one screen: the extra bytes past 7680 are not part of the picture.
      case 7684:
      case 7808:
      case 7936: {
        var offset = AtariPi8Reader.ExecutableOffset(data[..AtariPi9File.Gr9Size]);
        var height = (AtariPi9File.Gr9Size - offset) / 40;
        if (height == 0 || height > 240)
          throw new InvalidDataException($"A Graphics 9 screen is 1 to 240 rows, not {height}.");

        return new() {
          Data = data.ToArray(),
          Width = 320,
          Height = height,
          Kind = AtariPi9Kind.Graphics9,
          BitmapOffset = offset,
        };
      }

      case AtariPi9File.ApacSize:
        return new() { Data = data.ToArray(), Width = 320, Height = 192, Kind = AtariPi9Kind.Apac };

      default: {
        var (width, height) = data.Length switch {
          65024 => (320, 200),
          77824 => (320, 240),
          308224 => (640, 480),
          _ => throw new InvalidDataException($"Not a PI9 picture: {data.Length} bytes."),
        };

        return new() { Data = data.ToArray(), Width = width, Height = height, Kind = AtariPi9Kind.Falcon };
      }
    }
  }

  public static AtariPi9File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
