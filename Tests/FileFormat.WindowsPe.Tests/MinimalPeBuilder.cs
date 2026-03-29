using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace FileFormat.WindowsPe.Tests;

internal static class MinimalPeBuilder {
  private const int _RT_BITMAP = 2;
  private const int _RT_ICON = 3;
  private const int _RT_GROUP_ICON = 14;

  public static byte[] BuildEmpty() => _Build(null, null, null);

  public static byte[] BuildWithBitmap(byte[] dibData, int resourceId = 1) {
    var bitmaps = new Dictionary<int, byte[]> { [resourceId] = dibData };
    return _Build(bitmaps, null, null);
  }

  public static byte[] BuildWithIconGroup(byte[][] iconEntryData, int groupId = 1) {
    var icons = new Dictionary<int, byte[]>();
    for (var i = 0; i < iconEntryData.Length; ++i)
      icons[i + 1] = iconEntryData[i];
    var groupDir = _BuildGroupIconDir(iconEntryData, groupId);
    return _Build(null, icons, groupDir);
  }

  public static byte[] BuildWithEmbeddedImage(byte[] imageData, int resourceId = 100) {
    var rcdata = new Dictionary<int, byte[]> { [resourceId] = imageData };
    return _Build(null, null, null, rcdata: rcdata);
  }

  private static byte[] _Build(Dictionary<int, byte[]>? bitmaps, Dictionary<int, byte[]>? icons,
    (int GroupId, byte[] GroupDirData)? groupIcon, Dictionary<int, byte[]>? rcdata = null) {
    const int dosHeaderSize = 0x80; const int peSignatureSize = 4;
    const int coffHeaderSize = 20; const int optionalHeaderSize = 0x78;
    const int sectionAlignment = 0x200; const int rsrcFileOffset = sectionAlignment;
    var rsrcData = _BuildResourceSection(bitmaps, icons, groupIcon, rcdata);
    var pe = new byte[rsrcFileOffset + rsrcData.Length];
    pe[0] = 0x4D; pe[1] = 0x5A;
    BinaryPrimitives.WriteInt32LittleEndian(pe.AsSpan(60), dosHeaderSize);
    pe[dosHeaderSize] = 0x50; pe[dosHeaderSize + 1] = 0x45;
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
    pe[secOff] = 0x2E; pe[secOff+1] = 0x72; pe[secOff+2] = 0x73; pe[secOff+3] = 0x72; pe[secOff+4] = 0x63;
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(secOff + 8), (uint)rsrcData.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(secOff + 12), 0x1000);
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(secOff + 16), (uint)rsrcData.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(secOff + 20), (uint)rsrcFileOffset);
    rsrcData.CopyTo(pe.AsSpan(rsrcFileOffset));
    return pe;
  }

  private static byte[] _BuildResourceSection(Dictionary<int, byte[]>? bitmaps, Dictionary<int, byte[]>? icons,
    (int GroupId, byte[] GroupDirData)? groupIcon, Dictionary<int, byte[]>? rcdata) {
    var typeEntries = new List<(int TypeId, List<(int ResId, byte[] Data)> Resources)>();
    if (bitmaps != null && bitmaps.Count > 0) { var e = new List<(int, byte[])>(); foreach (var kv in bitmaps) e.Add((kv.Key, kv.Value)); typeEntries.Add((_RT_BITMAP, e)); }
    if (icons != null && icons.Count > 0) { var e = new List<(int, byte[])>(); foreach (var kv in icons) e.Add((kv.Key, kv.Value)); typeEntries.Add((_RT_ICON, e)); }
    if (groupIcon.HasValue) { var e = new List<(int, byte[])> { (groupIcon.Value.GroupId, groupIcon.Value.GroupDirData) }; typeEntries.Add((_RT_GROUP_ICON, e)); }
    if (rcdata != null && rcdata.Count > 0) { var e = new List<(int, byte[])>(); foreach (var kv in rcdata) e.Add((kv.Key, kv.Value)); typeEntries.Add((10, e)); }
    if (typeEntries.Count == 0) return new byte[16];
    var totalRes = 0; foreach (var (_, r) in typeEntries) totalRes += r.Count;
    var l1 = 16 + typeEntries.Count * 8; var l2t = 0;
    foreach (var (_, r) in typeEntries) l2t += 16 + r.Count * 8;
    var l3t = totalRes * 24; var det = totalRes * 16;
    var dirSz = l1 + l2t + l3t + det; var dataOff = (dirSz + 3) & ~3;
    var dataSz = 0; foreach (var (_, r) in typeEntries) foreach (var (_, d) in r) dataSz += (d.Length + 3) & ~3;
    var sec = new byte[dataOff + dataSz];
    BinaryPrimitives.WriteUInt16LittleEndian(sec.AsSpan(14), (ushort)typeEntries.Count);
    var eOff = 16; var cL2 = l1; var cL3 = l1 + l2t; var cDE = l1 + l2t + l3t; var cD = dataOff;
    foreach (var (tid, res) in typeEntries) {
      BinaryPrimitives.WriteUInt32LittleEndian(sec.AsSpan(eOff), (uint)tid);
      BinaryPrimitives.WriteUInt32LittleEndian(sec.AsSpan(eOff + 4), (uint)(cL2 | unchecked((int)0x80000000)));
      eOff += 8;
      BinaryPrimitives.WriteUInt16LittleEndian(sec.AsSpan(cL2 + 14), (ushort)res.Count);
      var l2e = cL2 + 16;
      foreach (var (rid, d) in res) {
        BinaryPrimitives.WriteUInt32LittleEndian(sec.AsSpan(l2e), (uint)rid);
        BinaryPrimitives.WriteUInt32LittleEndian(sec.AsSpan(l2e + 4), (uint)(cL3 | unchecked((int)0x80000000)));
        l2e += 8;
        BinaryPrimitives.WriteUInt16LittleEndian(sec.AsSpan(cL3 + 14), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(sec.AsSpan(cL3 + 16), 0x0409);
        BinaryPrimitives.WriteUInt32LittleEndian(sec.AsSpan(cL3 + 20), (uint)cDE);
        cL3 += 24;
        BinaryPrimitives.WriteUInt32LittleEndian(sec.AsSpan(cDE), (uint)(0x1000 + cD));
        BinaryPrimitives.WriteUInt32LittleEndian(sec.AsSpan(cDE + 4), (uint)d.Length);
        cDE += 16; d.CopyTo(sec.AsSpan(cD)); cD += (d.Length + 3) & ~3;
      }
      cL2 += 16 + res.Count * 8;
    }
    return sec;
  }

  private static (int GroupId, byte[] GroupDirData) _BuildGroupIconDir(byte[][] iconEntryData, int groupId) {
    var count = iconEntryData.Length; var dir = new byte[6 + count * 14];
    BinaryPrimitives.WriteUInt16LittleEndian(dir.AsSpan(2), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(dir.AsSpan(4), (ushort)count);
    for (var i = 0; i < count; ++i) {
      var off = 6 + i * 14; dir[off] = 16; dir[off + 1] = 16;
      BinaryPrimitives.WriteUInt16LittleEndian(dir.AsSpan(off + 4), 1);
      BinaryPrimitives.WriteUInt16LittleEndian(dir.AsSpan(off + 6), 32);
      BinaryPrimitives.WriteInt32LittleEndian(dir.AsSpan(off + 8), iconEntryData[i].Length);
      BinaryPrimitives.WriteUInt16LittleEndian(dir.AsSpan(off + 12), (ushort)(i + 1));
    }
    return (groupId, dir);
  }

  public static byte[] CreateMinimalDib(int width = 2, int height = 2) {
    var ps = width * 4 * height; var dib = new byte[40 + ps];
    BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(0), 40);
    BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), width);
    BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), height);
    BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 32);
    BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(20), ps);
    for (var i = 40; i < dib.Length; ++i) dib[i] = (byte)(i * 7 % 256);
    return dib;
  }

  public static byte[] CreateMinimalIconEntry(int width = 16, int height = 16) {
    var ps = width * 4 * height; var ms = ((width + 31) / 32) * 4 * height;
    var dib = new byte[40 + ps + ms];
    BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(0), 40);
    BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), width);
    BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), height * 2);
    BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 32);
    for (var i = 40; i < 40 + ps; ++i) dib[i] = (byte)(i * 11 % 256);
    return dib;
  }
}
