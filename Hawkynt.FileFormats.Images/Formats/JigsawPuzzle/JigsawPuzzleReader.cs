using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.JigsawPuzzle;

/// <summary>Reads jigsaw puzzle pictures from bytes, streams, or file paths.</summary>
public static class JigsawPuzzleReader {

  public static JigsawPuzzleFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Jigsaw puzzle picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static JigsawPuzzleFile FromStream(Stream stream) {
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

  public static JigsawPuzzleFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < JigsawPuzzleFile.InfoHeaderAt + 20)
      throw new InvalidDataException($"Data too small for a jigsaw puzzle picture (got {data.Length} bytes).");

    if (!data[..JigsawPuzzleFile.Magic.Length].SequenceEqual(JigsawPuzzleFile.Magic))
      throw new InvalidDataException("Not a jigsaw puzzle picture: it does not open the way one does.");

    var bitmapLength = BinaryPrimitives.ReadUInt32LittleEndian(data[JigsawPuzzleFile.BitmapLengthAt..]);
    var pixelOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[JigsawPuzzleFile.PixelOffsetAt..]);
    var width = BinaryPrimitives.ReadInt32LittleEndian(data[JigsawPuzzleFile.WidthAt..]);
    var height = BinaryPrimitives.ReadInt32LittleEndian(data[JigsawPuzzleFile.HeightAt..]);
    var bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(data[JigsawPuzzleFile.BitsPerPixelAt..]);
    var compression = BinaryPrimitives.ReadUInt32LittleEndian(data[JigsawPuzzleFile.CompressionAt..]);

    // A bitmap may be stored bottom-up or top-down; only the sign of the height says which, and the
    // pixels occupy the same number of bytes either way.
    var rows = height < 0 ? -(long)height : height;

    if (width < 1 || rows < 1 || bitsPerPixel < 1)
      throw new InvalidDataException($"Invalid jigsaw puzzle size: {width}x{height} at {bitsPerPixel} bits.");

    if (compression != 0)
      throw new InvalidDataException($"A jigsaw puzzle picture stores its pixels uncompressed; this one states compression {compression}.");

    if (bitmapLength > data.Length)
      throw new InvalidDataException($"The jigsaw puzzle states a bitmap of {bitmapLength} bytes in a file of {data.Length}.");

    // The one thing that says this is a picture and not merely two letters: the stated length must
    // be the stated pixel offset plus the pixels themselves, every row padded to four bytes. Nothing
    // that is not one of these accounts for itself that way.
    var stride = ((long)width * bitsPerPixel + 31) / 32 * 4;
    var accounted = pixelOffset + rows * stride;
    if (accounted != bitmapLength)
      throw new InvalidDataException(
        $"The jigsaw puzzle states {bitmapLength} bytes of bitmap and its {width}x{rows} at {bitsPerPixel} bits accounts for {accounted}.");

    var embedded = data[..(int)bitmapLength].ToArray();
    JigsawPuzzleFile.BitmapMagic.CopyTo(embedded);

    return new() {
      Width = width,
      Height = (int)rows,
      BitsPerPixel = bitsPerPixel,
      Embedded = embedded,
      Puzzle = data[(int)bitmapLength..].ToArray(),
    };
  }

  public static JigsawPuzzleFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
