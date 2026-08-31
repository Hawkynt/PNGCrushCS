using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.DrHalo;

/// <summary>Reads Dr. Halo CUT files from bytes, streams, or file paths.</summary>
public static class DrHaloReader {

  private const int _MaximumPixels = 100_000_000;

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

    var header = DrHaloHeader.ReadFrom(data);
    var width = header.Width;
    var height = header.Height;

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid dimensions: {width}x{height}.");
    if (header.Reserved != 0)
      throw new InvalidDataException("Dr. Halo CUT reserved header field must be zero.");
    if ((long)width * height > _MaximumPixels)
      throw new InvalidDataException($"Dr. Halo CUT exceeds the {_MaximumPixels:N0}-pixel implementation safety limit.");

    var pixelData = new byte[checked(width * height)];
    var offset = DrHaloHeader.StructSize;

    for (var row = 0; row < height; ++row) {
      if (offset + 2 > data.Length)
        throw new InvalidDataException($"Truncated Dr. Halo scanline-length field at row {row}.");

      var scanlineLength = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
      offset += 2;
      if (offset + scanlineLength > data.Length)
        throw new InvalidDataException($"Truncated Dr. Halo scanline payload at row {row}.");

      var scanlineData = data.Slice(offset, scanlineLength);
      var decompressed = DrHaloRleCompressor.DecompressScanline(scanlineData, width);
      decompressed.CopyTo(pixelData.AsSpan(row * width, width));
      offset += scanlineLength;
    }

    if (offset != data.Length)
      throw new InvalidDataException("Unexpected trailing data after the final Dr. Halo scanline.");

    return new DrHaloFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };
  }

  public static DrHaloFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
