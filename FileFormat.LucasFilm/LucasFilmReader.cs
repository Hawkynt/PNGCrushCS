using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.LucasFilm;

/// <summary>Reads LucasFilm LFF image files from bytes, streams, or file paths.</summary>
public static class LucasFilmReader {

  public static LucasFilmFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("LFF file not found.", file.FullName);

    return FromSpan(File.ReadAllBytes(file.FullName));
  }

  public static LucasFilmFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  public static LucasFilmFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static LucasFilmFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < LucasFilmFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid LFF file (need at least {LucasFilmFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != LucasFilmFile.Magic[0] || data[1] != LucasFilmFile.Magic[1] || data[2] != LucasFilmFile.Magic[2] || data[3] != LucasFilmFile.Magic[3])
      throw new InvalidDataException("Invalid LFF magic bytes.");

    var width = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
    var bpp = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
    var channels = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]);
    var reserved = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid LFF dimensions: {width}x{height}.");

    var pixelDataSize = width * height * 3;
    if (data.Length < LucasFilmFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("LFF file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.Slice(LucasFilmFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Bpp = bpp,
      Channels = channels,
      Reserved = reserved,
      PixelData = pixelData,
    };
  }
}
