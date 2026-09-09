using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.AppleIIgs;

/// <summary>Reads Apple IIGS Super Hi-Res ($C1) files from bytes, streams, or file paths.</summary>
public static class AppleIIgsReader {

  /// <summary>Total file size: 32000 pixel + 256 SCB block + 512 palette = 32768 bytes.</summary>
  internal const int FileSize = 32768;

  /// <summary>Pixel data size in bytes.</summary>
  internal const int PixelDataSize = 32000;

  /// <summary>Number of scan control bytes (one per scanline).</summary>
  internal const int ScbSize = 200;

  /// <summary>
  /// The bytes the scan control block occupies, which is more than the scanlines use.
  /// </summary>
  /// <remarks>
  /// A $C1 file is a photograph of the three regions of the IIGS's screen memory, and those regions
  /// sit at fixed addresses: pixels at $2000, the scan control bytes at $9D00, the palettes at
  /// $9E00. Only 200 of the 256 bytes between the last two are scanlines; the other 56 are reserved
  /// and belong where the hardware leaves them, before the palettes rather than after them.
  /// <para/>
  /// Putting them at the end instead shifts every palette 56 bytes down the file, and a decoder
  /// looking where the palettes actually live finds the reserved zeroes. That is what happened: the
  /// picture round-tripped through this package because the reader made the same mistake, and
  /// RECOIL — reading palette 0 at $9E00, as the machine does — rebuilt every $C1 this wrote as a
  /// canvas of black.
  /// </remarks>
  internal const int ScbBlockSize = 256;

  /// <summary>Where the palettes begin: after the whole scan control block.</summary>
  internal const int PaletteOffset = PixelDataSize + ScbBlockSize;

  /// <summary>Palette data size in bytes (16 palettes x 16 colors x 2 bytes).</summary>
  internal const int PaletteSize = 512;

  /// <summary>Number of palette entries (16 palettes x 16 colors).</summary>
  internal const int PaletteEntryCount = 256;

  /// <summary>Number of scanlines.</summary>
  internal const int LineCount = 200;

  /// <summary>Bytes per scanline.</summary>
  internal const int BytesPerLine = 160;

  /// <summary>SCB bit mask for 640 mode.</summary>
  internal const byte Scb640ModeBit = 0x80;

  public static AppleIIgsFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < FileSize)
      throw new InvalidDataException($"Data too small for a valid Apple IIGS file (expected {FileSize} bytes, got {data.Length}).");

    if (data.Length != FileSize)
      throw new InvalidDataException($"Invalid Apple IIGS file size (expected {FileSize} bytes, got {data.Length}).");

    var offset = 0;

    // Pixel data (32000 bytes)
    var pixelData = new byte[PixelDataSize];
    data.Slice(offset, PixelDataSize).CopyTo(pixelData);
    offset += PixelDataSize;

    // SCBs (200 of the 256 bytes the block reserves)
    var scbs = new byte[ScbSize];
    data.Slice(offset, ScbSize).CopyTo(scbs);

    // Palettes (512 bytes = 256 x 16-bit LE values), after the whole block
    var palettes = new short[PaletteEntryCount];
    var paletteSpan = data.Slice(PaletteOffset, PaletteSize);
    for (var i = 0; i < PaletteEntryCount; ++i)
      palettes[i] = BinaryPrimitives.ReadInt16LittleEndian(paletteSpan[(i * 2)..]);

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
    return FromSpan(data);
  }
}
