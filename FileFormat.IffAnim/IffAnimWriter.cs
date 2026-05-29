using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Iff;
using FileFormat.Ilbm;

namespace FileFormat.IffAnim;

/// <summary>Assembles IFF ANIM file bytes from an <see cref="IffAnimFile"/>.</summary>
public static class IffAnimWriter {

  public static byte[] ToBytes(IffAnimFile file) {
    ArgumentNullException.ThrowIfNull(file);

    // Pre-quantize to <=256 unique colors so IlbmFile.FromRawImage's RGB24 path doesn't throw.
    var quantized = _QuantizeToIndexed(file.PixelData, file.Width, file.Height);
    var rawImage = new RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = quantized.Indices,
      Palette = quantized.Palette,
      PaletteCount = quantized.PaletteCount,
    };
    var ilbmFile = IlbmFile.FromRawImage(rawImage);
    var ilbmBytes = IlbmWriter.ToBytes(ilbmFile);

    // Wrap in FORM ANIM: "FORM" + uint32 BE (4 + ilbmBytes.Length) + "ANIM" + ilbmBytes
    var formDataSize = 4 + ilbmBytes.Length; // "ANIM" + embedded ILBM
    var totalSize = 8 + formDataSize;        // "FORM" + size + data

    using var ms = new MemoryStream(totalSize);

    _WriteChunkHeader(ms, "FORM", formDataSize);
    ms.Write("ANIM"u8);
    ms.Write(ilbmBytes);

    return ms.ToArray();
  }

  private static void _WriteChunkHeader(Stream stream, string chunkId, int size) {
    Span<byte> buffer = stackalloc byte[IffChunkHeader.StructSize];
    new Riff.FourCC(chunkId).WriteTo(buffer);
    System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(buffer[4..], size);
    stream.Write(buffer);
  }

  /// <summary>Quantizes RGB24 pixel data to at most 256 unique colours by dropping low bits when over budget.</summary>
  private static (byte[] Indices, byte[] Palette, int PaletteCount) _QuantizeToIndexed(byte[] rgb24, int width, int height) {
    var pixelCount = width * height;
    for (var shift = 0; shift < 8; ++shift) {
      var mask = (byte)(0xFF << shift);
      var map = new System.Collections.Generic.Dictionary<int, byte>(256);
      var indices = new byte[pixelCount];
      var overflow = false;
      for (var i = 0; i < pixelCount; ++i) {
        var r = (byte)(rgb24[i * 3] & mask);
        var g = (byte)(rgb24[i * 3 + 1] & mask);
        var b = (byte)(rgb24[i * 3 + 2] & mask);
        var key = (r << 16) | (g << 8) | b;
        if (!map.TryGetValue(key, out var idx)) {
          if (map.Count >= 256) { overflow = true; break; }
          idx = (byte)map.Count;
          map[key] = idx;
        }
        indices[i] = idx;
      }
      if (overflow) continue;
      var palette = new byte[map.Count * 3];
      foreach (var kv in map) {
        palette[kv.Value * 3] = (byte)(kv.Key >> 16);
        palette[kv.Value * 3 + 1] = (byte)((kv.Key >> 8) & 0xFF);
        palette[kv.Value * 3 + 2] = (byte)(kv.Key & 0xFF);
      }
      return (indices, palette, map.Count);
    }
    // Fallback: collapse to single colour (should not happen for normal images).
    return (new byte[pixelCount], [0, 0, 0], 1);
  }
}
