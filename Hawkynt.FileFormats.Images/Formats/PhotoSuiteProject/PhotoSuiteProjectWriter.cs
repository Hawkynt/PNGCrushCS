using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.Png;

namespace FileFormat.PhotoSuiteProject;

/// <summary>Writes a raster picture as an MGI PhotoSuite project.</summary>
/// <remarks>
/// PhotoSuite's private project stream inventory is undocumented. The interoperable part established
/// by the reader and XnView is a Microsoft Compound File Binary container carrying whole PNG streams,
/// so the writer emits the smallest structurally valid CFB v3 document containing one PNG stream.
/// </remarks>
public static class PhotoSuiteProjectWriter {

  private const int _SECTOR_SIZE = 512;
  private const int _MINI_SECTOR_SIZE = 64;
  private const int _MINI_STREAM_CUTOFF = 4096;
  private const int _FAT_ENTRIES_PER_SECTOR = _SECTOR_SIZE / sizeof(uint);
  private const int _HEADER_DIFAT_ENTRIES = 109;
  private const int _DIFAT_ENTRIES_PER_SECTOR = _FAT_ENTRIES_PER_SECTOR - 1;

  private const uint _FREE_SECTOR = 0xFFFFFFFF;
  private const uint _END_OF_CHAIN = 0xFFFFFFFE;
  private const uint _FAT_SECTOR = 0xFFFFFFFD;
  private const uint _DIFAT_SECTOR = 0xFFFFFFFC;
  private const uint _NO_STREAM = 0xFFFFFFFF;

  /// <summary>Serializes a PhotoSuite project to a CFB document containing its picture as PNG.</summary>
  public static byte[] ToBytes(PhotoSuiteProjectFile file) {
    if (file.Width <= 0)
      throw new ArgumentOutOfRangeException(nameof(file), file.Width, "Image width must be positive.");
    if (file.Height <= 0)
      throw new ArgumentOutOfRangeException(nameof(file), file.Height, "Image height must be positive.");
    if (file.PixelData is null)
      throw new ArgumentException("PixelData must not be null.", nameof(file));

    var expectedLength = checked((long)file.Width * file.Height * 3);
    if (expectedLength > int.MaxValue || file.PixelData.Length != (int)expectedLength)
      throw new ArgumentException(
        $"PixelData must contain exactly {expectedLength} RGB bytes for a {file.Width}x{file.Height} image.",
        nameof(file));

    var png = PngWriter.ToBytes(PngFile.FromRawImage(PhotoSuiteProjectFile.ToRawImage(file)));
    return _BuildCompoundFile(png);
  }

  private static byte[] _BuildCompoundFile(byte[] png) {
    var usesMiniStream = png.Length < _MINI_STREAM_CUTOFF;
    var miniSectorCount = usesMiniStream ? _DivideRoundUp(png.Length, _MINI_SECTOR_SIZE) : 0;
    var miniStreamSize = usesMiniStream ? miniSectorCount * _MINI_SECTOR_SIZE : 0;
    var miniFatSectorCount = usesMiniStream ? _DivideRoundUp(miniSectorCount, _FAT_ENTRIES_PER_SECTOR) : 0;
    var dataSectorCount = _DivideRoundUp(usesMiniStream ? miniStreamSize : png.Length, _SECTOR_SIZE);

    var fixedSectorCount = checked(1 + miniFatSectorCount + dataSectorCount); // directory + payload support
    var fatSectorCount = 0;
    var difatSectorCount = 0;
    while (true) {
      var totalSectorCount = checked(fixedSectorCount + fatSectorCount + difatSectorCount);
      var requiredFat = _DivideRoundUp(totalSectorCount, _FAT_ENTRIES_PER_SECTOR);
      var requiredDifat = requiredFat <= _HEADER_DIFAT_ENTRIES
        ? 0
        : _DivideRoundUp(requiredFat - _HEADER_DIFAT_ENTRIES, _DIFAT_ENTRIES_PER_SECTOR);

      if (requiredFat == fatSectorCount && requiredDifat == difatSectorCount)
        break;

      fatSectorCount = requiredFat;
      difatSectorCount = requiredDifat;
    }

    var directorySector = checked(fatSectorCount + difatSectorCount);
    var miniFatStart = usesMiniStream ? checked(directorySector + 1) : -1;
    var dataStart = checked(directorySector + 1 + miniFatSectorCount);
    var sectorCount = checked(fatSectorCount + difatSectorCount + 1 + miniFatSectorCount + dataSectorCount);
    var byteLength = checked((long)(sectorCount + 1) * _SECTOR_SIZE);
    if (byteLength > int.MaxValue)
      throw new ArgumentException("The encoded project exceeds the CFB v3 size supported by this writer.", nameof(png));

    var result = new byte[(int)byteLength];
    _WriteHeader(result, fatSectorCount, directorySector, miniFatStart, miniFatSectorCount, difatSectorCount);
    _WriteDifatSectors(result, fatSectorCount, difatSectorCount);
    _WriteFatSectors(
      result, fatSectorCount, difatSectorCount, directorySector,
      miniFatStart, miniFatSectorCount, dataStart, dataSectorCount);
    _WriteDirectory(
      result, directorySector, usesMiniStream,
      dataStart, miniStreamSize, png.Length);

    if (usesMiniStream)
      _WriteMiniFat(result, miniFatStart, miniFatSectorCount, miniSectorCount);

    png.CopyTo(result, _SectorOffset(dataStart));
    return result;
  }

  private static void _WriteHeader(
    byte[] result,
    int fatSectorCount,
    int directorySector,
    int miniFatStart,
    int miniFatSectorCount,
    int difatSectorCount) {

    PhotoSuiteProjectFile.Signature.CopyTo(result);
    _WriteUInt16(result, 24, 0x003E); // minor version
    _WriteUInt16(result, 26, 0x0003); // CFB v3
    _WriteUInt16(result, 28, 0xFFFE); // little-endian byte order
    _WriteUInt16(result, 30, 9);      // 512-byte sectors
    _WriteUInt16(result, 32, 6);      // 64-byte mini sectors
    _WriteUInt32(result, 40, 0);      // directory-sector count is unused in v3
    _WriteUInt32(result, 44, (uint)fatSectorCount);
    _WriteUInt32(result, 48, (uint)directorySector);
    _WriteUInt32(result, 52, 0);      // transactions are not used
    _WriteUInt32(result, 56, _MINI_STREAM_CUTOFF);
    _WriteUInt32(result, 60, miniFatStart >= 0 ? (uint)miniFatStart : _END_OF_CHAIN);
    _WriteUInt32(result, 64, (uint)miniFatSectorCount);
    _WriteUInt32(result, 68, difatSectorCount > 0 ? (uint)fatSectorCount : _END_OF_CHAIN);
    _WriteUInt32(result, 72, (uint)difatSectorCount);

    for (var i = 0; i < _HEADER_DIFAT_ENTRIES; ++i)
      _WriteUInt32(result, 76 + i * sizeof(uint), i < fatSectorCount ? (uint)i : _FREE_SECTOR);
  }

  private static void _WriteDifatSectors(byte[] result, int fatSectorCount, int difatSectorCount) {
    var fatIndex = _HEADER_DIFAT_ENTRIES;
    for (var difatIndex = 0; difatIndex < difatSectorCount; ++difatIndex) {
      var sector = fatSectorCount + difatIndex;
      var offset = _SectorOffset(sector);
      for (var entry = 0; entry < _DIFAT_ENTRIES_PER_SECTOR; ++entry) {
        var value = fatIndex < fatSectorCount ? (uint)fatIndex++ : _FREE_SECTOR;
        _WriteUInt32(result, offset + entry * sizeof(uint), value);
      }

      var next = difatIndex + 1 < difatSectorCount ? (uint)(sector + 1) : _END_OF_CHAIN;
      _WriteUInt32(result, offset + _DIFAT_ENTRIES_PER_SECTOR * sizeof(uint), next);
    }
  }

  private static void _WriteFatSectors(
    byte[] result,
    int fatSectorCount,
    int difatSectorCount,
    int directorySector,
    int miniFatStart,
    int miniFatSectorCount,
    int dataStart,
    int dataSectorCount) {

    var entries = new uint[checked(fatSectorCount * _FAT_ENTRIES_PER_SECTOR)];
    Array.Fill(entries, _FREE_SECTOR);

    for (var i = 0; i < fatSectorCount; ++i)
      entries[i] = _FAT_SECTOR;
    for (var i = 0; i < difatSectorCount; ++i)
      entries[fatSectorCount + i] = _DIFAT_SECTOR;

    entries[directorySector] = _END_OF_CHAIN;
    if (miniFatSectorCount > 0)
      _WriteChain(entries, miniFatStart, miniFatSectorCount);
    _WriteChain(entries, dataStart, dataSectorCount);

    for (var i = 0; i < entries.Length; ++i) {
      var sector = i / _FAT_ENTRIES_PER_SECTOR;
      var entry = i % _FAT_ENTRIES_PER_SECTOR;
      _WriteUInt32(result, _SectorOffset(sector) + entry * sizeof(uint), entries[i]);
    }
  }

  private static void _WriteMiniFat(byte[] result, int miniFatStart, int miniFatSectorCount, int miniSectorCount) {
    var entries = new uint[checked(miniFatSectorCount * _FAT_ENTRIES_PER_SECTOR)];
    Array.Fill(entries, _FREE_SECTOR);
    _WriteChain(entries, 0, miniSectorCount);

    for (var i = 0; i < entries.Length; ++i) {
      var sector = miniFatStart + i / _FAT_ENTRIES_PER_SECTOR;
      var entry = i % _FAT_ENTRIES_PER_SECTOR;
      _WriteUInt32(result, _SectorOffset(sector) + entry * sizeof(uint), entries[i]);
    }
  }

  private static void _WriteDirectory(
    byte[] result,
    int directorySector,
    bool usesMiniStream,
    int dataStart,
    int miniStreamSize,
    int pngLength) {

    var directory = result.AsSpan(_SectorOffset(directorySector), _SECTOR_SIZE);
    _WriteDirectoryEntry(
      directory[..128], "Root Entry", 5, 1,
      usesMiniStream ? (uint)dataStart : _END_OF_CHAIN,
      usesMiniStream ? (ulong)miniStreamSize : 0);
    _WriteDirectoryEntry(
      directory.Slice(128, 128), "Image", 2, _NO_STREAM,
      usesMiniStream ? 0u : (uint)dataStart,
      (ulong)pngLength);
  }

  private static void _WriteDirectoryEntry(
    Span<byte> entry,
    string name,
    byte objectType,
    uint child,
    uint startingSector,
    ulong streamSize) {

    var encodedName = Encoding.Unicode.GetBytes(name + '\0');
    if (encodedName.Length > 64)
      throw new ArgumentException("CFB directory entry names are limited to 31 UTF-16 code points.", nameof(name));

    encodedName.CopyTo(entry);
    BinaryPrimitives.WriteUInt16LittleEndian(entry.Slice(64, 2), (ushort)encodedName.Length);
    entry[66] = objectType;
    entry[67] = 1; // black node; each tree here contains at most one child
    BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(68, 4), _NO_STREAM);
    BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(72, 4), _NO_STREAM);
    BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(76, 4), child);
    BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(116, 4), startingSector);
    BinaryPrimitives.WriteUInt64LittleEndian(entry.Slice(120, 8), streamSize);
  }

  private static void _WriteChain(uint[] entries, int start, int count) {
    for (var i = 0; i < count; ++i)
      entries[start + i] = i + 1 < count ? (uint)(start + i + 1) : _END_OF_CHAIN;
  }

  private static int _DivideRoundUp(int value, int divisor)
    => checked((int)(((long)value + divisor - 1) / divisor));

  private static int _SectorOffset(int sector)
    => checked((sector + 1) * _SECTOR_SIZE);

  private static void _WriteUInt16(byte[] data, int offset, ushort value)
    => BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, sizeof(ushort)), value);

  private static void _WriteUInt32(byte[] data, int offset, uint value)
    => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)), value);
}
