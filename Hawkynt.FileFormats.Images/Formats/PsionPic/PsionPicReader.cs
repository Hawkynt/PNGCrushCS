using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.PsionPic;

/// <summary>Reads Psion PIC bitmaps from bytes, streams, or file paths.</summary>
public static class PsionPicReader {

  public static PsionPicFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Psion PIC file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PsionPicFile FromStream(Stream stream) {
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

  public static PsionPicFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < PsionPicFile.FirstRecord + PsionPicFile.RecordSize)
      throw new InvalidDataException($"Data too small for a Psion PIC file (got {data.Length} bytes).");

    if (!data[..PsionPicFile.Magic.Length].SequenceEqual(PsionPicFile.Magic))
      throw new InvalidDataException("Not a Psion PIC file: it does not open with PIC.");

    var count = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
    if (count == 0)
      throw new InvalidDataException("A Psion PIC file states no bitmaps at all.");

    // The first bitmap is the picture; a second, where there is one, is its mask.
    var record = data[PsionPicFile.FirstRecord..];
    var width = BinaryPrimitives.ReadUInt16LittleEndian(record[2..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(record[4..]);
    var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(record[8..]);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"A Psion PIC bitmap of {width}x{height} is no size.");

    // Rows are padded out to whole sixteen-bit words.
    var bytesPerRow = (width + 15) / 16 * 2;
    var start = PsionPicFile.FirstRecord + PsionPicFile.RecordSize + offset;
    if (start < 0 || start + bytesPerRow * height > data.Length)
      throw new InvalidDataException($"A Psion PIC bitmap of {width}x{height} needs {bytesPerRow * height} bytes from {start}; this file is {data.Length}.");

    var pixels = new byte[width * height];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        // The bits run from the least significant end of each byte.
        var value = data[start + y * bytesPerRow + x / 8] >> (x & 7) & 1;
        pixels[y * width + x] = (byte)value;
      }

    return new() {
      Width = width,
      Height = height,
      Count = count,
      PixelData = pixels,
    };
  }

  public static PsionPicFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
