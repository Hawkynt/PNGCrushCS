using System;
using System.IO;

namespace FileFormat.NistIHead;

/// <summary>Reads NIST IHead files from bytes, streams, or file paths.</summary>
public static class NistIHeadReader {

  public static NistIHeadFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("NST file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static NistIHeadFile FromStream(Stream stream) {
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

  public static NistIHeadFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static NistIHeadFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < NistIHeadFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid NST file (need at least {NistIHeadFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != NistIHeadFile.Magic[0] || data[1] != NistIHeadFile.Magic[1] || data[2] != NistIHeadFile.Magic[2] || data[3] != NistIHeadFile.Magic[3])
      throw new InvalidDataException("Invalid NST magic bytes.");

    var width = BitConverter.ToUInt16(data, 4);
    var height = BitConverter.ToUInt16(data, 6);
    var bpp = BitConverter.ToUInt16(data, 8);
    var compression = BitConverter.ToUInt16(data, 10);
    var reserved = BitConverter.ToUInt32(data, 12);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid NST dimensions: {width}x{height}.");

    var pixelDataSize = width * height;
    if (data.Length < NistIHeadFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("NST file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(NistIHeadFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Bpp = bpp,
      Compression = compression,
      Reserved = reserved,
      PixelData = pixelData,
    };
  }
}
