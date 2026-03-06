using System;
using System.IO;
using BitMiracle.LibTiff.Classic;
using Compression.Core;
using LibTiff = BitMiracle.LibTiff.Classic.Tiff;

namespace FileFormat.Tiff;

/// <summary>Assembles TIFF file bytes from pixel data.</summary>
public static class TiffWriter {

  public static byte[] ToBytes(TiffFile file, TiffCompression compression = TiffCompression.None,
    TiffPredictor predictor = TiffPredictor.None, int stripRowCount = 1, int zopfliIterations = 15,
    int tileWidth = 0, int tileHeight = 0) {
    var photometric = _DeterminePhotometric(file);
    return Assemble(
      file.PixelData, file.Width, file.Height,
      file.SamplesPerPixel, file.BitsPerSample,
      compression, predictor, stripRowCount, zopfliIterations,
      photometric, file.ColorMap,
      tileWidth, tileHeight
    );
  }

  private static ushort _DeterminePhotometric(TiffFile file) {
    if (file.ColorMap != null)
      return (ushort)Photometric.PALETTE;
    if (file.SamplesPerPixel == 1)
      return (ushort)Photometric.MINISBLACK;
    return (ushort)Photometric.RGB;
  }

  internal static byte[] Assemble(
    byte[] pixelData,
    int width,
    int height,
    int samplesPerPixel,
    int bitsPerSample,
    TiffCompression compression,
    TiffPredictor predictor,
    int stripRowCount,
    int zopfliIterations,
    ushort photometric,
    byte[]? colorMap = null,
    int tileWidth = 0,
    int tileHeight = 0
  ) {
    using var ms = new MemoryStream();
    using var tiff = LibTiff.ClientOpen("output", "w", ms, new TiffStream());

    tiff.SetField(TiffTag.IMAGEWIDTH, width);
    tiff.SetField(TiffTag.IMAGELENGTH, height);
    tiff.SetField(TiffTag.SAMPLESPERPIXEL, samplesPerPixel);
    tiff.SetField(TiffTag.BITSPERSAMPLE, bitsPerSample);
    tiff.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);
    tiff.SetField(TiffTag.PHOTOMETRIC, (Photometric)photometric);
    tiff.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);

    var isTiled = tileWidth > 0 && tileHeight > 0;
    if (isTiled) {
      tiff.SetField(TiffTag.TILEWIDTH, tileWidth);
      tiff.SetField(TiffTag.TILELENGTH, tileHeight);
    } else {
      tiff.SetField(TiffTag.ROWSPERSTRIP, stripRowCount);
    }

    var libTiffCompression = _MapCompression(compression);
    tiff.SetField(TiffTag.COMPRESSION, libTiffCompression);

    if (predictor == TiffPredictor.HorizontalDifferencing &&
        compression is not (TiffCompression.None or TiffCompression.PackBits))
      tiff.SetField(TiffTag.PREDICTOR, Predictor.HORIZONTAL);

    if (colorMap != null && photometric == (ushort)Photometric.PALETTE) {
      var paletteSize = 1 << bitsPerSample;
      var redMap = new ushort[paletteSize];
      var greenMap = new ushort[paletteSize];
      var blueMap = new ushort[paletteSize];

      for (var i = 0; i < paletteSize && i * 3 + 2 < colorMap.Length; ++i) {
        redMap[i] = (ushort)(colorMap[i * 3] * 257);
        greenMap[i] = (ushort)(colorMap[i * 3 + 1] * 257);
        blueMap[i] = (ushort)(colorMap[i * 3 + 2] * 257);
      }

      tiff.SetField(TiffTag.COLORMAP, redMap, greenMap, blueMap);
    }

    if (isTiled)
      _WriteTiles(tiff, pixelData, width, height, samplesPerPixel, bitsPerSample, tileWidth, tileHeight,
        compression, predictor, zopfliIterations);
    else
      _WriteStrips(tiff, pixelData, width, height, samplesPerPixel, bitsPerSample, stripRowCount, compression,
        predictor, zopfliIterations);

    tiff.WriteDirectory();
    tiff.Flush();

    return ms.ToArray();
  }

  private static void _WriteStrips(
    LibTiff tiff,
    byte[] pixelData,
    int width,
    int height,
    int samplesPerPixel,
    int bitsPerSample,
    int stripRowCount,
    TiffCompression compression,
    TiffPredictor predictor,
    int zopfliIterations
  ) {
    var bytesPerRow = (width * samplesPerPixel * bitsPerSample + 7) / 8;

    if (compression is TiffCompression.DeflateUltra or TiffCompression.DeflateHyper) {
      tiff.SetField(TiffTag.COMPRESSION, BitMiracle.LibTiff.Classic.Compression.DEFLATE);

      for (var row = 0; row < height; row += stripRowCount) {
        var rowsInStrip = Math.Min(stripRowCount, height - row);
        var stripSize = bytesPerRow * rowsInStrip;
        var stripOffset = row * bytesPerRow;

        var stripData = new byte[stripSize];
        Array.Copy(pixelData, stripOffset, stripData, 0, Math.Min(stripSize, pixelData.Length - stripOffset));

        if (predictor == TiffPredictor.HorizontalDifferencing && samplesPerPixel > 0 && bitsPerSample == 8)
          _ApplyHorizontalDifferencing(stripData, bytesPerRow, rowsInStrip, samplesPerPixel);

        var useHyper = compression == TiffCompression.DeflateHyper;
        var compressedStrip = ZopfliDeflater.Compress(stripData, useHyper, zopfliIterations);
        tiff.WriteRawStrip(row / stripRowCount, compressedStrip, compressedStrip.Length);
      }
    } else {
      for (var row = 0; row < height; row += stripRowCount) {
        var rowsInStrip = Math.Min(stripRowCount, height - row);

        for (var r = 0; r < rowsInStrip; ++r) {
          var rowOffset = (row + r) * bytesPerRow;
          var rowData = new byte[bytesPerRow];
          Array.Copy(pixelData, rowOffset, rowData, 0, Math.Min(bytesPerRow, pixelData.Length - rowOffset));
          tiff.WriteScanline(rowData, row + r);
        }
      }
    }
  }

  private static void _WriteTiles(
    LibTiff tiff,
    byte[] pixelData,
    int width,
    int height,
    int samplesPerPixel,
    int bitsPerSample,
    int tileWidth,
    int tileHeight,
    TiffCompression compression,
    TiffPredictor predictor,
    int zopfliIterations
  ) {
    var bytesPerPixel = (samplesPerPixel * bitsPerSample + 7) / 8;
    var bytesPerRow = width * bytesPerPixel;
    var tileBytesPerRow = tileWidth * bytesPerPixel;
    var tileSize = tileBytesPerRow * tileHeight;
    var isZopfli = compression is TiffCompression.DeflateUltra or TiffCompression.DeflateHyper;

    if (isZopfli)
      tiff.SetField(TiffTag.COMPRESSION, BitMiracle.LibTiff.Classic.Compression.DEFLATE);

    var tileIndex = 0;
    for (var ty = 0; ty < height; ty += tileHeight)
    for (var tx = 0; tx < width; tx += tileWidth) {
      var tileData = new byte[tileSize];

      var rowsInTile = Math.Min(tileHeight, height - ty);
      var colsInTile = Math.Min(tileWidth, width - tx);
      var bytesToCopy = colsInTile * bytesPerPixel;

      for (var r = 0; r < rowsInTile; ++r) {
        var srcOffset = (ty + r) * bytesPerRow + tx * bytesPerPixel;
        var dstOffset = r * tileBytesPerRow;
        if (srcOffset + bytesToCopy <= pixelData.Length)
          Array.Copy(pixelData, srcOffset, tileData, dstOffset, bytesToCopy);
      }

      if (isZopfli) {
        if (predictor == TiffPredictor.HorizontalDifferencing && samplesPerPixel > 0 && bitsPerSample == 8)
          _ApplyHorizontalDifferencing(tileData, tileBytesPerRow, tileHeight, samplesPerPixel);

        var useHyper = compression == TiffCompression.DeflateHyper;
        var compressedTile = ZopfliDeflater.Compress(tileData, useHyper, zopfliIterations);
        tiff.WriteRawTile(tileIndex, compressedTile, compressedTile.Length);
      } else {
        tiff.WriteEncodedTile(tileIndex, tileData, tileData.Length);
      }

      ++tileIndex;
    }
  }

  private static void _ApplyHorizontalDifferencing(byte[] data, int bytesPerRow, int rows, int samplesPerPixel) {
    for (var row = 0; row < rows; ++row) {
      var rowStart = row * bytesPerRow;
      // Process right to left to avoid overwriting
      for (var x = bytesPerRow - 1; x >= samplesPerPixel; --x)
        data[rowStart + x] = (byte)(data[rowStart + x] - data[rowStart + x - samplesPerPixel]);
    }
  }

  private static BitMiracle.LibTiff.Classic.Compression _MapCompression(TiffCompression compression) {
    return compression switch {
      TiffCompression.None => BitMiracle.LibTiff.Classic.Compression.NONE,
      TiffCompression.PackBits => BitMiracle.LibTiff.Classic.Compression.PACKBITS,
      TiffCompression.Lzw => BitMiracle.LibTiff.Classic.Compression.LZW,
      TiffCompression.Deflate => BitMiracle.LibTiff.Classic.Compression.DEFLATE,
      TiffCompression.DeflateUltra => BitMiracle.LibTiff.Classic.Compression.DEFLATE,
      TiffCompression.DeflateHyper => BitMiracle.LibTiff.Classic.Compression.DEFLATE,
      _ => BitMiracle.LibTiff.Classic.Compression.NONE
    };
  }
}
