using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Bmp;

namespace FileFormat.JigsawPicture;

/// <summary>Reads the picture out of a Jigsaw 2 puzzle file (.jig).</summary>
public static class JigsawPictureReader {

  /// <summary>What a bitmap file header holds before the information header starts.</summary>
  private const int _FileHeaderSize = 14;

  /// <summary>The smallest information header a Windows bitmap has.</summary>
  private const int _InfoHeaderSize = 40;

  public static JigsawPictureFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Jigsaw picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static JigsawPictureFile FromStream(Stream stream) {
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

  public static JigsawPictureFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static JigsawPictureFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < JigsawPictureFile.MinimumSize)
      throw new InvalidDataException(
        $"Jigsaw picture too small: expected at least {JigsawPictureFile.MinimumSize} bytes, got {data.Length}.");

    if (!data[..2].SequenceEqual(JigsawPictureFile.Signature))
      throw new InvalidDataException("Not a Jigsaw picture: it does not open with \"JG\".");

    var statedSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[2..]);
    var reserved = BinaryPrimitives.ReadUInt32LittleEndian(data[6..]);
    var pixelsAt = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[10..]);
    var infoSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[14..]);
    var planes = BinaryPrimitives.ReadUInt16LittleEndian(data[26..]);

    // Two bytes decide nothing, so the rest of the two headers is what says this is a bitmap. AOL's
    // ART files also open with "JG" and fail here on the first field.
    if (reserved != 0)
      throw new InvalidDataException(
        $"Not a Jigsaw picture: the two reserved words of the file header hold {reserved} rather than nothing.");

    if (statedSize < JigsawPictureFile.MinimumSize || statedSize > data.Length)
      throw new InvalidDataException(
        $"Jigsaw picture states a size of {statedSize} bytes in a file of {data.Length}.");

    if (infoSize < _InfoHeaderSize || infoSize > statedSize - _FileHeaderSize)
      throw new InvalidDataException(
        $"Jigsaw picture states an information header of {infoSize} bytes, which does not fit its own {statedSize}.");

    if (pixelsAt < _FileHeaderSize + infoSize || pixelsAt >= statedSize)
      throw new InvalidDataException(
        $"Jigsaw picture puts its pixels at {pixelsAt} of the {statedSize} bytes it states.");

    if (planes != 1)
      throw new InvalidDataException(
        $"Jigsaw picture states {planes} colour planes where a Windows bitmap has one.");

    // Put the signature back and let the bitmap reader do the rest, so a Jigsaw picture and a
    // bitmap of the same picture come out identical rather than nearly so.
    var restored = data[..statedSize].ToArray();
    restored[0] = (byte)'B';
    restored[1] = (byte)'M';

    return new() { Image = BmpFile.ToRawImage(BmpReader.FromBytes(restored)) };
  }
}
