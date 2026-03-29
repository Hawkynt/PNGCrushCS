using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.Icns;

/// <summary>Assembles ICNS file bytes from an <see cref="IcnsFile"/>.</summary>
public static class IcnsWriter {

  /// <summary>The ICNS magic bytes: "icns".</summary>
  private static readonly byte[] _Magic = [(byte)'i', (byte)'c', (byte)'n', (byte)'s'];

  public static byte[] ToBytes(IcnsFile file) {
    ArgumentNullException.ThrowIfNull(file);

    // Calculate total file size
    var totalSize = 8; // header: 4 magic + 4 length
    foreach (var entry in file.Entries)
      totalSize += IcnsEntry.HeaderSize + entry.Data.Length;

    var result = new byte[totalSize];

    // Write header
    _Magic.AsSpan(0, 4).CopyTo(result.AsSpan(0));
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4), (uint)totalSize);

    // Write entries
    var offset = 8;
    foreach (var entry in file.Entries) {
      var osTypeBytes = Encoding.ASCII.GetBytes(entry.OsType);
      osTypeBytes.AsSpan(0, 4).CopyTo(result.AsSpan(offset));

      var entryLength = IcnsEntry.HeaderSize + entry.Data.Length;
      BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(offset + 4), (uint)entryLength);

      entry.Data.AsSpan(0, entry.Data.Length).CopyTo(result.AsSpan(offset + IcnsEntry.HeaderSize));
      offset += entryLength;
    }

    return result;
  }
}
