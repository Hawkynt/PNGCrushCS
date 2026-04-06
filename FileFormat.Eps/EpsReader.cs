using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Tiff;

namespace FileFormat.Eps;

/// <summary>Reads EPS files with embedded TIFF preview from bytes, streams, or file paths.</summary>
public static class EpsReader {

  private const int _HEADER_SIZE = 30;

  /// <summary>DOS EPS binary magic: C5 D0 D3 C6.</summary>
  private static ReadOnlySpan<byte> _Magic => [0xC5, 0xD0, 0xD3, 0xC6];

  public static EpsFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("EPS file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static EpsFile FromStream(Stream stream) {
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

  public static EpsFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static EpsFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _HEADER_SIZE)
      throw new InvalidDataException("Data too small for a valid EPS file.");

    if (!data.AsSpan(0, 4).SequenceEqual(_Magic))
      throw new InvalidDataException("Invalid EPS magic bytes.");

    var tiffOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(20));
    var tiffLength = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(24));

    if (tiffOffset == 0 || tiffLength == 0)
      throw new InvalidDataException("EPS file has no embedded TIFF preview.");

    if (tiffOffset + tiffLength > (uint)data.Length)
      throw new InvalidDataException("TIFF preview extends beyond end of file.");

    var tiffData = new byte[tiffLength];
    data.AsSpan((int)tiffOffset, (int)tiffLength).CopyTo(tiffData.AsSpan(0));

    var tiff = TiffReader.FromBytes(tiffData);
    var raw = TiffFile.ToRawImage(tiff);

    // Convert to RGB24 if needed
    var rgb24 = raw.ToRgb24();

    return new EpsFile {
      Width = raw.Width,
      Height = raw.Height,
      PixelData = rgb24,
    };
  }
}
