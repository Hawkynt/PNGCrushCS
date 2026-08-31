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
      var length = checked((int)(stream.Length - stream.Position));
      var data = new byte[length];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static MspFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < MspHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid MSP file.");

    var header = MspHeader.ReadFrom(data);
    var version = _DetectVersion(header.Key1, header.Key2)
      ?? throw new InvalidDataException("Invalid MSP magic bytes.");

    ushort checksum = 0;
    for (var offset = 0; offset <= 24; offset += 2)
      checksum ^= BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
    if (checksum != 0)
      throw new InvalidDataException("MSP header checksum mismatch.");

    if (header.Padding1 != 0 || header.Padding2 != 0 || header.Padding3 != 0)
      throw new InvalidDataException("MSP reserved header padding must be zero.");

    var width = (int)header.Width;
    var height = (int)header.Height;
    try {
      MspFile.ValidateDimensions(width, height, nameof(data));
    } catch (ArgumentOutOfRangeException exception) {
      throw new InvalidDataException(exception.Message, exception);
    }

    var bytesPerRow = MspFile.GetRowStride(width);
    var expectedPixelBytes = checked(bytesPerRow * height);
    var pixelData = new byte[expectedPixelBytes];

    if (version == MspVersion.V1)
      _DecodeV1(data, pixelData);
    else
      _DecodeV2(data, height, bytesPerRow, pixelData);

    return new MspFile {
      Width = width,
      Height = height,
      Version = version,
      XAspect = header.XAspect,
      YAspect = header.YAspect,
      XAspectPrinter = header.XAspectPrinter,
      YAspectPrinter = header.YAspectPrinter,
      PrinterWidth = header.PrinterWidth,
      PrinterHeight = header.PrinterHeight,
      XAspectCorr = header.XAspectCorr,
      YAspectCorr = header.YAspectCorr,
      PixelData = pixelData,
    };
  }

  public static MspFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static void _DecodeV1(ReadOnlySpan<byte> data, Span<byte> pixelData) {
    var expectedLength = checked(MspHeader.StructSize + pixelData.Length);
    if (data.Length != expectedLength)
      throw new InvalidDataException($"MSP v1 file length must be exactly {expectedLength} bytes.");

    data[MspHeader.StructSize..].CopyTo(pixelData);
  }

  private static void _DecodeV2(ReadOnlySpan<byte> data, int height, int bytesPerRow, Span<byte> pixelData) {
    var scanLineMapSize = checked(height * 2);
    var encodedOffset = checked(MspHeader.StructSize + scanLineMapSize);
    if (data.Length < encodedOffset)
      throw new InvalidDataException("Data too small for MSP v2 scan-line map.");

    for (var y = 0; y < height; ++y) {
      var scanLineLength = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(MspHeader.StructSize + y * 2, 2));
      if (encodedOffset + scanLineLength > data.Length)
        throw new InvalidDataException($"MSP v2 scanline {y} extends beyond the file.");

      var encodedScanline = data.Slice(encodedOffset, scanLineLength).ToArray();
      var decompressed = MspRleCompressor.Decompress(encodedScanline, bytesPerRow);
      decompressed.CopyTo(pixelData.Slice(y * bytesPerRow, bytesPerRow));
      encodedOffset += scanLineLength;
    }

    if (encodedOffset != data.Length)
      throw new InvalidDataException("Unexpected trailing data after the final MSP v2 scanline.");
  }

  private static MspVersion? _DetectVersion(ushort key1, ushort key2) {
    if (key1 == MspHeader.V1Key1 && key2 == MspHeader.V1Key2)
      return MspVersion.V1;

    if (key1 == MspHeader.V2Key1 && key2 == MspHeader.V2Key2)
      return MspVersion.V2;

    return null;
  }
}
