using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.GraspGl;

public static class GraspGlReader {

  private const int _ENTRY_SIZE = 16; // 1 byte name length + 12 byte name + 1 NUL + 2 bytes reserved

  public static GraspGlFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("GRASP GL file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static GraspGlFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static GraspGlFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static GraspGlFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 2)
      throw new InvalidDataException("GRASP GL data too small (no directory offset).");

    var dirOff = BinaryPrimitives.ReadUInt16LittleEndian(data[..2]);
    if (dirOff < 2 || dirOff > data.Length)
      throw new InvalidDataException($"GRASP GL directory offset {dirOff} out of range (file is {data.Length} bytes).");

    // Entries occupy the region [2, dirOff). Each is 16 bytes.
    var entriesRegion = dirOff - 2;
    if (entriesRegion % _ENTRY_SIZE != 0)
      throw new InvalidDataException($"GRASP GL directory region ({entriesRegion} bytes) is not a multiple of {_ENTRY_SIZE}.");

    var count = entriesRegion / _ENTRY_SIZE;
    var entries = new List<GraspGlFile.GraspEntry>(count);
    int cursor = dirOff;

    for (var i = 0; i < count; ++i) {
      var off = 2 + i * _ENTRY_SIZE;
      var nameLen = data[off];
      if (nameLen == 0)
        break; // null terminator entry — end of directory
      if (nameLen > 12)
        throw new InvalidDataException($"GRASP GL entry {i} has implausible name length {nameLen}.");
      var name = Encoding.ASCII.GetString(data.Slice(off + 1, nameLen));

      // Each payload is laid out as: 4-byte little-endian length prefix + length bytes of data.
      if (cursor + 4 > data.Length)
        throw new InvalidDataException($"GRASP GL entry {i} payload header out of range.");
      var len = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(cursor, 4));
      if (len < 0 || cursor + 4 + len > data.Length)
        throw new InvalidDataException($"GRASP GL entry {i} payload length {len} out of range.");
      var payload = data.Slice(cursor + 4, len).ToArray();
      entries.Add(new GraspGlFile.GraspEntry(name, payload));
      cursor += 4 + len;
    }

    return new GraspGlFile { Entries = entries.ToArray() };
  }
}
