using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Pl4Picture;

/// <summary>Reads PL4 pictures from bytes, streams, or file paths.</summary>
public static class Pl4PictureReader {

  public static Pl4PictureFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Pl4PictureFile FromStream(Stream stream) {
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

  public static Pl4PictureFile FromSpan(ReadOnlySpan<byte> data) {
    var unpacked = Lz4Frame.Unpack(data, Pl4PictureFile.UnpackedSize);

    // Each screen begins with two bytes that are not part of its palette and are always zero, which
    // is the only thing distinguishing this from any other pair of screens packed the same way.
    if (unpacked[0] != 0 || unpacked[1] != 0
        || unpacked[Pl4PictureFile.ScreenSize] != 0 || unpacked[Pl4PictureFile.ScreenSize + 1] != 0)
      throw new InvalidDataException("Not a PL4 picture: its screens do not start where they should.");

    return new() { Unpacked = unpacked };
  }

  public static Pl4PictureFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
