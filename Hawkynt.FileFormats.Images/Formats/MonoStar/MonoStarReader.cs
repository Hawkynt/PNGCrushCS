using System;
using System.IO;

namespace FileFormat.MonoStar;

/// <summary>Reads Atari ST MonoSTar objects from bytes, streams, or file paths.</summary>
public static class MonoStarReader {

  public static MonoStarFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MonoSTar object not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MonoStarFile FromStream(Stream stream) {
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

  public static MonoStarFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < MonoStarFile.HeaderSize + 2)
      throw new InvalidDataException($"A MonoSTar object is longer than its {MonoStarFile.HeaderSize}-byte header, got {data.Length}.");
    if (!data.Slice(4, 2).SequenceEqual(MonoStarFile.MonochromeMarker))
      throw new InvalidDataException("Not a MonoSTar object: the monochrome marker is missing. ColorSTar objects are a different format.");

    // Both dimensions are stored one less than they are.
    var width = (data[0] << 8) + data[1] + 1;
    var height = (data[2] << 8) + data[3] + 1;

    var expected = MonoStarFile.FileSizeFor(width, height);
    if (data.Length != expected)
      throw new InvalidDataException($"A {width}x{height} MonoSTar object is {expected} bytes, got {data.Length}.");

    var bitmap = new byte[expected - MonoStarFile.HeaderSize];
    data[MonoStarFile.HeaderSize..].CopyTo(bitmap);

    return new() { Width = width, Height = height, BitmapData = bitmap };
  }

  public static MonoStarFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
