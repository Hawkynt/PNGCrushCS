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

    // The packed form says DGC where this says DGU, and it is not accepted here.
    //
    // Not because the tag is hard to allow — it is one line — but because the unpacking below does
    // not decode it. Letting the tag through was tried: the .dc1 sample then decodes to 320 by 200
    // and agrees with the reference decoder on 0.04 per cent of its pixels, which is a picture
    // manufactured rather than read. The run-length scheme written here, an escape of 0x00 followed
    // by a count and a value, is not the one those files use.
    //
    // A refusal that names the reason is worth more than a decode that is wrong, so the tag stays
    // out until somebody has the scheme.
    if (!DuneGraphFile.TryReadHeader(data, out _, out _))
      throw new InvalidDataException("Not a DuneGraph file: missing the 'DGU' tag (a packed one says 'DGC', which is not decoded here).");

    if (data.Length < DuneGraphFile.HeaderSize + DuneGraphFile.PaletteDataSize + 1)
      throw new InvalidDataException($"Data too small for a valid DuneGraph file (minimum {DuneGraphFile.HeaderSize + DuneGraphFile.PaletteDataSize + 1} bytes, got {data.Length}).");

    // Convert Falcon palette to RGB
    var rgbPalette = new byte[DuneGraphFile.PaletteEntryCount * 3];
    DuneGraphFile.ConvertFalconPaletteToRgb(data.Slice(DuneGraphFile.HeaderSize, DuneGraphFile.PaletteDataSize), rgbPalette);

    var pixelSection = data.Slice(DuneGraphFile.HeaderSize + DuneGraphFile.PaletteDataSize);
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
    return FromSpan(data);
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
