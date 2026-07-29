using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.Gaf;

/// <summary>Assembles GAF (Total Annihilation) texture archive file bytes from a GafFile model.</summary>
public static class GafWriter {

  /// <summary>GAF magic: version 0x00010100.</summary>
  private const uint _MAGIC = 0x00010100;

  public static byte[] ToBytes(GafFile file) {
    ArgumentNullException.ThrowIfNull(file);

    using var ms = new MemoryStream();
    using var bw = new BinaryWriter(ms);

    // File header (12 bytes)
    bw.Write(_MAGIC);           // version
    bw.Write((uint)1);          // entry_count = 1
    bw.Write((uint)0);          // reserved

    // Entry pointer table (1 entry)
    var entryPointerPos = ms.Position;
    bw.Write((uint)0); // placeholder, will patch

    // Entry header (40 bytes)
    var entryOffset = (uint)ms.Position;
    bw.Write((ushort)1);        // frame_count = 1
    bw.Write((ushort)0);        // unknown
    bw.Write((uint)0);          // unknown

    var nameBytes = new byte[32];
    var name = file.Name;
    if (name is { Length: > 31 })
      name = name[..31];
    var encoded = Encoding.ASCII.GetBytes(name);
    encoded.AsSpan(0, Math.Min(encoded.Length, 31)).CopyTo(nameBytes);
    bw.Write(nameBytes);

    // Frame pointer (1 frame)
    var framePointerPos = ms.Position;
    bw.Write((uint)0); // placeholder, will patch

    // Frame header (20 bytes)
    var frameOffset = (uint)ms.Position;
    bw.Write((ushort)file.Width);
    bw.Write((ushort)file.Height);
    bw.Write(file.XOffset);
    bw.Write(file.YOffset);
    bw.Write(file.TransparencyIndex);
    bw.Write((byte)0);          // compressed = 0 (uncompressed)
    bw.Write((ushort)0);        // subframe_count = 0
    var dataOffsetPos = ms.Position;
    bw.Write((uint)0);          // data_offset placeholder
    bw.Write((uint)0);          // unknown

    // Pixel data (uncompressed)
    var dataOffset = (uint)ms.Position;
    var pixelCount = file.Width * file.Height;
    if (file.PixelData.Length >= pixelCount)
      bw.Write(file.PixelData, 0, pixelCount);
    else {
      bw.Write(file.PixelData);
      bw.Write(new byte[pixelCount - file.PixelData.Length]);
    }

    // Patch offsets
    var result = ms.ToArray();
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan((int)entryPointerPos), entryOffset);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan((int)framePointerPos), frameOffset);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan((int)dataOffsetPos), dataOffset);

    return result;
  }
}
