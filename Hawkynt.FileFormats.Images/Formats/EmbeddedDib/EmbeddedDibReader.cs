using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Bmp;

namespace FileFormat.EmbeddedDib;

/// <summary>Finds the Windows bitmap preview inside a drawing and decodes it.</summary>
public static class EmbeddedDibReader {

  public static EmbeddedDibFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("File not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static EmbeddedDibFile FromStream(Stream stream) {
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

  public static EmbeddedDibFile FromSpan(ReadOnlySpan<byte> data) {
    for (var at = 0; at + EmbeddedDibFile.MinHeaderSize <= data.Length; ++at) {
      if (!_Describes(data, at, out var length))
        continue;

      // Put a file header in front of the run and let the bitmap reader do the rest, so every depth,
      // palette form and row order it already knows works here without being written twice.
      var bmp = new byte[14 + length];
      bmp[0] = (byte)'B';
      bmp[1] = (byte)'M';
      BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(2), bmp.Length);
      BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(10), 14 + _PixelOffsetWithin(data, at));
      data.Slice(at, length).CopyTo(bmp.AsSpan(14));

      try {
        return new() { Preview = BmpFile.ToRawImage(BmpReader.FromSpan(bmp)), Offset = at };
      } catch (Exception) {
        // A run of bytes that looked like a header and was not. Keep looking.
      }
    }

    throw new InvalidDataException("No Windows bitmap preview was found in this file.");
  }

  /// <summary>Whether a bitmap header starts here, and how many bytes the whole preview takes.</summary>
  private static bool _Describes(ReadOnlySpan<byte> data, int at, out int length) {
    length = 0;
    var size = BinaryPrimitives.ReadInt32LittleEndian(data[at..]);
    if (size is < EmbeddedDibFile.MinHeaderSize or > EmbeddedDibFile.MaxHeaderSize || at + size > data.Length)
      return false;

    var width = BinaryPrimitives.ReadInt32LittleEndian(data[(at + 4)..]);
    var height = BinaryPrimitives.ReadInt32LittleEndian(data[(at + 8)..]);
    var planes = BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 12)..]);
    var bits = BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 14)..]);
    var compression = BinaryPrimitives.ReadInt32LittleEndian(data[(at + 16)..]);

    // Uncompressed, or one of the two run-length forms the bitmap reader already decodes.
    if (planes != 1 || bits is not (1 or 4 or 8 or 16 or 24 or 32) || compression is not (0 or 1 or 2))
      return false;

    // Height is signed: negative means the rows run top-down.
    var rows = Math.Abs((long)height);
    if (width < 1 || width > EmbeddedDibFile.MaxDimension || rows < 1 || rows > EmbeddedDibFile.MaxDimension)
      return false;

    // A packed picture states its own length; an unpacked one is a stride times its rows.
    var packed = BinaryPrimitives.ReadInt32LittleEndian(data[(at + 20)..]);
    var stride = ((long)width * bits + 31) / 32 * 4;
    var payload = compression == 0 || packed <= 0 ? stride * rows : packed;
    var total = _PixelOffsetWithin(data, at) + payload;
    if (at + total > data.Length)
      return false;

    length = (int)total;
    return true;
  }

  /// <summary>Bytes from the header to the picture: the header itself and the palette after it.</summary>
  private static int _PixelOffsetWithin(ReadOnlySpan<byte> data, int at) {
    var size = BinaryPrimitives.ReadInt32LittleEndian(data[at..]);
    var bits = BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 14)..]);
    if (bits > 8)
      return size;

    var used = BinaryPrimitives.ReadInt32LittleEndian(data[(at + 32)..]);
    if (used is <= 0 or > 256)
      used = 1 << bits;

    return size + used * 4;
  }

  public static EmbeddedDibFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
