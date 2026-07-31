using System;
using System.IO;
using System.Text;

namespace FileFormat.Uimg;

/// <summary>Reads UIMG pictures from bytes, streams, or file paths.</summary>
public static class UimgReader {

  /// <summary>Bytes each kind of palette spends on one colour; the first kind has no palette.</summary>
  private static ReadOnlySpan<byte> _PaletteUnit => [0, 2, 2, 4];

  public static UimgFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static UimgFile FromStream(Stream stream) {
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

  public static UimgFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 15 || Encoding.ASCII.GetString(data[..UimgFile.Signature.Length]) != UimgFile.Signature
        || data[6] != 0)
      throw new InvalidDataException("Not a UIMG picture.");

    var palette = data[7];
    if (palette >= _PaletteUnit.Length)
      throw new InvalidDataException($"A UIMG picture names no palette kind {palette}.");

    var depth = data[8];
    var chunk = data[9];
    var width = (data[10] << 8) | data[11];
    var height = (data[12] << 8) | data[13];
    var count = width * height;
    var bitmapOffset = UimgFile.PaletteOffset + (_PaletteUnit[palette] << depth);

    // The header states three things that have to agree with the file's own length, and the ways
    // they can agree are exactly the arrangements the format allows.
    switch (chunk) {
      case 0 or 255:
        if (palette == 0 || depth > 8 || (width & 15) != 0
            || data.Length != bitmapOffset + (count >> 3) * depth)
          throw new InvalidDataException("A UIMG picture of bitplanes does not match its own header.");

        break;

      case 1:
        if (palette == 0 || depth > 8 || data.Length != bitmapOffset + count)
          throw new InvalidDataException("A UIMG picture of bytes does not match its own header.");

        break;

      case 2 or 3 or 4:
        if (palette != 0 || depth != chunk << 3 || data.Length != UimgFile.PaletteOffset + count * chunk)
          throw new InvalidDataException("A UIMG true-colour picture does not match its own header.");

        break;

      default:
        throw new InvalidDataException($"A UIMG picture is arranged 0 to 4 or 255, not {chunk}.");
    }

    if (chunk == 255 && depth is not (1 or 2 or 4))
      throw new InvalidDataException($"A UIMG picture packed without rows is 1, 2 or 4 bits a pixel, not {depth}.");

    if (width == 0 || height == 0)
      throw new InvalidDataException($"A UIMG picture is not {width}x{height}.");

    return new() {
      Data = data.ToArray(),
      Width = width,
      Height = height,
      Depth = depth,
      PaletteKind = palette,
      Chunk = chunk,
      BitmapOffset = bitmapOffset,
    };
  }

  public static UimgFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
