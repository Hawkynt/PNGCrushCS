using System;
using System.IO;

namespace FileFormat.Interfile;

/// <summary>Reads Interfile nuclear medicine images from bytes, streams, or file paths.</summary>
public static class InterfileReader {

  /// <summary>Minimum size: the magic line "!INTERFILE :=\n" is at least 10 bytes for the magic prefix.</summary>
  private const int _MIN_SIZE = 10;

  /// <summary>Magic bytes at file start: "!INTERFILE" (ASCII).</summary>
  private static readonly byte[] _MAGIC = { 0x21, 0x49, 0x4E, 0x54, 0x45, 0x52, 0x46, 0x49, 0x4C, 0x45 };

  public static InterfileFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Interfile not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static InterfileFile FromStream(Stream stream) {
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

  public static InterfileFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_SIZE)
      throw new InvalidDataException("Data too small for a valid Interfile.");

    for (var i = 0; i < _MAGIC.Length; ++i)
      if (data[i] != _MAGIC[i])
        throw new InvalidDataException("Invalid Interfile: missing '!INTERFILE' magic.");

    var header = InterfileHeaderParser.Parse(data);

    var dataOffset = header.DataOffset;
    var remainingBytes = data.Length - dataOffset;

    var expectedPixelBytes = header.Width * header.Height * header.BytesPerPixel;

    byte[] pixelData;
    if (remainingBytes <= 0) {
      pixelData = [];
    } else {
      pixelData = new byte[expectedPixelBytes];
      data.AsSpan(dataOffset, Math.Min(remainingBytes, expectedPixelBytes)).CopyTo(pixelData.AsSpan(0));
    }

    return new InterfileFile {
      Width = header.Width,
      Height = header.Height,
      BytesPerPixel = header.BytesPerPixel,
      NumberFormat = header.NumberFormat,
      PixelData = pixelData,
    };
  }
}
