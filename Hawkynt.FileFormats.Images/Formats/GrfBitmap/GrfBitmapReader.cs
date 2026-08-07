using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.GrfBitmap;

/// <summary>Reads .grf bitmaps from bytes, streams, or file paths.</summary>
public static class GrfBitmapReader {

  public static GrfBitmapFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static GrfBitmapFile FromStream(Stream stream) {
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

  public static GrfBitmapFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length <= GrfBitmapFile.HeaderSize)
      throw new InvalidDataException($"Data too small for a .grf bitmap: got {data.Length} bytes.");

    var stated = BinaryPrimitives.ReadUInt16LittleEndian(data);

    // The header says how long the bitmap is, so it can be checked rather than guessed at: it has
    // to be whole rows, and it has to be there.
    if (stated == 0 || stated % GrfBitmapFile.BytesPerRow != 0)
      throw new InvalidDataException($"A .grf bitmap is whole rows of {GrfBitmapFile.BytesPerRow} bytes; the header states {stated}.");

    if (data.Length < GrfBitmapFile.HeaderSize + stated)
      throw new InvalidDataException($"The header states {stated} bytes of bitmap; the file holds {data.Length - GrfBitmapFile.HeaderSize}.");

    return new() {
      Height = stated / GrfBitmapFile.BytesPerRow,
      LoadAddress = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]),
      PixelData = data.Slice(GrfBitmapFile.HeaderSize, stated).ToArray(),
    };
  }

  public static GrfBitmapFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
