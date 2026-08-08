using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.AtariHighResPage;

/// <summary>Reads 640 by 400 Atari monochrome pictures whose header is padding.</summary>
public static class AtariHighResPageReader {

  public static AtariHighResPageFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariHighResPageFile FromStream(Stream stream) {
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

  public static AtariHighResPageFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length <= AtariHighResPageFile.BitmapSize)
      throw new InvalidDataException(
        $"A 640x400 monochrome picture needs more than {AtariHighResPageFile.BitmapSize} bytes; got {data.Length}.");

    var header = data.Length - AtariHighResPageFile.BitmapSize;
    if (header > AtariHighResPageFile.MaxHeaderSize)
      throw new InvalidDataException(
        $"There are {header} bytes in front of the picture, which is more header than this reads.");

    if (BinaryPrimitives.ReadUInt16BigEndian(data) != AtariHighResPageFile.HighResolution)
      throw new InvalidDataException("Not a high-resolution Atari picture: the resolution word is not 2.");

    return new() {
      Header = data[..header].ToArray(),
      PixelData = data[header..].ToArray(),
    };
  }

  public static AtariHighResPageFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
