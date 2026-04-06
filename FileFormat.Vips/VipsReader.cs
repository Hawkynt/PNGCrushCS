using System;
using System.IO;

namespace FileFormat.Vips;

/// <summary>Reads VIPS native image files from bytes, streams, or file paths.</summary>
public static class VipsReader {

  internal const int HeaderSize = VipsHeader.StructSize;
  internal const int MagicValue = VipsHeader.MagicValue;

  public static VipsFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("VIPS file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static VipsFile FromStream(Stream stream) {
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

  public static VipsFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static VipsFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < VipsHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid VIPS file.");

    var header = VipsHeader.ReadFrom(data.AsSpan(0, VipsHeader.StructSize));

    if (header.Magic != VipsHeader.MagicValue)
      throw new InvalidDataException($"Invalid VIPS magic: expected 0x{VipsHeader.MagicValue:X8}, got 0x{header.Magic:X8}.");

    if (header.Width <= 0)
      throw new InvalidDataException($"Invalid VIPS width: {header.Width}.");
    if (header.Height <= 0)
      throw new InvalidDataException($"Invalid VIPS height: {header.Height}.");
    if (header.Bands <= 0)
      throw new InvalidDataException($"Invalid VIPS band count: {header.Bands}.");

    var bandFormat = (VipsBandFormat)header.BandFormat;
    if (bandFormat != VipsBandFormat.UChar)
      throw new NotSupportedException($"Only UChar band format is supported, got {bandFormat}.");

    var bytesPerPixel = header.Bands;
    var expectedPixelBytes = header.Width * header.Height * bytesPerPixel;
    var available = data.Length - VipsHeader.StructSize;
    var copyLen = Math.Min(expectedPixelBytes, available);

    var pixelData = new byte[expectedPixelBytes];
    data.AsSpan(VipsHeader.StructSize, copyLen).CopyTo(pixelData.AsSpan(0));

    return new VipsFile {
      Width = header.Width,
      Height = header.Height,
      Bands = header.Bands,
      BandFormat = bandFormat,
      PixelData = pixelData,
    };
  }
}
