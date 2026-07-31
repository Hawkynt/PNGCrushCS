using System;
using System.IO;
using FileFormat.Mapletown;

namespace FileFormat.MapletownMl1;

/// <summary>Reads Mapletown Network ML1 pictures from bytes, streams, or file paths.</summary>
public static class MapletownMl1Reader {

  public static MapletownMl1File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MapletownMl1File FromStream(Stream stream) {
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

  public static MapletownMl1File FromSpan(ReadOnlySpan<byte> data) {
    var stream = new MapletownStream(data, 0);
    int[]? pixels = null;
    int width = 0, height = 0;

    if (MapletownDecoder.Decode(ref stream, ref pixels, ref width, ref height, -1) <= 0 || pixels == null)
      throw new InvalidDataException("Not a Mapletown ML1 picture.");

    return new() { Width = width, Height = height, Pixels = MapletownDecoder.ToRgb(pixels) };
  }

  public static MapletownMl1File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
