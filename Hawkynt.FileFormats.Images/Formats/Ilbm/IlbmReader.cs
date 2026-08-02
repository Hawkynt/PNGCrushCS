using System;
using FileFormat.Core;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.Ilbm;

/// <summary>Reads IFF ILBM files from bytes, streams, or file paths.</summary>
public static class IlbmReader {

  private const int _MIN_IFF_SIZE = 12; // "FORM" + size + form type

  public static IlbmFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ILBM file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IlbmFile FromStream(Stream stream) {
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

  /// <summary>
  /// Expands a PCHG chunk into one palette a scanline, or null when there is none to expand.
  /// </summary>
  /// <remarks>
  /// Only the uncompressed twelve-bit form is read. The other two — a Huffman-coded one and a
  /// thirty-two-bit one — are refused by returning nothing rather than half-decoded, which would put
  /// the wrong colours on the picture and look like a decoding fault somewhere else.
  /// </remarks>
  private static byte[]? _ReadPaletteChanges(byte[]? pchg, byte[]? cmap, int height) {
    const int HEADER_SIZE = 20;
    const int TWELVE_BIT = 0x0001;

    if (pchg == null || pchg.Length < HEADER_SIZE || cmap == null)
      return null;

    var compression = BinaryPrimitives.ReadUInt16BigEndian(pchg);
    var flags = BinaryPrimitives.ReadUInt16BigEndian(pchg.AsSpan(2));
    if (compression != 0 || (flags & TWELVE_BIT) == 0)
      return null;

    var startLine = BinaryPrimitives.ReadInt16BigEndian(pchg.AsSpan(4));
    var lineCount = BinaryPrimitives.ReadUInt16BigEndian(pchg.AsSpan(6));

    var entries = IlbmFile.SlicedPaletteEntries;
    var current = new byte[entries * 3];
    cmap.AsSpan(0, Math.Min(cmap.Length, current.Length)).CopyTo(current);

    var result = new byte[height * entries * 3];
    var maskWords = (lineCount + 31) / 32 * 4;
    var at = HEADER_SIZE + maskWords;

    for (var y = 0; y < height; ++y) {
      var line = y - startLine;
      if (line >= 0 && line < lineCount) {
        var maskAt = HEADER_SIZE + (line >> 3);
        var changes = maskAt < pchg.Length && (pchg[maskAt] >> (7 - (line & 7)) & 1) != 0;
        if (changes && at + 2 <= pchg.Length) {
          int small = pchg[at], big = pchg[at + 1];
          at += 2;
          for (var i = 0; i < small + big && at + 2 <= pchg.Length; ++i, at += 2) {
            var change = BinaryPrimitives.ReadUInt16BigEndian(pchg.AsSpan(at));
            var register = (change >> 12) + (i < small ? 0 : entries);
            if (register >= entries)
              continue;

            current[register * 3] = (byte)((change >> 8 & 0x0F) * 0x11);
            current[register * 3 + 1] = (byte)((change >> 4 & 0x0F) * 0x11);
            current[register * 3 + 2] = (byte)((change & 0x0F) * 0x11);
          }
        }
      }

      current.CopyTo(result, y * entries * 3);
    }

    return result;
  }

  public static IlbmFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static IlbmFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _MIN_IFF_SIZE)
      throw new InvalidDataException("Data too small for a valid IFF ILBM file.");

    // Validate FORM magic
    var formId = Encoding.ASCII.GetString(data.Slice(0, 4));
    if (formId != "FORM")
      throw new InvalidDataException($"Invalid IFF magic: expected 'FORM', got '{formId}'.");

    // Validate ILBM form type
    var formType = Encoding.ASCII.GetString(data.Slice(8, 4));
    if (formType != "ILBM")
      throw new InvalidDataException($"Invalid IFF form type: expected 'ILBM', got '{formType}'.");

    var formSize = BinaryPrimitives.ReadInt32BigEndian(data[4..]);

    // Parse chunks
    BmhdChunk? bmhd = null;
    byte[]? cmap = null;
    byte[]? body = null;
    uint camg = 0;

    byte[]? scanlinePalettes = null;
    byte[]? pchg = null;
    var offset = 12; // skip FORM header + form type
    var endOffset = Math.Min(8 + formSize, data.Length);

    while (offset + 8 <= endOffset) {
      var chunkId = Encoding.ASCII.GetString(data.Slice(offset, 4));
      var chunkSize = BinaryPrimitives.ReadInt32BigEndian(data[(offset + 4)..]);
      var chunkDataOffset = offset + 8;

      if (chunkDataOffset + chunkSize > data.Length)
        break;

      switch (chunkId) {
        case "BMHD":
          if (chunkSize >= BmhdChunk.StructSize)
            bmhd = BmhdChunk.ReadFrom(data[chunkDataOffset..]);
          break;
        case "CMAP":
          cmap = new byte[chunkSize];
          data.Slice(chunkDataOffset, chunkSize).CopyTo(cmap);
          AmigaColourMap.WidenIfFourBit(cmap);
          break;
        case "CAMG":
          if (chunkSize >= 4)
            camg = BinaryPrimitives.ReadUInt32BigEndian(data[chunkDataOffset..]);
          break;
        // Sliced HAM: a version word, then sixteen twelve-bit colours for each scanline. Without it
        // a SHAM picture is decoded against the one CMAP and drifts further out with every line.
        case "SHAM":
        case "CTBL":
        case "BEAM": {
          // The same table under three names; only SHAM puts a version word in front of it.
          var start = chunkId == "SHAM" ? 2 : 0;
          var words = (chunkSize - start) / 2;
          if (words < IlbmFile.SlicedPaletteEntries)
            break;

          scanlinePalettes = new byte[words * 3];
          for (var i = 0; i < words; ++i) {
            var colour = BinaryPrimitives.ReadUInt16BigEndian(data[(chunkDataOffset + start + i * 2)..]);
            scanlinePalettes[i * 3] = (byte)((colour >> 8 & 0x0F) * 0x11);
            scanlinePalettes[i * 3 + 1] = (byte)((colour >> 4 & 0x0F) * 0x11);
            scanlinePalettes[i * 3 + 2] = (byte)((colour & 0x0F) * 0x11);
          }

          break;
        }
        case "PCHG":
          pchg = new byte[chunkSize];
          data.Slice(chunkDataOffset, chunkSize).CopyTo(pchg);
          break;
        case "BODY":
          body = new byte[chunkSize];
          data.Slice(chunkDataOffset, chunkSize).CopyTo(body);
          break;
      }

      // Advance to next chunk (2-byte aligned)
      offset = chunkDataOffset + chunkSize + (chunkSize & 1);
    }

    if (bmhd == null)
      throw new InvalidDataException("ILBM file missing required BMHD chunk.");

    if (body == null)
      throw new InvalidDataException("ILBM file missing required BODY chunk.");

    var header = bmhd.Value;
    var width = header.Width;
    var height = header.Height;
    var numPlanes = header.NumPlanes;
    var compression = (IlbmCompression)header.Compression;

    // Decompress BODY if needed
    var bytesPerPlaneRow = ((width + 15) / 16) * 2;
    var bytesPerScanline = bytesPerPlaneRow * numPlanes;
    var expectedPlanarSize = bytesPerScanline * height;

    var planarData = compression == IlbmCompression.ByteRun1
      ? ByteRun1Compressor.Decode(body, expectedPlanarSize)
      : body;

    // Convert planar to chunky
    var pixelData = PlanarConverter.PlanarToChunky(planarData, width, height, numPlanes);

    // PCHG states the palette as changes rather than in full: a bitmap of which lines change, then
    // for each of those the registers it sets. It is read after the loop because it builds on CMAP,
    // which is not required to come first.
    scanlinePalettes ??= _ReadPaletteChanges(pchg, cmap, height);

    return new IlbmFile {
      Width = width,
      Height = height,
      NumPlanes = numPlanes,
      Compression = compression,
      Masking = (IlbmMasking)header.Masking,
      TransparentColor = header.TransparentColor,
      XAspect = header.XAspect,
      YAspect = header.YAspect,
      PageWidth = header.PageWidth,
      PageHeight = header.PageHeight,
      PixelData = pixelData,
      Palette = cmap,
      ScanlinePalettes = scanlinePalettes,
      ViewportMode = camg
    };
  }

}
