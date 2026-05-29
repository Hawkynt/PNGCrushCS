using System;
using System.IO;

namespace FileFormat.DuneGraph;

/// <summary>Reads Atari Falcon DuneGraph images from bytes, streams, or file paths.</summary>
public static class DuneGraphReader {

  public static DuneGraphFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("DuneGraph file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DuneGraphFile FromStream(Stream stream) {
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

  public static DuneGraphFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < DuneGraphFile.PaletteDataSize + 1)
      throw new InvalidDataException($"Data too small for a valid DuneGraph file (minimum {DuneGraphFile.PaletteDataSize + 1} bytes, got {data.Length}).");

    // Convert Falcon palette to RGB
    var rgbPalette = new byte[DuneGraphFile.PaletteEntryCount * 3];
    DuneGraphFile.ConvertFalconPaletteToRgb(data.Slice(0, DuneGraphFile.PaletteDataSize), rgbPalette);

    var pixelSection = data.Slice(DuneGraphFile.PaletteDataSize);
    var isUncompressed = data.Length == DuneGraphFile.UncompressedFileSize;

    byte[] pixelData;
    bool isCompressed;

    if (isUncompressed) {
      pixelData = new byte[DuneGraphFile.PixelDataSize];
      pixelSection.Slice(0, DuneGraphFile.PixelDataSize).CopyTo(pixelData);
      isCompressed = false;
    } else {
      pixelData = _DecompressRle(pixelSection);
      isCompressed = true;
    }

    return new DuneGraphFile {
      IsCompressed = isCompressed,
      Palette = rgbPalette,
      PixelData = pixelData,
    };
    }

  public static DuneGraphFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < DuneGraphFile.PaletteDataSize + 1)
      throw new InvalidDataException($"Data too small for a valid DuneGraph file (minimum {DuneGraphFile.PaletteDataSize + 1} bytes, got {data.Length}).");

    // Convert Falcon palette to RGB
    var rgbPalette = new byte[DuneGraphFile.PaletteEntryCount * 3];
    DuneGraphFile.ConvertFalconPaletteToRgb(data.AsSpan(0, DuneGraphFile.PaletteDataSize), rgbPalette);

    var pixelSection = data.AsSpan(DuneGraphFile.PaletteDataSize);
    var isUncompressed = data.Length == DuneGraphFile.UncompressedFileSize;

    byte[] pixelData;
    bool isCompressed;

    if (isUncompressed) {
      pixelData = new byte[DuneGraphFile.PixelDataSize];
      pixelSection.Slice(0, DuneGraphFile.PixelDataSize).CopyTo(pixelData);
      isCompressed = false;
    } else {
      pixelData = _DecompressRle(pixelSection);
      isCompressed = true;
    }

    return new DuneGraphFile {
      IsCompressed = isCompressed,
      Palette = rgbPalette,
      PixelData = pixelData,
    };
  }

  /// <summary>Decompresses DuneGraph RLE: escape byte 0x00 followed by count and value for runs; non-zero bytes are literal.</summary>
  private static byte[] _DecompressRle(ReadOnlySpan<byte> compressed) {
    var result = new byte[DuneGraphFile.PixelDataSize];
    var srcPos = 0;
    var dstPos = 0;

    while (srcPos < compressed.Length && dstPos < result.Length) {
      var current = compressed[srcPos++];
      if (current == DuneGraphFile.RleEscape) {
        if (srcPos + 1 >= compressed.Length)
          break;
        var count = compressed[srcPos++];
        var value = compressed[srcPos++];
        for (var i = 0; i < count && dstPos < result.Length; ++i)
          result[dstPos++] = value;
      } else {
        result[dstPos++] = current;
      }
    }

    return result;
  }
}
