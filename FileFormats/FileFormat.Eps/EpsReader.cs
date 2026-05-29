using System;
using System.IO;
using FileFormat.Tiff;

namespace FileFormat.Eps;

/// <summary>Reads EPS files with embedded TIFF preview from bytes, streams, or file paths.</summary>
public static class EpsReader {

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

  public static EpsFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < EpsHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid EPS file.");

    var header = EpsHeader.ReadFrom(data);

    if (header.Magic != EpsHeader.ExpectedMagic)
      throw new InvalidDataException("Invalid EPS magic bytes.");

    return _ParseFromHeader(header, data);
  }

  public static EpsFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static EpsFile _ParseFromHeader(EpsHeader header, ReadOnlySpan<byte> data) {
    var tiffOffset = header.TiffOffset;
    var tiffLength = header.TiffLength;

    if (tiffOffset == 0 || tiffLength == 0)
      throw new InvalidDataException("EPS file has no embedded TIFF preview.");

    if (tiffOffset + tiffLength > (uint)data.Length)
      throw new InvalidDataException("TIFF preview extends beyond end of file.");

    var tiffData = new byte[tiffLength];
    data.Slice((int)tiffOffset, (int)tiffLength).CopyTo(tiffData.AsSpan(0));

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
