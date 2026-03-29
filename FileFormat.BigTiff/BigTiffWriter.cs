using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.BigTiff;

/// <summary>Assembles BigTIFF byte streams from in-memory representations.</summary>
public static class BigTiffWriter {

  /// <summary>Number of IFD entries we write.</summary>
  private const int _IFD_ENTRY_COUNT = 9;

  /// <summary>Size of each IFD entry: 2 (tag) + 2 (type) + 8 (count) + 8 (value/offset) = 20 bytes.</summary>
  private const int _IFD_ENTRY_SIZE = 20;

  /// <summary>Header size: 2 (byte order) + 2 (version) + 2 (offset size) + 2 (reserved) + 8 (first IFD offset) = 16 bytes.</summary>
  private const int _HEADER_SIZE = 16;

  /// <summary>IFD size: 8 (entry count) + entries + 8 (next IFD offset).</summary>
  private static readonly int _IfdSize = 8 + _IFD_ENTRY_COUNT * _IFD_ENTRY_SIZE + 8;

  public static byte[] ToBytes(BigTiffFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Pages.Count == 0)
      return _AssembleSinglePage(file);

    return _AssembleMultiPage(file);
  }

  private static byte[] _AssembleSinglePage(BigTiffFile file) {
    var samplesPerPixel = file.SamplesPerPixel;
    var bytesPerSample = file.BitsPerSample > 8 ? 2 : 1;
    var pixelDataSize = file.Width * file.Height * samplesPerPixel * bytesPerSample;

    // Layout: header (16) | IFD | pixel data
    var ifdOffset = (ulong)_HEADER_SIZE;
    var pixelDataOffset = (ulong)(_HEADER_SIZE + _IfdSize);

    var totalSize = (int)pixelDataOffset + pixelDataSize;
    var result = new byte[totalSize];
    var span = result.AsSpan();

    _WriteHeader(span, ifdOffset);
    _WriteIfd(span[(int)ifdOffset..], file.Width, file.Height, file.SamplesPerPixel,
      file.BitsPerSample, file.PhotometricInterpretation, pixelDataOffset, 0);

    var copyLen = Math.Min(file.PixelData.Length, pixelDataSize);
    file.PixelData.AsSpan(0, copyLen).CopyTo(result.AsSpan((int)pixelDataOffset));

    return result;
  }

  private static byte[] _AssembleMultiPage(BigTiffFile file) {
    using var ms = new MemoryStream();

    // Reserve header space
    ms.Write(new byte[_HEADER_SIZE]);

    var allPages = new List<(int Width, int Height, int Spp, int Bps, ushort Photo, byte[] Pixels)> {
      (file.Width, file.Height, file.SamplesPerPixel, file.BitsPerSample, file.PhotometricInterpretation, file.PixelData)
    };
    foreach (var p in file.Pages)
      allPages.Add((p.Width, p.Height, p.SamplesPerPixel, p.BitsPerSample, p.PhotometricInterpretation, p.PixelData));

    var ifdOffsets = new List<long>();

    for (var pageIdx = 0; pageIdx < allPages.Count; ++pageIdx) {
      var (w, h, spp, bps, photo, pixels) = allPages[pageIdx];
      var bytesPerSample = bps > 8 ? 2 : 1;
      var pixelDataSize = w * h * spp * bytesPerSample;

      var ifdStart = ms.Position;
      ifdOffsets.Add(ifdStart);

      // Write IFD placeholder
      var ifdBytes = new byte[_IfdSize + pixelDataSize];
      ms.Write(ifdBytes);

      // Now go back and fill in the IFD
      var pxOffset = (ulong)(ifdStart + _IfdSize);

      var span = ifdBytes.AsSpan();
      _WriteIfd(span, w, h, spp, bps, photo, pxOffset, 0);

      var copyLen = Math.Min(pixels.Length, pixelDataSize);
      pixels.AsSpan(0, copyLen).CopyTo(span[_IfdSize..]);

      // Write back
      var currentPos = ms.Position;
      ms.Position = ifdStart;
      ms.Write(ifdBytes);
      ms.Position = currentPos;
    }

    var result = ms.ToArray();

    // Patch header
    _WriteHeader(result.AsSpan(), (ulong)ifdOffsets[0]);

    // Patch next IFD offsets
    for (var i = 0; i < ifdOffsets.Count; ++i) {
      var nextIfd = i + 1 < ifdOffsets.Count ? (ulong)ifdOffsets[i + 1] : 0UL;
      var nextIfdPos = (int)ifdOffsets[i] + 8 + _IFD_ENTRY_COUNT * _IFD_ENTRY_SIZE;
      BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(nextIfdPos), nextIfd);
    }

    return result;
  }

  internal static byte[] Assemble(BigTiffFile file) => _AssembleSinglePage(file);

  private static void _WriteHeader(Span<byte> span, ulong ifdOffset) {
    var header = new BigTiffFileHeader(0x4949, BigTiffFile.Version, BigTiffFile.OffsetSize, 0, (long)ifdOffset);
    header.WriteTo(span);
  }

  private static void _WriteIfd(Span<byte> span, int width, int height, int samplesPerPixel,
    int bitsPerSample, ushort photometric, ulong pixelDataOffset, ulong nextIfdOffset) {
    var bytesPerSample = bitsPerSample > 8 ? 2 : 1;
    var pixelDataSize = (ulong)(width * height * samplesPerPixel * bytesPerSample);
    var pos = 0;

    // Entry count (uint64)
    BinaryPrimitives.WriteUInt64LittleEndian(span[pos..], _IFD_ENTRY_COUNT);
    pos += 8;

    // Tag 256: ImageWidth (LONG)
    _WriteEntry(span[pos..], BigTiffFile.TagImageWidth, BigTiffFile.TypeLong, 1, (ulong)width);
    pos += _IFD_ENTRY_SIZE;

    // Tag 257: ImageLength (LONG)
    _WriteEntry(span[pos..], BigTiffFile.TagImageLength, BigTiffFile.TypeLong, 1, (ulong)height);
    pos += _IFD_ENTRY_SIZE;

    // Tag 258: BitsPerSample (SHORT) — pack inline (up to 4 SHORTs fit in 8-byte value field)
    var bpsValue = 0UL;
    for (var i = 0; i < samplesPerPixel && i < 4; ++i)
      bpsValue |= (ulong)(ushort)bitsPerSample << (i * 16);
    _WriteEntry(span[pos..], BigTiffFile.TagBitsPerSample, BigTiffFile.TypeShort, (ulong)samplesPerPixel, bpsValue);
    pos += _IFD_ENTRY_SIZE;

    // Tag 259: Compression (SHORT) = 1 (None)
    _WriteEntry(span[pos..], BigTiffFile.TagCompression, BigTiffFile.TypeShort, 1, BigTiffFile.CompressionNone);
    pos += _IFD_ENTRY_SIZE;

    // Tag 262: PhotometricInterpretation (SHORT)
    _WriteEntry(span[pos..], BigTiffFile.TagPhotometricInterpretation, BigTiffFile.TypeShort, 1, photometric);
    pos += _IFD_ENTRY_SIZE;

    // Tag 273: StripOffsets (LONG8)
    _WriteEntry(span[pos..], BigTiffFile.TagStripOffsets, BigTiffFile.TypeLong8, 1, pixelDataOffset);
    pos += _IFD_ENTRY_SIZE;

    // Tag 277: SamplesPerPixel (SHORT)
    _WriteEntry(span[pos..], BigTiffFile.TagSamplesPerPixel, BigTiffFile.TypeShort, 1, (ulong)samplesPerPixel);
    pos += _IFD_ENTRY_SIZE;

    // Tag 278: RowsPerStrip (LONG)
    _WriteEntry(span[pos..], BigTiffFile.TagRowsPerStrip, BigTiffFile.TypeLong, 1, (ulong)height);
    pos += _IFD_ENTRY_SIZE;

    // Tag 279: StripByteCounts (LONG8)
    _WriteEntry(span[pos..], BigTiffFile.TagStripByteCounts, BigTiffFile.TypeLong8, 1, pixelDataSize);
    pos += _IFD_ENTRY_SIZE;

    // Next IFD offset
    BinaryPrimitives.WriteUInt64LittleEndian(span[pos..], nextIfdOffset);
  }

  private static void _WriteEntry(Span<byte> span, ushort tag, ushort type, ulong count, ulong value) {
    BinaryPrimitives.WriteUInt16LittleEndian(span, tag);
    BinaryPrimitives.WriteUInt16LittleEndian(span[2..], type);
    BinaryPrimitives.WriteUInt64LittleEndian(span[4..], count);
    BinaryPrimitives.WriteUInt64LittleEndian(span[12..], value);
  }
}
