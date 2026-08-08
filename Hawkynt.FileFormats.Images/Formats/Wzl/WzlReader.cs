using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Wzl;

/// <summary>Undoes the scrambling on a .wzl and hands the bitmap underneath to the bitmap reader.</summary>
public static class WzlReader {

  public static WzlFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static WzlFile FromStream(Stream stream) {
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

  public static WzlFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < WzlFile.MinimumLength)
      throw new InvalidDataException("Not a .wzl picture: it is too short to hold a bitmap header.");

    var bitmap = data.ToArray();
    var scrambled = Math.Min(WzlFile.ScrambledLength, bitmap.Length);
    for (var at = 0; at < scrambled; ++at)
      bitmap[at] ^= WzlFile.Key;

    if (bitmap[0] != (byte)'B' || bitmap[1] != (byte)'M')
      throw new InvalidDataException("Not a .wzl picture: undoing the scrambling does not give a bitmap.");

    // The bitmap states its own length, so a file that is not this one will disagree here. That check
    // is what keeps two bytes of magic from being enough to draw somebody else's file.
    var stated = BinaryPrimitives.ReadUInt32LittleEndian(bitmap.AsSpan(2));
    if (stated != (uint)bitmap.Length)
      throw new InvalidDataException($"A .wzl picture states {stated} bytes and the file is {bitmap.Length}.");

    return new() { Bitmap = bitmap };
  }

  public static WzlFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
