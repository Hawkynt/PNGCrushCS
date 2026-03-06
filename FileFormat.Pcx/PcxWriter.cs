using System;
using System.IO;

namespace FileFormat.Pcx;

/// <summary>Assembles PCX file bytes from pixel data.</summary>
public static class PcxWriter {

  public static byte[] ToBytes(PcxFile file) => Assemble(
    file.PixelData,
    file.Width,
    file.Height,
    file.ColorMode,
    file.PlaneConfig,
    file.Palette,
    file.PaletteColorCount
  );

  internal static byte[] Assemble(
    byte[] pixelData,
    int width,
    int height,
    PcxColorMode colorMode,
    PcxPlaneConfig planeConfig,
    byte[]? palette = null,
    int paletteColorCount = 0
  ) {
    using var ms = new MemoryStream();

    byte bitsPerPixel;
    byte numPlanes;

    switch (colorMode) {
      case PcxColorMode.Rgb24 when planeConfig == PcxPlaneConfig.SeparatePlanes:
        bitsPerPixel = 8;
        numPlanes = 3;
        break;
      case PcxColorMode.Rgb24:
      case PcxColorMode.Original when palette == null:
        bitsPerPixel = 8;
        numPlanes = 3;
        break;
      case PcxColorMode.Indexed8:
        bitsPerPixel = 8;
        numPlanes = 1;
        break;
      case PcxColorMode.Indexed4:
        bitsPerPixel = 4;
        numPlanes = 1;
        break;
      case PcxColorMode.Monochrome:
        bitsPerPixel = 1;
        numPlanes = 1;
        break;
      default:
        bitsPerPixel = 8;
        numPlanes = 3;
        break;
    }

    // Bytes per scanline per plane (must be even per PCX spec)
    var bytesPerLine = (width * bitsPerPixel + 7) / 8;
    if (bytesPerLine % 2 != 0)
      ++bytesPerLine;

    // Build EGA palette
    var egaPalette = new byte[48];
    if (palette != null && colorMode is PcxColorMode.Indexed4 or PcxColorMode.Monochrome) {
      var entryCount = Math.Min(paletteColorCount, 16);
      for (var i = 0; i < entryCount && i * 3 + 2 < palette.Length; ++i) {
        egaPalette[i * 3] = palette[i * 3];
        egaPalette[i * 3 + 1] = palette[i * 3 + 1];
        egaPalette[i * 3 + 2] = palette[i * 3 + 2];
      }
    }

    // Build and write the 128-byte header
    var header = new PcxHeader(
      Manufacturer: 0x0A,
      Version: 5,
      Encoding: 1,
      BitsPerPixel: bitsPerPixel,
      XMin: 0,
      YMin: 0,
      XMax: (short)(width - 1),
      YMax: (short)(height - 1),
      HDpi: 72,
      VDpi: 72,
      EgaPalette: egaPalette,
      Reserved: 0,
      NumPlanes: numPlanes,
      BytesPerLine: (short)bytesPerLine,
      PaletteInfo: 1,
      HScreenSize: (short)width,
      VScreenSize: (short)height,
      Padding: new byte[54]
    );

    var headerBytes = new byte[PcxHeader.StructSize];
    header.WriteTo(headerBytes);
    ms.Write(headerBytes, 0, headerBytes.Length);

    // Scanline data (RLE encoded)
    for (var y = 0; y < height; ++y) {
      if (numPlanes == 3) {
        var rowOffset = y * width * 3;
        for (var plane = 0; plane < 3; ++plane) {
          var planeData = new byte[bytesPerLine];
          for (var x = 0; x < width && rowOffset + x * 3 + plane < pixelData.Length; ++x)
            planeData[x] = pixelData[rowOffset + x * 3 + plane];

          var compressed = PcxRleCompressor.Compress(planeData);
          ms.Write(compressed, 0, compressed.Length);
        }
      } else {
        var srcBytesPerRow = (width * bitsPerPixel + 7) / 8;
        var rowOffset = y * srcBytesPerRow;
        var planeData = new byte[bytesPerLine];
        var copyLen = Math.Min(srcBytesPerRow, pixelData.Length - rowOffset);
        if (copyLen > 0)
          Array.Copy(pixelData, rowOffset, planeData, 0, copyLen);

        var compressed = PcxRleCompressor.Compress(planeData);
        ms.Write(compressed, 0, compressed.Length);
      }
    }

    // VGA palette for 256-color images (marker 0x0C + 768 bytes)
    if (colorMode == PcxColorMode.Indexed8 && palette != null) {
      ms.WriteByte(0x0C);
      var vgaPalette = new byte[768];
      for (var i = 0; i < 256 && i * 3 + 2 < palette.Length; ++i) {
        vgaPalette[i * 3] = palette[i * 3];
        vgaPalette[i * 3 + 1] = palette[i * 3 + 1];
        vgaPalette[i * 3 + 2] = palette[i * 3 + 2];
      }

      ms.Write(vgaPalette, 0, vgaPalette.Length);
    }

    return ms.ToArray();
  }
}
