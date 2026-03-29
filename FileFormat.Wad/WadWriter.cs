using System;
using System.IO;

namespace FileFormat.Wad;

/// <summary>Assembles WAD file bytes from a <see cref="WadFile"/>.</summary>
public static class WadWriter {

  public static byte[] ToBytes(WadFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var lumps = file.Lumps;
    var numLumps = lumps.Count;

    // Calculate total lump data size
    var totalDataSize = 0;
    for (var i = 0; i < numLumps; ++i)
      totalDataSize += lumps[i].Data.Length;

    var directoryOffset = WadHeader.StructSize + totalDataSize;
    var totalSize = directoryOffset + numLumps * WadEntry.StructSize;

    var result = new byte[totalSize];
    var span = result.AsSpan();

    // Write header
    var id = file.Type == WadType.Iwad ? "IWAD" : "PWAD";
    var header = new WadHeader((byte)id[0], (byte)id[1], (byte)id[2], (byte)id[3], numLumps, directoryOffset);
    header.WriteTo(span);

    // Write lump data and build directory
    var dataOffset = WadHeader.StructSize;
    for (var i = 0; i < numLumps; ++i) {
      var lump = lumps[i];

      if (lump.Data.Length > 0)
        lump.Data.AsSpan(0, lump.Data.Length).CopyTo(result.AsSpan(dataOffset));

      var entry = new WadEntry(dataOffset, lump.Data.Length, lump.Name);
      var entryOffset = directoryOffset + i * WadEntry.StructSize;
      entry.WriteTo(span[entryOffset..]);

      dataOffset += lump.Data.Length;
    }

    return result;
  }
}
