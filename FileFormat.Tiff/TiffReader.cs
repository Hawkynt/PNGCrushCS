using System;
using System.Collections.Generic;
using System.IO;
using BitMiracle.LibTiff.Classic;
using LibTiff = BitMiracle.LibTiff.Classic.Tiff;

namespace FileFormat.Tiff;

/// <summary>Reads TIFF files from bytes, streams, or file paths.</summary>
public static class TiffReader {

  public static TiffFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("TIFF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static TiffFile FromStream(Stream stream) {
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

  public static TiffFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < 8)
      throw new InvalidDataException("Data too small for a valid TIFF file.");

    using var ms = new MemoryStream(data);
    using var tiff = LibTiff.ClientOpen("input", "r", ms, new TiffStream());
    if (tiff == null)
      throw new InvalidDataException("Failed to open TIFF data.");

    // Read first IFD (directory 0)
    var (width, height, samplesPerPixel, bitsPerSample, colorMap, pixelData, colorMode) = _ReadCurrentDirectory(tiff);

    // Read additional directories as pages
    var pages = new List<TiffPage>();
    while (tiff.ReadDirectory()) {
      try {
        var (pw, ph, pspp, pbps, pcm, ppd, pcm2) = _ReadCurrentDirectory(tiff);
        pages.Add(new TiffPage {
          Width = pw,
          Height = ph,
          SamplesPerPixel = pspp,
          BitsPerSample = pbps,
          PixelData = ppd,
          ColorMap = pcm,
          ColorMode = pcm2,
        });
      } catch {
        // Skip unreadable directories
      }
    }

    return new TiffFile {
      Width = width,
      Height = height,
      SamplesPerPixel = samplesPerPixel,
      BitsPerSample = bitsPerSample,
      PixelData = pixelData,
      ColorMap = colorMap,
      ColorMode = colorMode,
      Pages = pages,
    };
  }

  private static (int Width, int Height, int SamplesPerPixel, int BitsPerSample, byte[]? ColorMap, byte[] PixelData, TiffColorMode ColorMode) _ReadCurrentDirectory(LibTiff tiff) {
    var widthField = tiff.GetField(TiffTag.IMAGEWIDTH);
    var heightField = tiff.GetField(TiffTag.IMAGELENGTH);
    if (widthField == null || heightField == null)
      throw new InvalidDataException("TIFF missing required width/height tags.");

    var width = widthField[0].ToInt();
    var height = heightField[0].ToInt();

    var sppField = tiff.GetField(TiffTag.SAMPLESPERPIXEL);
    var samplesPerPixel = sppField?[0].ToInt() ?? 1;

    var bpsField = tiff.GetField(TiffTag.BITSPERSAMPLE);
    var bitsPerSample = bpsField?[0].ToInt() ?? 8;

    var photoField = tiff.GetField(TiffTag.PHOTOMETRIC);
    var photometric = photoField != null ? (Photometric)photoField[0].ToInt() : Photometric.RGB;

    byte[]? colorMap = null;
    if (photometric == Photometric.PALETTE) {
      var cmField = tiff.GetField(TiffTag.COLORMAP);
      if (cmField != null) {
        var redMap = cmField[0].ToUShortArray();
        var greenMap = cmField[1].ToUShortArray();
        var blueMap = cmField[2].ToUShortArray();
        var paletteSize = 1 << bitsPerSample;
        colorMap = new byte[paletteSize * 3];
        for (var i = 0; i < paletteSize && i < redMap.Length; ++i) {
          colorMap[i * 3] = (byte)(redMap[i] / 257);
          colorMap[i * 3 + 1] = (byte)(greenMap[i] / 257);
          colorMap[i * 3 + 2] = (byte)(blueMap[i] / 257);
        }
      }
    }

    byte[] pixelData;
    if (tiff.IsTiled())
      pixelData = _ReadTiledPixelData(tiff, width, height, samplesPerPixel, bitsPerSample);
    else
      pixelData = _ReadStrippedPixelData(tiff, width, height, samplesPerPixel, bitsPerSample);

    var colorMode = _DetectColorMode(photometric, samplesPerPixel, bitsPerSample);

    return (width, height, samplesPerPixel, bitsPerSample, colorMap, pixelData, colorMode);
  }

  private static byte[] _ReadStrippedPixelData(LibTiff tiff, int width, int height, int samplesPerPixel, int bitsPerSample) {
    var bytesPerRow = (width * samplesPerPixel * bitsPerSample + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];
    var scanline = new byte[tiff.ScanlineSize()];

    for (var row = 0; row < height; ++row) {
      tiff.ReadScanline(scanline, row);
      var copyLen = Math.Min(bytesPerRow, scanline.Length);
      scanline.AsSpan(0, copyLen).CopyTo(pixelData.AsSpan(row * bytesPerRow));
    }

    return pixelData;
  }

  private static byte[] _ReadTiledPixelData(LibTiff tiff, int width, int height, int samplesPerPixel, int bitsPerSample) {
    var bytesPerPixel = (samplesPerPixel * bitsPerSample + 7) / 8;
    var bytesPerRow = width * bytesPerPixel;
    var pixelData = new byte[bytesPerRow * height];

    var twField = tiff.GetField(TiffTag.TILEWIDTH);
    var thField = tiff.GetField(TiffTag.TILELENGTH);
    if (twField == null || thField == null)
      throw new InvalidDataException("Tiled TIFF missing tile dimension tags.");

    var tileWidth = twField[0].ToInt();
    var tileHeight = thField[0].ToInt();
    var tileBytesPerRow = tileWidth * bytesPerPixel;
    var tileSize = tiff.TileSize();
    var tileBuf = new byte[tileSize];

    var tileIndex = 0;
    for (var ty = 0; ty < height; ty += tileHeight)
    for (var tx = 0; tx < width; tx += tileWidth) {
      tiff.ReadEncodedTile(tileIndex, tileBuf, 0, tileSize);

      var rowsInTile = Math.Min(tileHeight, height - ty);
      var colsInTile = Math.Min(tileWidth, width - tx);
      var bytesToCopy = colsInTile * bytesPerPixel;

      for (var r = 0; r < rowsInTile; ++r) {
        var srcOffset = r * tileBytesPerRow;
        var dstOffset = (ty + r) * bytesPerRow + tx * bytesPerPixel;
        if (srcOffset + bytesToCopy <= tileBuf.Length && dstOffset + bytesToCopy <= pixelData.Length)
          tileBuf.AsSpan(srcOffset, bytesToCopy).CopyTo(pixelData.AsSpan(dstOffset));
      }

      ++tileIndex;
    }

    return pixelData;
  }

  private static TiffColorMode _DetectColorMode(Photometric photometric, int samplesPerPixel, int bitsPerSample) {
    if (photometric == Photometric.PALETTE)
      return TiffColorMode.Palette;

    if (photometric == Photometric.MINISBLACK || photometric == Photometric.MINISWHITE) {
      if (bitsPerSample == 1)
        return TiffColorMode.BiLevel;
      return TiffColorMode.Grayscale;
    }

    if (photometric == Photometric.RGB)
      return TiffColorMode.Rgb;

    return TiffColorMode.Original;
  }
}
