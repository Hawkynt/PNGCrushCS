using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.AppleIIgs;

/// <summary>Reads Apple IIGS Super Hi-Res ($C1) files from bytes, streams, or file paths.</summary>
public static class AppleIIgsReader {

  /// <summary>Total file size: 32000 pixel + 200 SCB + 512 palette + 56 padding = 32768 bytes.</summary>
  internal const int FileSize = 32768;

  /// <summary>Pixel data size in bytes.</summary>
  internal const int PixelDataSize = 32000;

  /// <summary>Number of scan control bytes (one per scanline).</summary>
  internal const int ScbSize = 200;

  /// <summary>Palette data size in bytes (16 palettes x 16 colors x 2 bytes).</summary>
  internal const int PaletteSize = 512;

  /// <summary>Number of palette entries (16 palettes x 16 colors).</summary>
  internal const int PaletteEntryCount = 256;

  /// <summary>Padding size in bytes.</summary>
  internal const int PaddingSize = 56;

  /// <summary>Number of scanlines.</summary>
  internal const int LineCount = 200;

  /// <summary>Bytes per scanline.</summary>
  internal const int BytesPerLine = 160;

  /// <summary>SCB bit mask for 640 mode.</summary>
  internal const byte Scb640ModeBit = 0x80;

  public static AppleIIgsFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Apple IIGS file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AppleIIgsFile FromStream(Stream stream) {
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

  public static AppleIIgsFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < FileSize)
      throw new InvalidDataException($"Data too small for a valid Apple IIGS file (expected {FileSize} bytes, got {data.Length}).");

    if (data.Length != FileSize)
      throw new InvalidDataException($"Invalid Apple IIGS file size (expected {FileSize} bytes, got {data.Length}).");

    var offset = 0;

    // Pixel data (32000 bytes)
    var pixelData = new byte[PixelDataSize];
    data.AsSpan(offset, PixelDataSize).CopyTo(pixelData.AsSpan(0));
    offset += PixelDataSize;

    // SCBs (200 bytes)
    var scbs = new byte[ScbSize];
    data.AsSpan(offset, ScbSize).CopyTo(scbs.AsSpan(0));
    offset += ScbSize;

    // Palettes (512 bytes = 256 x 16-bit LE values)
    var palettes = new short[PaletteEntryCount];
    var span = data.AsSpan(offset, PaletteSize);
    for (var i = 0; i < PaletteEntryCount; ++i)
      palettes[i] = BinaryPrimitives.ReadInt16LittleEndian(span[(i * 2)..]);

    // Determine mode from first SCB bit 7
    var mode = (scbs[0] & Scb640ModeBit) != 0 ? AppleIIgsMode.Mode640 : AppleIIgsMode.Mode320;
    var width = mode == AppleIIgsMode.Mode640 ? 640 : 320;

    return new AppleIIgsFile {
      Width = width,
      Height = LineCount,
      Mode = mode,
      PixelData = pixelData,
      Scbs = scbs,
      Palettes = palettes
    };
  }
}
