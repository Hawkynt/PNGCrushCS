using System;
using System.IO;

namespace FileFormat.BfxBitware;

/// <summary>Reads Bitware BFX fax files from bytes, streams, or file paths.</summary>
public static class BfxBitwareReader {

  public static BfxBitwareFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("BFX file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static BfxBitwareFile FromStream(Stream stream) {
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

  public static BfxBitwareFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < BfxBitwareFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid BFX file (need at least {BfxBitwareFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != BfxBitwareFile.Magic[0] || data[1] != BfxBitwareFile.Magic[1] || data[2] != BfxBitwareFile.Magic[2] || data[3] != BfxBitwareFile.Magic[3])
      throw new InvalidDataException("Invalid BFX magic bytes.");

    var header = BfxBitwareHeader.ReadFrom(data);
    var version = header.Version;
    var width = header.Width;
    var height = header.Height;
    var compression = header.Compression;

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid BFX dimensions: {width}x{height}.");

    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    if (data.Length < BfxBitwareFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("BFX file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.Slice(BfxBitwareFile.HeaderSize, pixelDataSize).CopyTo(pixelData);

    return new() {
      Width = width,
      Height = height,
      Version = version,
      Compression = compression,
      PixelData = pixelData,
    };
  }

  public static BfxBitwareFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
