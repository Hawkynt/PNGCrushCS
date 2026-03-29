using System;
using System.IO;

namespace FileFormat.Pcx;

/// <summary>Reads PCX files from bytes, streams, or file paths.</summary>
public static class PcxReader {

  public static PcxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PCX file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PcxFile FromStream(Stream stream) {
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

  public static PcxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < PcxHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid PCX file.");

    var span = data.AsSpan();
    var header = PcxHeader.ReadFrom(span);

    if (header.Manufacturer != 0x0A)
      throw new InvalidDataException("Invalid PCX manufacturer byte.");

    var encoding = header.Encoding;
    var bitsPerPixel = header.BitsPerPixel;
    var xMin = header.XMin;
    var yMin = header.YMin;
    var xMax = header.XMax;
    var yMax = header.YMax;
    var egaPalette = header.EgaPalette;
    var numPlanes = header.NumPlanes;
    var bytesPerLine = header.BytesPerLine;

    var width = xMax - xMin + 1;
    var height = yMax - yMin + 1;

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid PCX dimensions: {width}x{height}.");

    // Decode RLE scanlines starting after the 128-byte header
    var totalBytesPerScanline = bytesPerLine * numPlanes;
    var decodedScanlines = new byte[totalBytesPerScanline * height];
    var offset = PcxHeader.StructSize;

    if (encoding == 1) {
      var scanlineOffset = 0;
      for (var y = 0; y < height; ++y) {
        var bytesDecoded = 0;
        while (bytesDecoded < totalBytesPerScanline && offset < data.Length) {
          var b = data[offset++];
          if ((b & 0xC0) == 0xC0) {
            var count = b & 0x3F;
            if (offset >= data.Length)
              break;
            var value = data[offset++];
            for (var j = 0; j < count && bytesDecoded < totalBytesPerScanline; ++j)
              decodedScanlines[scanlineOffset + bytesDecoded++] = value;
          } else {
            decodedScanlines[scanlineOffset + bytesDecoded++] = b;
          }
        }

        scanlineOffset += totalBytesPerScanline;
      }
    } else {
      var remaining = Math.Min(decodedScanlines.Length, data.Length - offset);
      data.AsSpan(offset, remaining).CopyTo(decodedScanlines.AsSpan(0));
    }

    // Determine color mode and extract pixel data + palette
    PcxColorMode colorMode;
    var planeConfig = PcxPlaneConfig.SinglePlane;
    byte[]? palette = null;
    var paletteColorCount = 0;
    byte[] pixelData;

    if (numPlanes == 3 && bitsPerPixel == 8) {
      colorMode = PcxColorMode.Rgb24;
      planeConfig = PcxPlaneConfig.SeparatePlanes;
      pixelData = new byte[width * height * 3];

      for (var y = 0; y < height; ++y) {
        var scanlineBase = y * totalBytesPerScanline;
        for (var x = 0; x < width; ++x) {
          pixelData[(y * width + x) * 3] = decodedScanlines[scanlineBase + x];
          pixelData[(y * width + x) * 3 + 1] = decodedScanlines[scanlineBase + bytesPerLine + x];
          pixelData[(y * width + x) * 3 + 2] = decodedScanlines[scanlineBase + bytesPerLine * 2 + x];
        }
      }
    } else if (numPlanes == 1 && bitsPerPixel == 8) {
      colorMode = PcxColorMode.Indexed8;

      var srcBytesPerRow = width;
      pixelData = new byte[width * height];
      for (var y = 0; y < height; ++y)
        decodedScanlines.AsSpan(y * bytesPerLine, srcBytesPerRow).CopyTo(pixelData.AsSpan(y * srcBytesPerRow));

      if (data.Length >= 769) {
        var paletteMarkerPos = data.Length - 769;
        if (data[paletteMarkerPos] == 0x0C) {
          palette = new byte[768];
          data.AsSpan(paletteMarkerPos + 1, 768).CopyTo(palette.AsSpan(0));
          paletteColorCount = 256;
        }
      }
    } else if (numPlanes == 1 && bitsPerPixel == 4) {
      colorMode = PcxColorMode.Indexed4;
      var srcBytesPerRow = (width + 1) / 2;
      pixelData = new byte[srcBytesPerRow * height];
      for (var y = 0; y < height; ++y)
        decodedScanlines.AsSpan(y * bytesPerLine, srcBytesPerRow).CopyTo(pixelData.AsSpan(y * srcBytesPerRow));

      palette = new byte[48];
      egaPalette.AsSpan(0, 48).CopyTo(palette);
      paletteColorCount = 16;
    } else if (numPlanes == 1 && bitsPerPixel == 1) {
      colorMode = PcxColorMode.Monochrome;
      var srcBytesPerRow = (width + 7) / 8;
      pixelData = new byte[srcBytesPerRow * height];
      for (var y = 0; y < height; ++y)
        decodedScanlines.AsSpan(y * bytesPerLine, srcBytesPerRow).CopyTo(pixelData.AsSpan(y * srcBytesPerRow));

      palette = new byte[6];
      egaPalette.AsSpan(0, Math.Min(6, egaPalette.Length)).CopyTo(palette);
      paletteColorCount = 2;
    } else {
      colorMode = PcxColorMode.Original;
      planeConfig = numPlanes > 1 ? PcxPlaneConfig.SeparatePlanes : PcxPlaneConfig.SinglePlane;
      pixelData = new byte[decodedScanlines.Length];
      decodedScanlines.AsSpan(0, decodedScanlines.Length).CopyTo(pixelData);
    }

    return new PcxFile {
      Width = width,
      Height = height,
      BitsPerPixel = bitsPerPixel,
      PixelData = pixelData,
      Palette = palette,
      PaletteColorCount = paletteColorCount,
      ColorMode = colorMode,
      PlaneConfig = planeConfig
    };
  }
}
