using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Msp;

/// <summary>Assembles MSP (Microsoft Paint) file bytes from pixel data.</summary>
public static class MspWriter {

  public static byte[] ToBytes(MspFile file) => Assemble(file.PixelData, file.Width, file.Height, file.Version);

  internal static byte[] Assemble(byte[] pixelData, int width, int height, MspVersion version) {
    using var ms = new MemoryStream();
    var bytesPerRow = (width + 7) / 8;

    if (version == MspVersion.V2) {
      // Compress each scanline
      var compressedScanlines = new byte[height][];
      for (var y = 0; y < height; ++y) {
        var scanline = new byte[bytesPerRow];
        var srcOffset = y * bytesPerRow;
        var available = Math.Min(bytesPerRow, pixelData.Length - srcOffset);
        if (available > 0)
          pixelData.AsSpan(srcOffset, available).CopyTo(scanline.AsSpan(0));

        compressedScanlines[y] = MspRleCompressor.Compress(scanline);
      }

      // Build header
      var header = new MspHeader(
        Key1: MspHeader.V2Key1,
        Key2: MspHeader.V2Key2,
        Width: (ushort)width,
        Height: (ushort)height,
        XAspect: 1,
        YAspect: 1,
        XAspectPrinter: 0,
        YAspectPrinter: 0,
        PrinterWidth: 0,
        PrinterHeight: 0,
        XAspectCorr: 0,
        YAspectCorr: 0,
        Checksum: 0,
        Padding1: 0,
        Padding2: 0,
        Padding3: 0
      );

      var headerBytes = new byte[MspHeader.StructSize];
      header.WriteTo(headerBytes);
      ms.Write(headerBytes);

      // Write scan-line map (Height * uint16 LE)
      for (var y = 0; y < height; ++y) {
        var len = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(len, (ushort)compressedScanlines[y].Length);
        ms.Write(len);
      }

      // Write compressed scanlines
      for (var y = 0; y < height; ++y)
        ms.Write(compressedScanlines[y]);
    } else {
      // V1: uncompressed
      var header = new MspHeader(
        Key1: MspHeader.V1Key1,
        Key2: MspHeader.V1Key2,
        Width: (ushort)width,
        Height: (ushort)height,
        XAspect: 1,
        YAspect: 1,
        XAspectPrinter: 0,
        YAspectPrinter: 0,
        PrinterWidth: 0,
        PrinterHeight: 0,
        XAspectCorr: 0,
        YAspectCorr: 0,
        Checksum: 0,
        Padding1: 0,
        Padding2: 0,
        Padding3: 0
      );

      var headerBytes = new byte[MspHeader.StructSize];
      header.WriteTo(headerBytes);
      ms.Write(headerBytes);

      // Write pixel data
      var expectedPixelBytes = bytesPerRow * height;
      var writeLen = Math.Min(expectedPixelBytes, pixelData.Length);
      ms.Write(pixelData, 0, writeLen);

      // Pad with zeros if pixel data is short
      for (var i = writeLen; i < expectedPixelBytes; ++i)
        ms.WriteByte(0);
    }

    return ms.ToArray();
  }
}
