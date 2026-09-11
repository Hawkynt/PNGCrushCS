using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace FileFormat.WindowsPe.Tests;

internal static class MinimalPeBuilder {
  private const int _RT_CURSOR = 1;
  private const int _RT_BITMAP = 2;
  private const int _RT_ICON = 3;
  private const int _RT_GROUP_CURSOR = 12;
  private const int _RT_GROUP_ICON = 14;

  public static byte[] BuildEmpty() => _Build(null, null, null, null, null);

  public static byte[] BuildWithBitmap(byte[] dibData, int resourceId = 1) {
    var bitmaps = new Dictionary<int, byte[]> { [resourceId] = dibData };
    return _Build(bitmaps, null, null, null, null);
  }

  public static byte[] BuildWithIconGroup(byte[][] iconEntryData, int groupId = 1) {
    var icons = new Dictionary<int, byte[]>();
    for (var i = 0; i < iconEntryData.Length; ++i)
      icons[i + 1] = iconEntryData[i];
    var groupDir = _BuildGroupIconDir(iconEntryData, groupId);
    return _Build(null, icons, groupDir, null, null);
  }

  public static byte[] BuildWithCursorGroup(
    byte[] cursorImageData,
    int width,
    int height,
    ushort hotspotX = 0,
    ushort hotspotY = 0,
    int groupId = 1
  ) {
    var cursorData = new byte[4 + cursorImageData.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(cursorData, hotspotX);
    BinaryPrimitives.WriteUInt16LittleEndian(cursorData.AsSpan(2), hotspotY);
    cursorImageData.CopyTo(cursorData.AsSpan(4));

    var cursors = new Dictionary<int, byte[]> { [1] = cursorData };
    var groupDir = _BuildGroupCursorDir(cursorImageData, width, height, groupId);
    return _Build(null, null, null, cursors, groupDir);
  }

  public static byte[] BuildWithEmbeddedImage(byte[] imageData, int resourceId = 100) {
    var rcdata = new Dictionary<int, byte[]> { [resourceId] = imageData };
    return _Build(null, null, null, null, null, rcdata);
  }

  private static byte[] _Build(
    Dictionary<int, byte[]>? bitmaps,
    Dictionary<int, byte[]>? icons,
    (int GroupId, byte[] GroupDirData)? groupIcon,
    Dictionary<int, byte[]>? cursors,
    (int GroupId, byte[] GroupDirData)? groupCursor,
    Dictionary<int, byte[]>? rcdata = null
  ) {
    const int dosHeaderSize = 0x80;
    const int peSignatureSize = 4;
    const int coffHeaderSize = 20;
    const int optionalHeaderSize = 0x78;
    const int sectionAlignment = 0x200;
    const int rsrcFileOffset = sectionAlignment;
    var rsrcData = _BuildResourceSection(bitmaps, icons, groupIcon, cursors, groupCursor, rcdata);
    var pe = new byte[rsrcFileOffset + rsrcData.Length];
    pe[0] = 0x4D;
    pe[1] = 0x5A;
    BinaryPrimitives.WriteInt32LittleEndian(pe.AsSpan(60), dosHeaderSize);
    pe[dosHeaderSize] = 0x50;
    pe[dosHeaderSize + 1] = 0x45;
    var coffOff = dosHeaderSize + peSignatureSize;
    BinaryPrimitives.WriteUInt16LittleEndian(pe.AsSpan(coffOff), 0x014C);
    BinaryPrimitives.WriteUInt16LittleEndian(pe.AsSpan(coffOff + 2), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(pe.AsSpan(coffOff + 16), (ushort)optionalHeaderSize);
    BinaryPrimitives.WriteUInt16LittleEndian(pe.AsSpan(coffOff + 18), 0x0102);
    var optOff = coffOff + coffHeaderSize;
    BinaryPrimitives.WriteUInt16LittleEndian(pe.AsSpan(optOff), 0x10B);
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(optOff + 56), 0x1000);
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(optOff + 60), (uint)sectionAlignment);
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(optOff + 64), 16);
    var resDirOff = optOff + 96 + 2 * 8;
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(resDirOff), 0x1000);
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(resDirOff + 4), (uint)rsrcData.Length);
    var secOff = optOff + optionalHeaderSize;
    pe[secOff] = 0x2E;
    pe[secOff + 1] = 0x72;
    pe[secOff + 2] = 0x73;
    pe[secOff + 3] = 0x72;
    pe[secOff + 4] = 0x63;
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(secOff + 8), (uint)rsrcData.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(secOff + 12), 0x1000);
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(secOff + 16), (uint)rsrcData.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(secOff + 20), (uint)rsrcFileOffset);
    rsrcData.CopyTo(pe.AsSpan(rsrcFileOffset));
    return pe;
  }

  private static byte[] _BuildResourceSection(
    Dictionary<int, byte[]>? bitmaps,
    Dictionary<int, byte[]>? icons,
    (int GroupId, byte[] GroupDirData)? groupIcon,
    Dictionary<int, byte[]>? cursors,
    (int GroupId, byte[] GroupDirData)? groupCursor,
    Dictionary<int, byte[]>? rcdata
  ) {
    var typeEntries = new List<(int TypeId, List<(int ResId, byte[] Data)> Resources)>();

    _AddDictionaryType(typeEntries, _RT_BITMAP, bitmaps);
    _AddDictionaryType(typeEntries, _RT_ICON, icons);
    if (groupIcon.HasValue)
      typeEntries.Add((_RT_GROUP_ICON, [(groupIcon.Value.GroupId, groupIcon.Value.GroupDirData)]));
    _AddDictionaryType(typeEntries, _RT_CURSOR, cursors);
    if (groupCursor.HasValue)
      typeEntries.Add((_RT_GROUP_CURSOR, [(groupCursor.Value.GroupId, groupCursor.Value.GroupDirData)]));
    _AddDictionaryType(typeEntries, 10, rcdata);

    if (typeEntries.Count == 0)
      return new byte[16];

    var totalRes = 0;
    foreach (var (_, resources) in typeEntries)
      totalRes += resources.Count;

    var level1Size = 16 + typeEntries.Count * 8;
    var level2Size = 0;
    foreach (var (_, resources) in typeEntries)
      level2Size += 16 + resources.Count * 8;
    var level3Size = totalRes * 24;
    var dataEntrySize = totalRes * 16;
    var directorySize = level1Size + level2Size + level3Size + dataEntrySize;
    var dataOffset = (directorySize + 3) & ~3;

    var dataSize = 0;
    foreach (var (_, resources) in typeEntries)
      foreach (var (_, data) in resources)
        dataSize += (data.Length + 3) & ~3;

    var section = new byte[dataOffset + dataSize];
    BinaryPrimitives.WriteUInt16LittleEndian(section.AsSpan(14), (ushort)typeEntries.Count);

    var level1EntryOffset = 16;
    var currentLevel2 = level1Size;
    var currentLevel3 = level1Size + level2Size;
    var currentDataEntry = level1Size + level2Size + level3Size;
    var currentData = dataOffset;

    foreach (var (typeId, resources) in typeEntries) {
      BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(level1EntryOffset), (uint)typeId);
      BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(level1EntryOffset + 4), (uint)(currentLevel2 | unchecked((int)0x80000000)));
      level1EntryOffset += 8;

      BinaryPrimitives.WriteUInt16LittleEndian(section.AsSpan(currentLevel2 + 14), (ushort)resources.Count);
      var level2EntryOffset = currentLevel2 + 16;

      foreach (var (resourceId, data) in resources) {
        BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(level2EntryOffset), (uint)resourceId);
        BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(level2EntryOffset + 4), (uint)(currentLevel3 | unchecked((int)0x80000000)));
        level2EntryOffset += 8;

        BinaryPrimitives.WriteUInt16LittleEndian(section.AsSpan(currentLevel3 + 14), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(currentLevel3 + 16), 0x0409);
        BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(currentLevel3 + 20), (uint)currentDataEntry);
        currentLevel3 += 24;

        BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(currentDataEntry), (uint)(0x1000 + currentData));
        BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(currentDataEntry + 4), (uint)data.Length);
        currentDataEntry += 16;

        data.CopyTo(section.AsSpan(currentData));
        currentData += (data.Length + 3) & ~3;
      }

      currentLevel2 += 16 + resources.Count * 8;
    }

    return section;
  }

  private static void _AddDictionaryType(
    List<(int TypeId, List<(int ResId, byte[] Data)> Resources)> types,
    int typeId,
    Dictionary<int, byte[]>? resources
  ) {
    if (resources is null || resources.Count == 0)
      return;

    var entries = new List<(int, byte[])>();
    foreach (var pair in resources)
      entries.Add((pair.Key, pair.Value));
    types.Add((typeId, entries));
  }

  private static (int GroupId, byte[] GroupDirData) _BuildGroupIconDir(byte[][] iconEntryData, int groupId) {
    var count = iconEntryData.Length;
    var dir = new byte[6 + count * 14];
    BinaryPrimitives.WriteUInt16LittleEndian(dir.AsSpan(2), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(dir.AsSpan(4), (ushort)count);
    for (var i = 0; i < count; ++i) {
      var offset = 6 + i * 14;
      dir[offset] = 16;
      dir[offset + 1] = 16;
      BinaryPrimitives.WriteUInt16LittleEndian(dir.AsSpan(offset + 4), 1);
      BinaryPrimitives.WriteUInt16LittleEndian(dir.AsSpan(offset + 6), 32);
      BinaryPrimitives.WriteInt32LittleEndian(dir.AsSpan(offset + 8), iconEntryData[i].Length);
      BinaryPrimitives.WriteUInt16LittleEndian(dir.AsSpan(offset + 12), (ushort)(i + 1));
    }
    return (groupId, dir);
  }

  private static (int GroupId, byte[] GroupDirData) _BuildGroupCursorDir(
    byte[] cursorImageData,
    int width,
    int height,
    int groupId
  ) {
    var dir = new byte[20];
    BinaryPrimitives.WriteUInt16LittleEndian(dir.AsSpan(2), 2);
    BinaryPrimitives.WriteUInt16LittleEndian(dir.AsSpan(4), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(dir.AsSpan(6), checked((ushort)width));
    BinaryPrimitives.WriteUInt16LittleEndian(dir.AsSpan(8), checked((ushort)height));
    BinaryPrimitives.WriteUInt16LittleEndian(dir.AsSpan(10), BinaryPrimitives.ReadUInt16LittleEndian(cursorImageData.AsSpan(12)));
    BinaryPrimitives.WriteUInt16LittleEndian(dir.AsSpan(12), BinaryPrimitives.ReadUInt16LittleEndian(cursorImageData.AsSpan(14)));
    BinaryPrimitives.WriteInt32LittleEndian(dir.AsSpan(14), checked(cursorImageData.Length + 4));
    BinaryPrimitives.WriteUInt16LittleEndian(dir.AsSpan(18), 1);
    return (groupId, dir);
  }

  public static byte[] CreateMinimalDib(int width = 2, int height = 2) {
    var pixelSize = width * 4 * height;
    var dib = new byte[40 + pixelSize];
    BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(0), 40);
    BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), width);
    BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), height);
    BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 32);
    BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(20), pixelSize);
    for (var i = 40; i < dib.Length; ++i)
      dib[i] = (byte)(i * 7 % 256);
    return dib;
  }

  public static byte[] CreateMinimalIconEntry(int width = 16, int height = 16) {
    var pixelSize = width * 4 * height;
    var maskSize = ((width + 31) / 32) * 4 * height;
    var dib = new byte[40 + pixelSize + maskSize];
    BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(0), 40);
    BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), width);
    BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), height * 2);
    BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 32);
    for (var i = 40; i < 40 + pixelSize; ++i)
      dib[i] = (byte)(i * 11 % 256);
    return dib;
  }
}
