using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Msp;

/// <summary>Reads MSP (Microsoft Paint) files from bytes, streams, or file paths.</summary>
public static class MspReader {

  public static MspFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MSP file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MspFile FromStream(Stream stream) {
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

  public static MspFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static MspFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < MspHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid MSP file.");

    var span = data.AsSpan();
    var header = MspHeader.ReadFrom(span);

    var version = _DetectVersion(header.Key1, header.Key2);
    if (version == null)
      throw new InvalidDataException("Invalid MSP magic bytes.");

    var width = (int)header.Width;
    var height = (int)header.Height;
    var bytesPerRow = (width + 7) / 8;

    byte[] pixelData;

    if (version == MspVersion.V1) {
      // V1: uncompressed pixel data starts at offset 32
      var expectedPixelBytes = bytesPerRow * height;
      pixelData = new byte[expectedPixelBytes];
      var available = Math.Min(expectedPixelBytes, data.Length - MspHeader.StructSize);
      data.AsSpan(MspHeader.StructSize, available).CopyTo(pixelData.AsSpan(0));
    } else {
      // V2: scan-line map at offset 32 (Height * uint16 LE values), then encoded scanlines
      var scanLineMapOffset = MspHeader.StructSize;
      var scanLineMapSize = height * 2;

      if (data.Length < scanLineMapOffset + scanLineMapSize)
        throw new InvalidDataException("Data too small for MSP v2 scan-line map.");

      var scanLineLengths = new ushort[height];
      for (var i = 0; i < height; ++i)
        scanLineLengths[i] = BinaryPrimitives.ReadUInt16LittleEndian(span[(scanLineMapOffset + i * 2)..]);

      var dataOffset = scanLineMapOffset + scanLineMapSize;
      pixelData = new byte[bytesPerRow * height];

      for (var y = 0; y < height; ++y) {
        var scanLineLength = scanLineLengths[y];

        if (scanLineLength == 0) {
          // Zero length means a blank (all-zero) scanline
          Array.Clear(pixelData, y * bytesPerRow, bytesPerRow);
        } else {
          var encodedScanline = new byte[scanLineLength];
          var available = Math.Min(scanLineLength, data.Length - dataOffset);
          data.AsSpan(dataOffset, available).CopyTo(encodedScanline.AsSpan(0));

          var decompressed = MspRleCompressor.Decompress(encodedScanline, bytesPerRow);
          decompressed.AsSpan(0, bytesPerRow).CopyTo(pixelData.AsSpan(y * bytesPerRow));
        }

        dataOffset += scanLineLength;
      }
    }

    return new MspFile {
      Width = width,
      Height = height,
      Version = version.Value,
      PixelData = pixelData
    };
  }

  private static MspVersion? _DetectVersion(ushort key1, ushort key2) {
    if (key1 == MspHeader.V1Key1 && key2 == MspHeader.V1Key2)
      return MspVersion.V1;

    if (key1 == MspHeader.V2Key1 && key2 == MspHeader.V2Key2)
      return MspVersion.V2;

    return null;
  }
}
