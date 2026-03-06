using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace FileFormat.Png;

/// <summary>Reads and parses PNG files</summary>
public static class PngReader {

  /// <summary>Read a PNG file from disk</summary>
  public static PngFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PNG file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  /// <summary>Read a PNG from a stream</summary>
  public static PngFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  /// <summary>Read a PNG from a byte array</summary>
  public static PngFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < 8 + 25)
      throw new InvalidDataException("Data too short to be a valid PNG file.");

    var sig = PngSignatureHeader.ReadFrom(data);
    if (!sig.IsValid)
      throw new InvalidDataException("Invalid PNG signature.");

    var offset = PngSignatureHeader.StructSize;
    int width = 0, height = 0, bitDepth = 0;
    var colorType = PngColorType.RGB;
    var interlaceMethod = PngInterlaceMethod.None;
    byte[]? palette = null;
    var paletteCount = 0;
    byte[]? tRNS = null;
    var idatChunks = new List<byte[]>();
    var chunksBeforePlte = new List<PngChunk>();
    var chunksBetweenPlteAndIdat = new List<PngChunk>();
    var chunksAfterIdat = new List<PngChunk>();
    var seenPlte = false;
    var seenIdat = false;

    while (offset + PngChunkHeader.StructSize <= data.Length) {
      var chunkHeader = PngChunkHeader.ReadFrom(data.AsSpan(offset));
      var chunkLength = chunkHeader.Length;
      var chunkType = chunkHeader.Type;
      offset += PngChunkHeader.StructSize;

      if (chunkLength < 0 || offset + chunkLength + 4 > data.Length)
        break;

      var chunkData = data.AsSpan(offset, chunkLength);

      switch (chunkType) {
        case "IHDR":
          var ihdr = PngIhdr.ReadFrom(chunkData);
          width = ihdr.Width;
          height = ihdr.Height;
          bitDepth = ihdr.BitDepth;
          colorType = (PngColorType)ihdr.ColorType;
          interlaceMethod = (PngInterlaceMethod)ihdr.InterlaceMethod;
          break;
        case "PLTE":
          seenPlte = true;
          paletteCount = chunkLength / 3;
          palette = chunkData.ToArray();
          break;
        case "tRNS":
          tRNS = chunkData.ToArray();
          break;
        case "IDAT":
          seenIdat = true;
          idatChunks.Add(chunkData.ToArray());
          break;
        case "IEND":
          break;
        default:
          if (char.IsLower(chunkType[0])) {
            var chunk = new PngChunk(chunkType, chunkData.ToArray());
            if (!seenPlte && !seenIdat)
              chunksBeforePlte.Add(chunk);
            else if (!seenIdat)
              chunksBetweenPlteAndIdat.Add(chunk);
            else
              chunksAfterIdat.Add(chunk);
          }

          break;
      }

      offset += chunkLength;
      offset += 4;

      if (chunkType == "IEND")
        break;
    }

    byte[][]? pixelData = null;
    if (idatChunks.Count > 0 && width > 0 && height > 0) {
      var compressedData = _ConcatenateIdatChunks(idatChunks);
      pixelData = _DecompressAndDefilter(compressedData, width, height, bitDepth, colorType, interlaceMethod);
    }

    return new PngFile {
      Width = width,
      Height = height,
      BitDepth = bitDepth,
      ColorType = colorType,
      InterlaceMethod = interlaceMethod,
      PixelData = pixelData,
      Palette = palette,
      PaletteCount = paletteCount,
      Transparency = tRNS,
      ChunksBeforePlte = chunksBeforePlte.Count > 0 ? chunksBeforePlte : null,
      ChunksBetweenPlteAndIdat = chunksBetweenPlteAndIdat.Count > 0 ? chunksBetweenPlteAndIdat : null,
      ChunksAfterIdat = chunksAfterIdat.Count > 0 ? chunksAfterIdat : null
    };
  }

  private static byte[] _ConcatenateIdatChunks(List<byte[]> chunks) {
    var totalLength = 0;
    foreach (var chunk in chunks)
      totalLength += chunk.Length;

    var result = new byte[totalLength];
    var offset = 0;
    foreach (var chunk in chunks) {
      Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
      offset += chunk.Length;
    }

    return result;
  }

  private static byte[][] _DecompressAndDefilter(byte[] compressedData, int width, int height, int bitDepth,
    PngColorType colorType, PngInterlaceMethod interlaceMethod) {
    byte[] rawData;
    using (var input = new MemoryStream(compressedData))
    using (var zlib = new ZLibStream(input, CompressionMode.Decompress))
    using (var output = new MemoryStream()) {
      zlib.CopyTo(output);
      rawData = output.ToArray();
    }

    if (interlaceMethod == PngInterlaceMethod.Adam7)
      return _DefilterAdam7(rawData, width, height, bitDepth, colorType);

    return _DefilterNonInterlaced(rawData, width, height, bitDepth, colorType);
  }

  private static byte[][] _DefilterNonInterlaced(byte[] rawData, int width, int height, int bitDepth,
    PngColorType colorType) {
    var samplesPerPixel = _GetSamplesPerPixel(colorType);
    var bitsPerPixel = bitDepth * samplesPerPixel;
    var bytesPerScanline = (width * bitsPerPixel + 7) / 8;
    var filterStride = Math.Max(1, samplesPerPixel * bitDepth / 8);

    var result = new byte[height][];
    var offset = 0;

    byte[]? previousRow = null;
    for (var y = 0; y < height; ++y) {
      if (offset >= rawData.Length)
        break;

      var filterByte = rawData[offset++];
      var filteredRow = new byte[bytesPerScanline];
      var available = Math.Min(bytesPerScanline, rawData.Length - offset);
      Buffer.BlockCopy(rawData, offset, filteredRow, 0, available);
      offset += bytesPerScanline;

      result[y] = _ReverseFilter((PngFilterType)filterByte, filteredRow, previousRow, filterStride);
      previousRow = result[y];
    }

    return result;
  }

  private static byte[][] _DefilterAdam7(byte[] rawData, int width, int height, int bitDepth,
    PngColorType colorType) {
    var samplesPerPixel = _GetSamplesPerPixel(colorType);
    var bitsPerPixel = bitDepth * samplesPerPixel;
    var filterStride = Math.Max(1, samplesPerPixel * bitDepth / 8);

    var bytesPerFullScanline = (width * bitsPerPixel + 7) / 8;
    var result = new byte[height][];
    for (var y = 0; y < height; ++y)
      result[y] = new byte[bytesPerFullScanline];

    var offset = 0;
    for (var pass = 0; pass < Adam7.PassCount; ++pass) {
      var (subW, subH) = Adam7.GetPassDimensions(pass, width, height);
      if (subW == 0 || subH == 0)
        continue;

      var subBytesPerScanline = (subW * bitsPerPixel + 7) / 8;
      byte[]? previousRow = null;

      for (var sy = 0; sy < subH; ++sy) {
        if (offset >= rawData.Length)
          break;

        var filterByte = rawData[offset++];
        var filteredRow = new byte[subBytesPerScanline];
        var available = Math.Min(subBytesPerScanline, rawData.Length - offset);
        Buffer.BlockCopy(rawData, offset, filteredRow, 0, available);
        offset += subBytesPerScanline;

        var defiltered = _ReverseFilter((PngFilterType)filterByte, filteredRow, previousRow, filterStride);
        previousRow = defiltered;

        var destY = Adam7.YStart(pass) + sy * Adam7.YStep(pass);
        if (bitDepth >= 8) {
          var bytesPerPixel = samplesPerPixel * (bitDepth / 8);
          for (var sx = 0; sx < subW; ++sx) {
            var destX = Adam7.XStart(pass) + sx * Adam7.XStep(pass);
            Buffer.BlockCopy(defiltered, sx * bytesPerPixel, result[destY], destX * bytesPerPixel, bytesPerPixel);
          }
        } else {
          var pixelsPerByte = 8 / bitDepth;
          var mask = (1 << bitDepth) - 1;
          for (var sx = 0; sx < subW; ++sx) {
            var destX = Adam7.XStart(pass) + sx * Adam7.XStep(pass);
            var srcByteIdx = sx / pixelsPerByte;
            var srcBitPos = sx % pixelsPerByte;
            var srcShift = 8 - bitDepth * (srcBitPos + 1);
            var value = (defiltered[srcByteIdx] >> srcShift) & mask;

            var destByteIdx = destX / pixelsPerByte;
            var destBitPos = destX % pixelsPerByte;
            var destShift = 8 - bitDepth * (destBitPos + 1);
            result[destY][destByteIdx] = (byte)((result[destY][destByteIdx] & ~(mask << destShift)) | ((value & mask) << destShift));
          }
        }
      }
    }

    return result;
  }

  private static byte[] _ReverseFilter(PngFilterType filterType, byte[] filtered, byte[]? previousRow, int stride) {
    var result = new byte[filtered.Length];

    switch (filterType) {
      case PngFilterType.None:
        Buffer.BlockCopy(filtered, 0, result, 0, filtered.Length);
        break;
      case PngFilterType.Sub:
        for (var i = 0; i < filtered.Length; ++i)
          result[i] = (byte)(filtered[i] + (i >= stride ? result[i - stride] : 0));
        break;
      case PngFilterType.Up:
        for (var i = 0; i < filtered.Length; ++i)
          result[i] = (byte)(filtered[i] + (previousRow != null ? previousRow[i] : 0));
        break;
      case PngFilterType.Average:
        for (var i = 0; i < filtered.Length; ++i) {
          var left = i >= stride ? result[i - stride] : 0;
          var above = previousRow != null ? previousRow[i] : 0;
          result[i] = (byte)(filtered[i] + ((left + above) >> 1));
        }
        break;
      case PngFilterType.Paeth:
        for (var i = 0; i < filtered.Length; ++i) {
          var a = i >= stride ? result[i - stride] : 0;
          var b = previousRow != null ? previousRow[i] : 0;
          var c = i >= stride && previousRow != null ? previousRow[i - stride] : 0;
          var p = a + b - c;
          var pa = Math.Abs(p - a);
          var pb = Math.Abs(p - b);
          var pc = Math.Abs(p - c);
          result[i] = (byte)(filtered[i] + (pa <= pb && pa <= pc ? a : pb <= pc ? b : c));
        }
        break;
    }

    return result;
  }

  private static int _GetSamplesPerPixel(PngColorType colorType) => colorType switch {
    PngColorType.Grayscale => 1,
    PngColorType.GrayscaleAlpha => 2,
    PngColorType.RGB => 3,
    PngColorType.RGBA => 4,
    PngColorType.Palette => 1,
    _ => throw new ArgumentException($"Invalid color type: {colorType}")
  };
}
