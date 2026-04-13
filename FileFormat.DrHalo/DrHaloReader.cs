using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.DrHalo;

/// <summary>Reads Dr. Halo CUT files from bytes, streams, or file paths.</summary>
public static class DrHaloReader {

  public static DrHaloFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CUT file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DrHaloFile FromStream(Stream stream) {
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

  public static DrHaloFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < DrHaloHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Dr. Halo CUT file.");

    var span = data;
    var header = DrHaloHeader.ReadFrom(span);

    var width = header.Width;
    var height = header.Height;

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid dimensions: {width}x{height}.");

    var pixelData = new byte[width * height];
    var offset = DrHaloHeader.StructSize;

    for (var row = 0; row < height; ++row) {
      if (offset + 2 > data.Length)
        break;

      var scanlineLength = BinaryPrimitives.ReadUInt16LittleEndian(span[offset..]);
      offset += 2;

      if (scanlineLength == 0)
        continue;

      var end = Math.Min(offset + scanlineLength, data.Length);
      var scanlineData = span[offset..end];
      var decompressed = DrHaloRleCompressor.DecompressScanline(scanlineData, width);
      decompressed.AsSpan(0, width).CopyTo(pixelData.AsSpan(row * width));

      offset += scanlineLength;
    }

    return new DrHaloFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };
    }

  public static DrHaloFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < DrHaloHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Dr. Halo CUT file.");

    var span = data.AsSpan();
    var header = DrHaloHeader.ReadFrom(span);

    var width = header.Width;
    var height = header.Height;

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid dimensions: {width}x{height}.");

    var pixelData = new byte[width * height];
    var offset = DrHaloHeader.StructSize;

    for (var row = 0; row < height; ++row) {
      if (offset + 2 > data.Length)
        break;

      var scanlineLength = BinaryPrimitives.ReadUInt16LittleEndian(span[offset..]);
      offset += 2;

      if (scanlineLength == 0)
        continue;

      var end = Math.Min(offset + scanlineLength, data.Length);
      var scanlineData = span[offset..end];
      var decompressed = DrHaloRleCompressor.DecompressScanline(scanlineData, width);
      decompressed.AsSpan(0, width).CopyTo(pixelData.AsSpan(row * width));

      offset += scanlineLength;
    }

    return new DrHaloFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };
  }
}
