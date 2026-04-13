using System;
using System.IO;

namespace FileFormat.AliasPix;

/// <summary>Reads Alias/Wavefront PIX files from bytes, streams, or file paths.</summary>
public static class AliasPixReader {

  public static AliasPixFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("AliasPix file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AliasPixFile FromStream(Stream stream) {
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

  public static AliasPixFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < AliasPixHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid AliasPix file.");

    var header = AliasPixHeader.ReadFrom(data);

    if (header.BitsPerPixel != 24 && header.BitsPerPixel != 32)
      throw new InvalidDataException($"Invalid AliasPix bits per pixel: {header.BitsPerPixel}. Expected 24 or 32.");

    if (header.Width == 0 || header.Height == 0)
      throw new InvalidDataException("AliasPix image dimensions must be non-zero.");

    var bytesPerPixel = header.BitsPerPixel / 8;
    var rleData = data.Slice(AliasPixHeader.StructSize);
    var pixelData = AliasPixRleCompressor.Decompress(rleData, header.Width, header.Height, bytesPerPixel);

    return new AliasPixFile {
      Width = header.Width,
      Height = header.Height,
      XOffset = header.XOffset,
      YOffset = header.YOffset,
      BitsPerPixel = header.BitsPerPixel,
      PixelData = pixelData
    };
    }

  public static AliasPixFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < AliasPixHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid AliasPix file.");

    var header = AliasPixHeader.ReadFrom(data.AsSpan());

    if (header.BitsPerPixel != 24 && header.BitsPerPixel != 32)
      throw new InvalidDataException($"Invalid AliasPix bits per pixel: {header.BitsPerPixel}. Expected 24 or 32.");

    if (header.Width == 0 || header.Height == 0)
      throw new InvalidDataException("AliasPix image dimensions must be non-zero.");

    var bytesPerPixel = header.BitsPerPixel / 8;
    var rleData = data.AsSpan(AliasPixHeader.StructSize);
    var pixelData = AliasPixRleCompressor.Decompress(rleData, header.Width, header.Height, bytesPerPixel);

    return new AliasPixFile {
      Width = header.Width,
      Height = header.Height,
      XOffset = header.XOffset,
      YOffset = header.YOffset,
      BitsPerPixel = header.BitsPerPixel,
      PixelData = pixelData
    };
  }
}
