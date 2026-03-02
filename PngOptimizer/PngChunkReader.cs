using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace PngOptimizer;

/// <summary>
///   Reads PNG chunks from a byte stream and categorizes ancillary chunks
///   by their insertion point relative to PLTE and IDAT.
/// </summary>
internal sealed class PngChunkReader {
  // Critical chunk types we must never preserve
  private static readonly HashSet<string> _CriticalChunks = ["IHDR", "PLTE", "IDAT", "IEND"];

  // Chunks that belong before PLTE
  private static readonly HashSet<string> _BeforePlteChunks = ["gAMA", "cHRM", "sRGB", "iCCP", "sBIT"];

  // Chunks that belong after IDAT
  private static readonly HashSet<string> _AfterIdatChunks = ["tEXt", "zTXt", "iTXt", "tIME"];

  /// <summary>Chunks to insert before PLTE (gAMA, cHRM, sRGB, iCCP, sBIT)</summary>
  public List<PngChunk> BeforePlte { get; } = [];

  /// <summary>Chunks to insert between PLTE and IDAT (hIST, bKGD, pHYs, sPLT)</summary>
  public List<PngChunk> BetweenPlteAndIdat { get; } = [];

  /// <summary>Chunks to insert after IDAT (tEXt, zTXt, iTXt, tIME)</summary>
  public List<PngChunk> AfterIdat { get; } = [];

  /// <summary>Whether any ancillary chunks were found.</summary>
  public bool HasChunks => this.BeforePlte.Count > 0 || this.BetweenPlteAndIdat.Count > 0 || this.AfterIdat.Count > 0;

  /// <summary>Parse PNG byte stream and extract ancillary chunks.</summary>
  public static PngChunkReader Parse(byte[] pngBytes) {
    var reader = new PngChunkReader();

    if (pngBytes.Length < 8)
      return reader;

    // Skip PNG signature (8 bytes)
    var offset = 8;
    var seenPlte = false;
    var seenIdat = false;

    while (offset + 8 <= pngBytes.Length) {
      var length = BinaryPrimitives.ReadInt32BigEndian(pngBytes.AsSpan(offset));
      offset += 4;

      if (offset + 4 > pngBytes.Length)
        break;

      var type = Encoding.ASCII.GetString(pngBytes, offset, 4);
      offset += 4;

      if (length < 0 || offset + length + 4 > pngBytes.Length)
        break;

      var data = new byte[length];
      if (length > 0)
        Array.Copy(pngBytes, offset, data, 0, length);
      offset += length;

      // Skip CRC (4 bytes)
      offset += 4;

      // Track critical chunk ordering
      if (type == "PLTE")
        seenPlte = true;
      else if (type == "IDAT")
        seenIdat = true;

      // Skip critical chunks
      if (_CriticalChunks.Contains(type))
        continue;

      // Skip tRNS — we regenerate it ourselves
      if (type == "tRNS")
        continue;

      // Ancillary chunks have lowercase first letter
      if (char.IsUpper(type[0]))
        continue;

      // Categorize by insertion point
      if (_BeforePlteChunks.Contains(type))
        reader.BeforePlte.Add(new PngChunk(type, data));
      else if (_AfterIdatChunks.Contains(type))
        reader.AfterIdat.Add(new PngChunk(type, data));
      else if (!seenIdat)
        reader.BetweenPlteAndIdat.Add(new PngChunk(type, data));
      else
        reader.AfterIdat.Add(new PngChunk(type, data));

      if (type == "IEND")
        break;
    }

    return reader;
  }

  /// <summary>A parsed PNG chunk with its type and data.</summary>
  internal readonly record struct PngChunk(string Type, byte[] Data);
}
