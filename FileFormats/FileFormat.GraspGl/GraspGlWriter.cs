using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.GraspGl;

public static class GraspGlWriter {

  private const int _ENTRY_SIZE = 16;

  public static byte[] ToBytes(GraspGlFile file) {
    ArgumentNullException.ThrowIfNull(file.Entries);

    var entries = file.Entries;
    var dirOff = 2 + entries.Length * _ENTRY_SIZE + _ENTRY_SIZE; // +trailing null entry
    using var ms = new MemoryStream();
    using var w = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);
    w.Write((ushort)dirOff);

    foreach (var e in entries) {
      var name = e.Name ?? string.Empty;
      var nameBytes = Encoding.ASCII.GetBytes(name);
      if (nameBytes.Length > 12)
        throw new InvalidDataException($"GRASP GL entry name '{name}' exceeds 12 characters.");
      w.Write((byte)nameBytes.Length);
      w.Write(nameBytes);
      // Pad name region to 13 bytes (12 chars + NUL terminator) so the full entry stride matches
      // the 16-byte reader expectation: 1 (len) + 13 (name+NUL) + 2 (reserved) = 16.
      for (var p = nameBytes.Length; p < 13; ++p) w.Write((byte)0);
      w.Write((ushort)0); // reserved
    }
    // null terminator entry
    for (var p = 0; p < _ENTRY_SIZE; ++p) w.Write((byte)0);

    // Payloads with explicit 4-byte little-endian length prefix so the reader can walk them.
    Span<byte> lenBuf = stackalloc byte[4];
    foreach (var e in entries) {
      var data = e.Data ?? [];
      BinaryPrimitives.WriteInt32LittleEndian(lenBuf, data.Length);
      w.Write(lenBuf);
      w.Write(data);
    }
    return ms.ToArray();
  }
}
