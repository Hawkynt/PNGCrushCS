using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Ico;

/// <summary>Assembles ICO file bytes from an <see cref="IcoFile"/>.</summary>
public static class IcoWriter {

  public static byte[] ToBytes(IcoFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return _Assemble(file, IcoFileType.Icon);
  }

  /// <summary>
  /// Assembles an icon or a cursor from entries that already carry their payloads and hotspots.
  /// </summary>
  /// <remarks>
  /// The counterpart to <see cref="IcoReader.ReadBundle(byte[])"/>, and its inverse: what that
  /// reads, this writes back. Payload bytes are copied unexamined — an entry's payload is whatever
  /// the caller says it is, and re-encoding it here would silently undo the choice of PNG or bitmap
  /// that the caller made.
  /// </remarks>
  /// <exception cref="ArgumentNullException"><paramref name="entries"/> is null.</exception>
  /// <exception cref="ArgumentException">There are more entries than a directory can count.</exception>
  public static byte[] Assemble(IcoFileType kind, IReadOnlyList<IconBundleEntry> entries) {
    ArgumentNullException.ThrowIfNull(entries);
    if (entries.Count > ushort.MaxValue)
      throw new ArgumentException($"A directory counts entries in two bytes, so it cannot hold {entries.Count}.", nameof(entries));

    var count = entries.Count;
    var dataStart = IcoHeader.StructSize + count * IcoDirectoryEntry.StructSize;
    var totalDataSize = 0;
    for (var i = 0; i < count; ++i)
      totalDataSize += entries[i].Data.Length;

    var result = new byte[dataStart + totalDataSize];
    new IcoHeader(0, (ushort)kind, (ushort)count).WriteTo(result);

    var dataOffset = dataStart;
    for (var i = 0; i < count; ++i) {
      var entry = entries[i];

      // In a cursor these two bytes are the hotspot. In an icon they are one plane and the depth:
      // the plane count has been 1 since colours stopped being stored a bit-plane at a time, and
      // nothing reads anything else.
      var (field4, field5) = kind == IcoFileType.Cursor
        ? (entry.HotspotX, entry.HotspotY)
        : ((ushort)1, (ushort)entry.BitsPerPixel);

      var directoryEntry = new IcoDirectoryEntry(
        entry.Width >= 256 ? (byte)0 : (byte)entry.Width,
        entry.Height >= 256 ? (byte)0 : (byte)entry.Height,
        _ColourCount(entry.BitsPerPixel),
        0,  // Reserved
        field4,
        field5,
        entry.Data.Length,
        dataOffset
      );
      directoryEntry.WriteTo(result.AsSpan(IcoHeader.StructSize + i * IcoDirectoryEntry.StructSize));

      entry.Data.CopyTo(result, dataOffset);
      dataOffset += entry.Data.Length;
    }

    return result;
  }

  /// <summary>How many colours the entry's palette holds, as a directory entry states it.</summary>
  /// <remarks>
  /// The field is one byte, so 256 does not fit and is written as nought — which is also what a
  /// picture too deep to have a palette writes. Both readings mean "do not expect a palette of a
  /// size I could tell you", so the collision is harmless, and it is why the field is one byte in
  /// the first place.
  /// </remarks>
  private static byte _ColourCount(int bitsPerPixel)
    => bitsPerPixel is > 0 and <= 8 ? (byte)((1 << bitsPerPixel) & 0xFF) : (byte)0;

  internal static byte[] _Assemble(IcoFile file, IcoFileType fileType, Func<int, (ushort field4, ushort field5)>? directoryFieldOverride = null) {
    var entries = new List<IconBundleEntry>(file.Images.Count);
    for (var i = 0; i < file.Images.Count; ++i) {
      var image = file.Images[i];
      var (hotspotX, hotspotY) = directoryFieldOverride != null
        ? directoryFieldOverride(i)
        : ((ushort)0, (ushort)0);

      entries.Add(new IconBundleEntry(
        i, image.Width, image.Height, image.BitsPerPixel, hotspotX, hotspotY, image.Format, image.Data));
    }

    return Assemble(fileType, entries);
  }
}
