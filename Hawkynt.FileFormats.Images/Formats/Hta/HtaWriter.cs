using System;
using System.Buffers.Binary;

namespace FileFormat.Hta;

/// <summary>Writes Hemera Thumbs archives whose members are complete PNG files.</summary>
public static class HtaWriter {

  public static byte[] ToBytes(HtaFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var members = file.Members ?? throw new ArgumentException("HTA members are required.", nameof(file));
    if (members.Count is < 1 or > HtaFile.MaximumMemberCount)
      throw new ArgumentException($"HTA requires 1..{HtaFile.MaximumMemberCount} members; got {members.Count}.", nameof(file));

    var directoryEnd = checked(HtaFile.DirectoryOffset + members.Count * HtaFile.DirectoryEntrySize);
    var first = Math.Max(HtaFile.FirstMemberOffset, directoryEnd);
    long total = first;
    foreach (var member in members) {
      if (member == null || member.Length < 8 || member[0] != 0x89 || member[1] != (byte)'P' || member[2] != (byte)'N' || member[3] != (byte)'G')
        throw new ArgumentException("Every HTA member must be a complete PNG file.", nameof(file));
      total += member.Length;
    }
    if (total > int.MaxValue)
      throw new ArgumentException("HTA output is too large for one in-memory file.", nameof(file));

    var output = new byte[(int)total];
    HtaFile.Magic.CopyTo(output);
    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(8, 4), HtaFile.SupportedVersion);
    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(12, 4), (uint)members.Count);

    var position = first;
    for (var i = 0; i < members.Count; ++i) {
      var member = members[i];
      var entry = HtaFile.DirectoryOffset + i * HtaFile.DirectoryEntrySize;
      BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(entry, 4), (uint)position);
      BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(entry + 4, 4), (uint)member.Length);
      member.CopyTo(output, position);
      position += member.Length;
    }

    return output;
  }
}
