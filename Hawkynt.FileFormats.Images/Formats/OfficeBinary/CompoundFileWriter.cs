using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.OfficeBinary;

/// <summary>A named stream stored in a Microsoft Compound File Binary container.</summary>
internal readonly record struct CompoundStream(string Name, byte[] Data);

/// <summary>Writes version-3 Microsoft Compound File Binary containers.</summary>
/// <remarks>
/// Small streams use the 64-byte mini stream and MiniFAT; larger streams use ordinary 512-byte
/// sectors. Directory siblings are sorted with the CFB name ordering and laid out as a balanced
/// all-black search tree.
/// </remarks>
internal static class CompoundFileWriter {

  internal static ReadOnlySpan<byte> Signature => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

  private const int _HeaderSize = 512;
  private const int _SectorSize = 512;
  private const int _MiniSectorSize = 64;
  private const int _MiniStreamCutoff = 4096;
  private const int _DirectoryEntrySize = 128;
  private const int _FatEntriesPerSector = _SectorSize / sizeof(uint);
  private const int _HeaderDifatEntries = 109;
  private const int _DifatEntriesPerSector = _FatEntriesPerSector - 1;

  private const uint _DifatSector = 0xFFFFFFFC;
  private const uint _FatSector = 0xFFFFFFFD;
  private const uint _EndOfChain = 0xFFFFFFFE;
  private const uint _FreeSector = 0xFFFFFFFF;
  private const uint _NoStream = 0xFFFFFFFF;

  internal static byte[] Write(params CompoundStream[] streams)
    => Write((IReadOnlyList<CompoundStream>)streams);

  internal static byte[] Write(IReadOnlyList<CompoundStream> streams) {
    ArgumentNullException.ThrowIfNull(streams);
    if (streams.Count == 0)
      throw new ArgumentException("A compound file needs at least one stream.", nameof(streams));

    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < streams.Count; ++i) {
      var stream = streams[i];
      if (string.IsNullOrEmpty(stream.Name))
        throw new ArgumentException($"Compound stream {i} has no name.", nameof(streams));
      if (Encoding.Unicode.GetByteCount(stream.Name + '\0') > 64)
        throw new ArgumentException($"Compound stream name '{stream.Name}' is longer than 31 UTF-16 code units.", nameof(streams));
      if (stream.Name.IndexOfAny(['/', '\\', ':', '!']) >= 0)
        throw new ArgumentException($"Compound stream name '{stream.Name}' contains a CFB-forbidden character.", nameof(streams));
      if (stream.Data is null)
        throw new ArgumentException($"Compound stream '{stream.Name}' has null data.", nameof(streams));
      if (!names.Add(stream.Name))
        throw new ArgumentException($"Compound stream name '{stream.Name}' is duplicated.", nameof(streams));
    }

    var regularStarts = new uint[streams.Count];
    var regularSectorCounts = new int[streams.Count];
    var miniStarts = new uint[streams.Count];
    Array.Fill(regularStarts, _EndOfChain);
    Array.Fill(miniStarts, _EndOfChain);

    var regularDataSectors = 0;
    var miniSectorCount = 0;
    for (var i = 0; i < streams.Count; ++i) {
      var length = streams[i].Data.Length;
      if (length == 0)
        continue;

      if (length < _MiniStreamCutoff) {
        miniStarts[i] = checked((uint)miniSectorCount);
        miniSectorCount = checked(miniSectorCount + _DivideRoundUp(length, _MiniSectorSize));
      } else {
        regularSectorCounts[i] = _DivideRoundUp(length, _SectorSize);
        regularDataSectors = checked(regularDataSectors + regularSectorCounts[i]);
      }
    }

    var miniStreamLength = checked(miniSectorCount * _MiniSectorSize);
    var miniStreamSectors = _DivideRoundUp(miniStreamLength, _SectorSize);
    var directorySectors = Math.Max(1, _DivideRoundUp(checked((streams.Count + 1) * _DirectoryEntrySize), _SectorSize));
    var miniFatSectors = miniSectorCount == 0 ? 0 : _DivideRoundUp(checked(miniSectorCount * sizeof(uint)), _SectorSize);
    var nonAllocationSectors = checked(regularDataSectors + miniStreamSectors + directorySectors + miniFatSectors);

    var fatSectorCount = 0;
    var difatSectorCount = 0;
    while (true) {
      var totalSectors = checked(nonAllocationSectors + fatSectorCount + difatSectorCount);
      var neededFat = _DivideRoundUp(totalSectors, _FatEntriesPerSector);
      var neededDifat = neededFat <= _HeaderDifatEntries
        ? 0
        : _DivideRoundUp(neededFat - _HeaderDifatEntries, _DifatEntriesPerSector);
      if (neededFat == fatSectorCount && neededDifat == difatSectorCount)
        break;
      fatSectorCount = neededFat;
      difatSectorCount = neededDifat;
    }

    var cursor = 0;
    for (var i = 0; i < streams.Count; ++i) {
      if (regularSectorCounts[i] == 0)
        continue;
      regularStarts[i] = checked((uint)cursor);
      cursor = checked(cursor + regularSectorCounts[i]);
    }

    var miniStreamStart = miniStreamSectors == 0 ? _EndOfChain : checked((uint)cursor);
    cursor = checked(cursor + miniStreamSectors);
    var directoryStart = checked((uint)cursor);
    cursor = checked(cursor + directorySectors);
    var miniFatStart = miniFatSectors == 0 ? _EndOfChain : checked((uint)cursor);
    cursor = checked(cursor + miniFatSectors);
    var difatStart = difatSectorCount == 0 ? _EndOfChain : checked((uint)cursor);
    cursor = checked(cursor + difatSectorCount);
    var fatStart = checked((uint)cursor);
    cursor = checked(cursor + fatSectorCount);
    var totalSectorCount = cursor;

    var totalBytes = checked((long)_HeaderSize + (long)totalSectorCount * _SectorSize);
    if (totalBytes > int.MaxValue)
      throw new InvalidDataException("Compound file is too large for a version-3 container held in one byte array.");

    var result = new byte[(int)totalBytes];
    _WriteHeader(result, fatSectorCount, directoryStart, miniFatStart, miniFatSectors, difatStart, difatSectorCount, fatStart);

    for (var i = 0; i < streams.Count; ++i) {
      if (regularSectorCounts[i] == 0)
        continue;
      streams[i].Data.CopyTo(result.AsSpan(_SectorOffset(regularStarts[i])));
    }

    if (miniSectorCount != 0)
      _WriteMiniStream(result, streams, miniStarts, miniStreamStart);

    _WriteDirectory(result, streams, regularStarts, miniStarts, miniStreamStart, miniStreamLength, directoryStart, directorySectors);

    if (miniFatSectors != 0)
      _WriteMiniFat(result, streams, miniStarts, miniFatStart, miniFatSectors);
    if (difatSectorCount != 0)
      _WriteDifat(result, difatStart, difatSectorCount, fatStart, fatSectorCount);

    _WriteFat(
      result,
      regularStarts,
      regularSectorCounts,
      miniStreamStart,
      miniStreamSectors,
      directoryStart,
      directorySectors,
      miniFatStart,
      miniFatSectors,
      difatStart,
      difatSectorCount,
      fatStart,
      fatSectorCount,
      totalSectorCount);

    return result;
  }

  private static void _WriteHeader(
    Span<byte> result,
    int fatSectorCount,
    uint directoryStart,
    uint miniFatStart,
    int miniFatSectorCount,
    uint difatStart,
    int difatSectorCount,
    uint fatStart) {

    Signature.CopyTo(result);
    BinaryPrimitives.WriteUInt16LittleEndian(result[24..], 0x003E);
    BinaryPrimitives.WriteUInt16LittleEndian(result[26..], 0x0003);
    BinaryPrimitives.WriteUInt16LittleEndian(result[28..], 0xFFFE);
    BinaryPrimitives.WriteUInt16LittleEndian(result[30..], 9);
    BinaryPrimitives.WriteUInt16LittleEndian(result[32..], 6);
    BinaryPrimitives.WriteUInt32LittleEndian(result[40..], 0);
    BinaryPrimitives.WriteUInt32LittleEndian(result[44..], checked((uint)fatSectorCount));
    BinaryPrimitives.WriteUInt32LittleEndian(result[48..], directoryStart);
    BinaryPrimitives.WriteUInt32LittleEndian(result[52..], 0);
    BinaryPrimitives.WriteUInt32LittleEndian(result[56..], _MiniStreamCutoff);
    BinaryPrimitives.WriteUInt32LittleEndian(result[60..], miniFatStart);
    BinaryPrimitives.WriteUInt32LittleEndian(result[64..], checked((uint)miniFatSectorCount));
    BinaryPrimitives.WriteUInt32LittleEndian(result[68..], difatStart);
    BinaryPrimitives.WriteUInt32LittleEndian(result[72..], checked((uint)difatSectorCount));

    for (var i = 0; i < _HeaderDifatEntries; ++i) {
      var value = i < fatSectorCount ? checked(fatStart + (uint)i) : _FreeSector;
      BinaryPrimitives.WriteUInt32LittleEndian(result[(76 + i * sizeof(uint))..], value);
    }
  }

  private static void _WriteMiniStream(
    Span<byte> result,
    IReadOnlyList<CompoundStream> streams,
    IReadOnlyList<uint> miniStarts,
    uint miniStreamStart) {

    var at = _SectorOffset(miniStreamStart);
    for (var i = 0; i < streams.Count; ++i) {
      if (miniStarts[i] == _EndOfChain)
        continue;
      streams[i].Data.CopyTo(result.Slice(checked(at + (int)miniStarts[i] * _MiniSectorSize)));
    }
  }

  private static void _WriteDirectory(
    Span<byte> result,
    IReadOnlyList<CompoundStream> streams,
    IReadOnlyList<uint> regularStarts,
    IReadOnlyList<uint> miniStarts,
    uint miniStreamStart,
    int miniStreamLength,
    uint directoryStart,
    int directorySectorCount) {

    var directoryLength = checked(directorySectorCount * _SectorSize);
    var directory = result.Slice(_SectorOffset(directoryStart), directoryLength);

    var ordered = new (int SourceIndex, CompoundStream Stream)[streams.Count];
    for (var i = 0; i < streams.Count; ++i)
      ordered[i] = (i, streams[i]);
    Array.Sort(ordered, static (x, y) => _CompareNames(x.Stream.Name, y.Stream.Name));

    var left = new uint[streams.Count + 1];
    var right = new uint[streams.Count + 1];
    Array.Fill(left, _NoStream);
    Array.Fill(right, _NoStream);
    var child = _BuildDirectoryTree(1, streams.Count, left, right);

    _WriteDirectoryEntry(
      directory[.._DirectoryEntrySize],
      "Root Entry",
      5,
      _NoStream,
      _NoStream,
      child,
      miniStreamStart,
      checked((ulong)miniStreamLength));

    for (var rank = 0; rank < ordered.Length; ++rank) {
      var id = rank + 1;
      var source = ordered[rank].SourceIndex;
      var stream = ordered[rank].Stream;
      var start = stream.Data.Length == 0
        ? _EndOfChain
        : stream.Data.Length < _MiniStreamCutoff ? miniStarts[source] : regularStarts[source];
      _WriteDirectoryEntry(
        directory.Slice(id * _DirectoryEntrySize, _DirectoryEntrySize),
        stream.Name,
        2,
        left[id],
        right[id],
        _NoStream,
        start,
        checked((ulong)stream.Data.Length));
    }
  }

  private static uint _BuildDirectoryTree(int first, int last, Span<uint> left, Span<uint> right) {
    if (first > last)
      return _NoStream;

    var middle = first + (last - first) / 2;
    left[middle] = _BuildDirectoryTree(first, middle - 1, left, right);
    right[middle] = _BuildDirectoryTree(middle + 1, last, left, right);
    return checked((uint)middle);
  }

  private static void _WriteDirectoryEntry(
    Span<byte> entry,
    string name,
    byte type,
    uint left,
    uint right,
    uint child,
    uint start,
    ulong size) {

    var encodedName = Encoding.Unicode.GetBytes(name + '\0');
    encodedName.CopyTo(entry);
    BinaryPrimitives.WriteUInt16LittleEndian(entry[64..], checked((ushort)encodedName.Length));
    entry[66] = type;
    entry[67] = 1;
    BinaryPrimitives.WriteUInt32LittleEndian(entry[68..], left);
    BinaryPrimitives.WriteUInt32LittleEndian(entry[72..], right);
    BinaryPrimitives.WriteUInt32LittleEndian(entry[76..], child);
    BinaryPrimitives.WriteUInt32LittleEndian(entry[116..], start);
    BinaryPrimitives.WriteUInt64LittleEndian(entry[120..], size);
  }

  private static void _WriteMiniFat(
    Span<byte> result,
    IReadOnlyList<CompoundStream> streams,
    IReadOnlyList<uint> miniStarts,
    uint miniFatStart,
    int miniFatSectorCount) {

    var entryCount = checked(miniFatSectorCount * _FatEntriesPerSector);
    var entries = new uint[entryCount];
    Array.Fill(entries, _FreeSector);

    for (var i = 0; i < streams.Count; ++i) {
      if (miniStarts[i] == _EndOfChain)
        continue;
      var count = _DivideRoundUp(streams[i].Data.Length, _MiniSectorSize);
      _MarkChain(entries, miniStarts[i], count);
    }

    for (var i = 0; i < entries.Length; ++i)
      BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(_SectorOffset(miniFatStart) + i * sizeof(uint)), entries[i]);
  }

  private static void _WriteDifat(
    Span<byte> result,
    uint difatStart,
    int difatSectorCount,
    uint fatStart,
    int fatSectorCount) {

    var fatIndex = _HeaderDifatEntries;
    for (var i = 0; i < difatSectorCount; ++i) {
      var sector = result.Slice(_SectorOffset(difatStart + (uint)i), _SectorSize);
      for (var j = 0; j < _DifatEntriesPerSector; ++j) {
        var value = fatIndex < fatSectorCount ? checked(fatStart + (uint)fatIndex++) : _FreeSector;
        BinaryPrimitives.WriteUInt32LittleEndian(sector[(j * sizeof(uint))..], value);
      }
      var next = i + 1 < difatSectorCount ? checked(difatStart + (uint)i + 1) : _EndOfChain;
      BinaryPrimitives.WriteUInt32LittleEndian(sector[(_SectorSize - sizeof(uint))..], next);
    }
  }

  private static void _WriteFat(
    Span<byte> result,
    IReadOnlyList<uint> regularStarts,
    IReadOnlyList<int> regularSectorCounts,
    uint miniStreamStart,
    int miniStreamSectorCount,
    uint directoryStart,
    int directorySectorCount,
    uint miniFatStart,
    int miniFatSectorCount,
    uint difatStart,
    int difatSectorCount,
    uint fatStart,
    int fatSectorCount,
    int totalSectorCount) {

    var entries = new uint[checked(fatSectorCount * _FatEntriesPerSector)];
    Array.Fill(entries, _FreeSector);

    for (var i = 0; i < regularStarts.Count; ++i)
      if (regularSectorCounts[i] != 0)
        _MarkChain(entries, regularStarts[i], regularSectorCounts[i]);

    if (miniStreamSectorCount != 0)
      _MarkChain(entries, miniStreamStart, miniStreamSectorCount);
    _MarkChain(entries, directoryStart, directorySectorCount);
    if (miniFatSectorCount != 0)
      _MarkChain(entries, miniFatStart, miniFatSectorCount);

    for (var i = 0; i < difatSectorCount; ++i)
      entries[checked((int)(difatStart + (uint)i))] = _DifatSector;
    for (var i = 0; i < fatSectorCount; ++i)
      entries[checked((int)(fatStart + (uint)i))] = _FatSector;

    for (var i = totalSectorCount; i < entries.Length; ++i)
      entries[i] = _FreeSector;

    for (var i = 0; i < entries.Length; ++i)
      BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(_SectorOffset(fatStart) + i * sizeof(uint)), entries[i]);
  }

  private static void _MarkChain(Span<uint> entries, uint start, int count) {
    if (count <= 0)
      return;
    for (var i = 0; i < count; ++i) {
      var at = checked((int)(start + (uint)i));
      entries[at] = i + 1 < count ? checked(start + (uint)i + 1) : _EndOfChain;
    }
  }

  private static int _CompareNames(string left, string right) {
    var leftLength = checked(Encoding.Unicode.GetByteCount(left + '\0'));
    var rightLength = checked(Encoding.Unicode.GetByteCount(right + '\0'));
    var byLength = leftLength.CompareTo(rightLength);
    if (byLength != 0)
      return byLength;

    var count = Math.Min(left.Length, right.Length);
    for (var i = 0; i < count; ++i) {
      var a = char.ToUpperInvariant(left[i]);
      var b = char.ToUpperInvariant(right[i]);
      if (a != b)
        return a.CompareTo(b);
    }
    return left.Length.CompareTo(right.Length);
  }

  private static int _SectorOffset(uint sector)
    => checked(_HeaderSize + (int)sector * _SectorSize);

  private static int _DivideRoundUp(int value, int divisor)
    => value == 0 ? 0 : checked((int)(((long)value + divisor - 1) / divisor));
}
