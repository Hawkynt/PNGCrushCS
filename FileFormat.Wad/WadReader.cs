using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Wad;

/// <summary>Reads WAD files from bytes, streams, or file paths.</summary>
public static class WadReader {

  public static WadFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("WAD file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static WadFile FromStream(Stream stream) {
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

  public static WadFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < WadHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid WAD file.");

    var span = data.AsSpan();
    var header = WadHeader.ReadFrom(span);

    var type = (header.Id1, header.Id2, header.Id3, header.Id4) switch {
      ((byte)'I', (byte)'W', (byte)'A', (byte)'D') => WadType.Iwad,
      ((byte)'P', (byte)'W', (byte)'A', (byte)'D') => WadType.Pwad,
      _ => throw new InvalidDataException("Invalid WAD signature.")
    };

    var numLumps = header.NumLumps;
    var directoryOffset = header.DirectoryOffset;

    var requiredSize = directoryOffset + (long)numLumps * WadEntry.StructSize;
    if (data.Length < requiredSize)
      throw new InvalidDataException("Data too small for the declared WAD directory.");

    var lumps = new List<WadLump>(numLumps);
    for (var i = 0; i < numLumps; ++i) {
      var entryOffset = directoryOffset + i * WadEntry.StructSize;
      var entry = WadEntry.ReadFrom(span[entryOffset..]);

      var lumpData = new byte[entry.Size];
      if (entry.Size > 0)
        data.AsSpan(entry.FilePos, entry.Size).CopyTo(lumpData.AsSpan(0));

      lumps.Add(new WadLump { Name = entry.Name, Data = lumpData });
    }

    return new WadFile { Type = type, Lumps = lumps };
  }
}
