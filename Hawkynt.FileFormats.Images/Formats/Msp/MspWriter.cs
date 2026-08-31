using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Msp;

/// <summary>Assembles MSP (Microsoft Paint) file bytes from pixel data.</summary>
public static class MspWriter {

  public static byte[] ToBytes(MspFile file) {
    MspFile.Validate(file, nameof(file));
    return _Assemble(file);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height, MspVersion version) => ToBytes(new MspFile {
    PixelData = pixelData,
    Width = width,
    Height = height,
    Version = version,
  });

  private static byte[] _Assemble(MspFile file) {
    var headerBytes = _BuildHeader(file);
    if (file.Version == MspVersion.V1) {
      var result = new byte[checked(MspHeader.StructSize + file.PixelData.Length)];
      headerBytes.CopyTo(result, 0);
      file.PixelData.CopyTo(result, MspHeader.StructSize);
      return result;
    }

    var bytesPerRow = MspFile.GetRowStride(file.Width);
    var compressedScanlines = new byte[file.Height][];
    var compressedBytes = 0;
    for (var y = 0; y < file.Height; ++y) {
      var scanline = file.PixelData.AsSpan(y * bytesPerRow, bytesPerRow).ToArray();
      var compressed = MspRleCompressor.Compress(scanline);
      if (compressed.Length > ushort.MaxValue)
        throw new InvalidOperationException("An MSP encoded scanline exceeds the 16-bit scanline-map limit.");
      compressedScanlines[y] = compressed;
      compressedBytes = checked(compressedBytes + compressed.Length);
    }

    var scanLineMapSize = checked(file.Height * 2);
    var output = new byte[checked(MspHeader.StructSize + scanLineMapSize + compressedBytes)];
    headerBytes.CopyTo(output, 0);

    var dataOffset = MspHeader.StructSize + scanLineMapSize;
    for (var y = 0; y < file.Height; ++y) {
      var compressed = compressedScanlines[y];
      BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(MspHeader.StructSize + y * 2), checked((ushort)compressed.Length));
      compressed.CopyTo(output, dataOffset);
      dataOffset += compressed.Length;
    }

    return output;
  }

  private static byte[] _BuildHeader(MspFile file) {
    var header = new MspHeader(
      Key1: file.Version == MspVersion.V1 ? MspHeader.V1Key1 : MspHeader.V2Key1,
      Key2: file.Version == MspVersion.V1 ? MspHeader.V1Key2 : MspHeader.V2Key2,
      Width: checked((ushort)file.Width),
      Height: checked((ushort)file.Height),
      XAspect: file.XAspect,
      YAspect: file.YAspect,
      XAspectPrinter: file.XAspectPrinter,
      YAspectPrinter: file.YAspectPrinter,
      PrinterWidth: file.PrinterWidth,
      PrinterHeight: file.PrinterHeight,
      XAspectCorr: file.XAspectCorr,
      YAspectCorr: file.YAspectCorr,
      Checksum: 0,
      Padding1: 0,
      Padding2: 0,
      Padding3: 0
    );

    var bytes = new byte[MspHeader.StructSize];
    header.WriteTo(bytes);

    ushort checksum = 0;
    for (var offset = 0; offset < 24; offset += 2)
      checksum ^= BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(24), checksum);
    return bytes;
  }
}
