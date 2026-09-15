using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FileFormat.Fpx;

/// <summary>Builds a Compound File Binary v3 container with storages, streams and class IDs.</summary>
/// <remarks>
/// FlashPix needs more than a flat OLE carrier: the root, image-object and resolution storages all
/// have class IDs, small streams live in the MiniFAT, and each storage owns a red/black sibling tree.
/// This writer implements that container machinery here rather than depending on COM or a native OLE
/// implementation.
/// </remarks>
internal sealed class CompoundFileWriter {

  private const int _SectorSize = 512;
  private const int _MiniSectorSize = 64;
  private const int _MiniStreamCutoff = 4096;
  private const int _DirectoryEntrySize = 128;
  private const int _FatEntriesPerSector = _SectorSize / sizeof(uint);
  private const int _HeaderDifatEntries = 109;
  private const int _DifatEntriesPerSector = _FatEntriesPerSector - 1;

  private const uint _FreeSector = 0xFFFFFFFF;
  private const uint _EndOfChain = 0xFFFFFFFE;
  private const uint _FatSector = 0xFFFFFFFD;
  private const uint _DifatSector = 0xFFFFFFFC;
  private const uint _NoStream = 0xFFFFFFFF;

  private const byte _Red = 0;
  private const byte _Black = 1;

  private readonly List<Node> _nodes = [];

  internal CompoundFileWriter(Guid rootClassId) {
    _nodes.Add(new("Root Entry", CompoundFile.EntryRoot, rootClassId, -1, null));
  }

  internal int AddStorage(int parent, string name, Guid classId)
    => _Add(parent, name, CompoundFile.EntryStorage, classId, null);

  internal int AddStream(int parent, string name, byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return _Add(parent, name, CompoundFile.EntryStream, Guid.Empty, data);
  }

  private int _Add(int parent, string name, byte type, Guid classId, byte[]? data) {
    if ((uint)parent >= (uint)_nodes.Count || _nodes[parent].Type is not CompoundFile.EntryRoot and not CompoundFile.EntryStorage)
      throw new ArgumentOutOfRangeException(nameof(parent));
    if (string.IsNullOrEmpty(name) || name.Length > 31)
      throw new ArgumentException("CFB directory names must contain 1 to 31 UTF-16 code units.", nameof(name));

    var index = _nodes.Count;
    _nodes.Add(new(name, type, classId, parent, data));
    return index;
  }

  internal byte[] Build() {
    _BuildDirectoryTrees();

    var miniStarts = Enumerable.Repeat(-1, _nodes.Count).ToArray();
    var regularStarts = Enumerable.Repeat(-1, _nodes.Count).ToArray();
    var regularCounts = new int[_nodes.Count];

    var miniSectorCount = 0;
    for (var i = 1; i < _nodes.Count; ++i) {
      var data = _nodes[i].Data;
      if (_nodes[i].Type != CompoundFile.EntryStream || data is null || data.Length == 0 || data.Length >= _MiniStreamCutoff)
        continue;

      miniStarts[i] = miniSectorCount;
      miniSectorCount = checked(miniSectorCount + _DivideRoundUp(data.Length, _MiniSectorSize));
    }

    var miniStream = new byte[checked(miniSectorCount * _MiniSectorSize)];
    var miniFatEntries = new uint[miniSectorCount];
    Array.Fill(miniFatEntries, _FreeSector);

    for (var i = 1; i < _nodes.Count; ++i) {
      var start = miniStarts[i];
      if (start < 0)
        continue;

      var data = _nodes[i].Data!;
      data.CopyTo(miniStream, checked(start * _MiniSectorSize));
      _WriteChain(miniFatEntries, start, _DivideRoundUp(data.Length, _MiniSectorSize));
    }

    var directorySectorCount = Math.Max(1, _DivideRoundUp(checked(_nodes.Count * _DirectoryEntrySize), _SectorSize));
    var miniFatSectorCount = miniSectorCount == 0 ? 0 : _DivideRoundUp(checked(miniSectorCount * sizeof(uint)), _SectorSize);
    var miniStreamSectorCount = miniStream.Length == 0 ? 0 : _DivideRoundUp(miniStream.Length, _SectorSize);

    var largeStreamSectorCount = 0;
    for (var i = 1; i < _nodes.Count; ++i) {
      var data = _nodes[i].Data;
      if (_nodes[i].Type != CompoundFile.EntryStream || data is null || data.Length < _MiniStreamCutoff)
        continue;

      regularCounts[i] = _DivideRoundUp(data.Length, _SectorSize);
      largeStreamSectorCount = checked(largeStreamSectorCount + regularCounts[i]);
    }

    var fixedSectorCount = checked(directorySectorCount + miniFatSectorCount + miniStreamSectorCount + largeStreamSectorCount);
    var fatSectorCount = 0;
    var difatSectorCount = 0;
    while (true) {
      var totalSectorCount = checked(fixedSectorCount + fatSectorCount + difatSectorCount);
      var requiredFat = _DivideRoundUp(totalSectorCount, _FatEntriesPerSector);
      var requiredDifat = requiredFat <= _HeaderDifatEntries
        ? 0
        : _DivideRoundUp(requiredFat - _HeaderDifatEntries, _DifatEntriesPerSector);

      if (requiredFat == fatSectorCount && requiredDifat == difatSectorCount)
        break;

      fatSectorCount = requiredFat;
      difatSectorCount = requiredDifat;
    }

    var directoryStart = checked(fatSectorCount + difatSectorCount);
    var miniFatStart = miniFatSectorCount == 0 ? -1 : checked(directoryStart + directorySectorCount);
    var miniStreamStart = miniStreamSectorCount == 0
      ? -1
      : checked(directoryStart + directorySectorCount + miniFatSectorCount);
    var next = checked(directoryStart + directorySectorCount + miniFatSectorCount + miniStreamSectorCount);

    for (var i = 1; i < _nodes.Count; ++i) {
      if (regularCounts[i] == 0)
        continue;
      regularStarts[i] = next;
      next = checked(next + regularCounts[i]);
    }

    var sectorCount = next;
    var byteLength = checked((long)(sectorCount + 1) * _SectorSize);
    if (byteLength > int.MaxValue)
      throw new ArgumentException("The compound file exceeds the CFB v3 size supported by this writer.");

    var result = new byte[(int)byteLength];
    _WriteHeader(result, fatSectorCount, directoryStart, miniFatStart, miniFatSectorCount, difatSectorCount);
    _WriteDifat(result, fatSectorCount, difatSectorCount);
    _WriteFat(
      result, fatSectorCount, difatSectorCount,
      directoryStart, directorySectorCount,
      miniFatStart, miniFatSectorCount,
      miniStreamStart, miniStreamSectorCount,
      regularStarts, regularCounts);

    _WriteDirectory(result, directoryStart, directorySectorCount, miniStarts, regularStarts, miniStreamStart, miniStream.Length);

    if (miniFatSectorCount > 0)
      _WriteMiniFat(result, miniFatStart, miniFatSectorCount, miniFatEntries);
    if (miniStreamSectorCount > 0)
      miniStream.CopyTo(result, _SectorOffset(miniStreamStart));

    for (var i = 1; i < _nodes.Count; ++i) {
      if (regularStarts[i] < 0)
        continue;
      _nodes[i].Data!.CopyTo(result, _SectorOffset(regularStarts[i]));
    }

    return result;
  }

  private void _BuildDirectoryTrees() {
    foreach (var node in _nodes) {
      node.Left = _NoStream;
      node.Right = _NoStream;
      node.Child = _NoStream;
      node.Color = _Black;
    }

    var treeParents = Enumerable.Repeat(-1, _nodes.Count).ToArray();
    for (var parent = 0; parent < _nodes.Count; ++parent) {
      if (_nodes[parent].Type is not CompoundFile.EntryRoot and not CompoundFile.EntryStorage)
        continue;

      uint root = _NoStream;
      foreach (var child in Enumerable.Range(1, _nodes.Count - 1).Where(i => _nodes[i].Parent == parent))
        _InsertDirectoryNode(ref root, child, treeParents);

      _nodes[parent].Child = root;
    }
  }

  private void _InsertDirectoryNode(ref uint root, int inserted, int[] parents) {
    var parent = -1;
    var current = root;
    while (current != _NoStream) {
      parent = (int)current;
      current = _CompareNames(_nodes[inserted].Name, _nodes[parent].Name) < 0
        ? _nodes[parent].Left
        : _nodes[parent].Right;
    }

    parents[inserted] = parent;
    if (parent < 0)
      root = (uint)inserted;
    else if (_CompareNames(_nodes[inserted].Name, _nodes[parent].Name) < 0)
      _nodes[parent].Left = (uint)inserted;
    else
      _nodes[parent].Right = (uint)inserted;

    _nodes[inserted].Left = _NoStream;
    _nodes[inserted].Right = _NoStream;
    _nodes[inserted].Color = _Red;

    var node = inserted;
    while ((uint)node != root && parents[node] >= 0 && _nodes[parents[node]].Color == _Red) {
      var p = parents[node];
      var grand = parents[p];
      if (grand < 0)
        break;

      if (_nodes[grand].Left == (uint)p) {
        var uncle = _nodes[grand].Right;
        if (uncle != _NoStream && _nodes[(int)uncle].Color == _Red) {
          _nodes[p].Color = _Black;
          _nodes[(int)uncle].Color = _Black;
          _nodes[grand].Color = _Red;
          node = grand;
          continue;
        }

        if (_nodes[p].Right == (uint)node) {
          node = p;
          _RotateLeft(ref root, node, parents);
          p = parents[node];
          grand = p >= 0 ? parents[p] : -1;
        }

        if (p >= 0 && grand >= 0) {
          _nodes[p].Color = _Black;
          _nodes[grand].Color = _Red;
          _RotateRight(ref root, grand, parents);
        }
      } else {
        var uncle = _nodes[grand].Left;
        if (uncle != _NoStream && _nodes[(int)uncle].Color == _Red) {
          _nodes[p].Color = _Black;
          _nodes[(int)uncle].Color = _Black;
          _nodes[grand].Color = _Red;
          node = grand;
          continue;
        }

        if (_nodes[p].Left == (uint)node) {
          node = p;
          _RotateRight(ref root, node, parents);
          p = parents[node];
          grand = p >= 0 ? parents[p] : -1;
        }

        if (p >= 0 && grand >= 0) {
          _nodes[p].Color = _Black;
          _nodes[grand].Color = _Red;
          _RotateLeft(ref root, grand, parents);
        }
      }
    }

    _nodes[(int)root].Color = _Black;
  }

  private void _RotateLeft(ref uint root, int node, int[] parents) {
    var right = _nodes[node].Right;
    if (right == _NoStream)
      return;

    var pivot = (int)right;
    _nodes[node].Right = _nodes[pivot].Left;
    if (_nodes[pivot].Left != _NoStream)
      parents[(int)_nodes[pivot].Left] = node;

    parents[pivot] = parents[node];
    if (parents[node] < 0)
      root = (uint)pivot;
    else if (_nodes[parents[node]].Left == (uint)node)
      _nodes[parents[node]].Left = (uint)pivot;
    else
      _nodes[parents[node]].Right = (uint)pivot;

    _nodes[pivot].Left = (uint)node;
    parents[node] = pivot;
  }

  private void _RotateRight(ref uint root, int node, int[] parents) {
    var left = _nodes[node].Left;
    if (left == _NoStream)
      return;

    var pivot = (int)left;
    _nodes[node].Left = _nodes[pivot].Right;
    if (_nodes[pivot].Right != _NoStream)
      parents[(int)_nodes[pivot].Right] = node;

    parents[pivot] = parents[node];
    if (parents[node] < 0)
      root = (uint)pivot;
    else if (_nodes[parents[node]].Left == (uint)node)
      _nodes[parents[node]].Left = (uint)pivot;
    else
      _nodes[parents[node]].Right = (uint)pivot;

    _nodes[pivot].Right = (uint)node;
    parents[node] = pivot;
  }

  private static int _CompareNames(string left, string right) {
    var byLength = left.Length.CompareTo(right.Length);
    return byLength != 0 ? byLength : StringComparer.OrdinalIgnoreCase.Compare(left, right);
  }

  private static void _WriteHeader(
    byte[] result, int fatSectorCount, int directoryStart,
    int miniFatStart, int miniFatSectorCount, int difatSectorCount) {

    CompoundFile.Signature.CopyTo(result);
    _WriteUInt16(result, 24, 0x003E);
    _WriteUInt16(result, 26, 0x0003);
    _WriteUInt16(result, 28, 0xFFFE);
    _WriteUInt16(result, 30, 9);
    _WriteUInt16(result, 32, 6);
    _WriteUInt32(result, 40, 0);
    _WriteUInt32(result, 44, (uint)fatSectorCount);
    _WriteUInt32(result, 48, (uint)directoryStart);
    _WriteUInt32(result, 52, 0);
    _WriteUInt32(result, 56, _MiniStreamCutoff);
    _WriteUInt32(result, 60, miniFatStart >= 0 ? (uint)miniFatStart : _EndOfChain);
    _WriteUInt32(result, 64, (uint)miniFatSectorCount);
    _WriteUInt32(result, 68, difatSectorCount > 0 ? (uint)fatSectorCount : _EndOfChain);
    _WriteUInt32(result, 72, (uint)difatSectorCount);

    for (var i = 0; i < _HeaderDifatEntries; ++i)
      _WriteUInt32(result, 76 + i * sizeof(uint), i < fatSectorCount ? (uint)i : _FreeSector);
  }

  private static void _WriteDifat(byte[] result, int fatSectorCount, int difatSectorCount) {
    var fatIndex = _HeaderDifatEntries;
    for (var difatIndex = 0; difatIndex < difatSectorCount; ++difatIndex) {
      var sector = fatSectorCount + difatIndex;
      var offset = _SectorOffset(sector);
      for (var entry = 0; entry < _DifatEntriesPerSector; ++entry)
        _WriteUInt32(result, offset + entry * sizeof(uint), fatIndex < fatSectorCount ? (uint)fatIndex++ : _FreeSector);

      _WriteUInt32(
        result,
        offset + _DifatEntriesPerSector * sizeof(uint),
        difatIndex + 1 < difatSectorCount ? (uint)(sector + 1) : _EndOfChain);
    }
  }

  private static void _WriteFat(
    byte[] result, int fatSectorCount, int difatSectorCount,
    int directoryStart, int directorySectorCount,
    int miniFatStart, int miniFatSectorCount,
    int miniStreamStart, int miniStreamSectorCount,
    int[] regularStarts, int[] regularCounts) {

    var entries = new uint[checked(fatSectorCount * _FatEntriesPerSector)];
    Array.Fill(entries, _FreeSector);

    for (var i = 0; i < fatSectorCount; ++i)
      entries[i] = _FatSector;
    for (var i = 0; i < difatSectorCount; ++i)
      entries[fatSectorCount + i] = _DifatSector;

    _WriteChain(entries, directoryStart, directorySectorCount);
    if (miniFatSectorCount > 0)
      _WriteChain(entries, miniFatStart, miniFatSectorCount);
    if (miniStreamSectorCount > 0)
      _WriteChain(entries, miniStreamStart, miniStreamSectorCount);

    for (var i = 0; i < regularStarts.Length; ++i)
      if (regularCounts[i] > 0)
        _WriteChain(entries, regularStarts[i], regularCounts[i]);

    for (var i = 0; i < entries.Length; ++i) {
      var sector = i / _FatEntriesPerSector;
      var entry = i % _FatEntriesPerSector;
      _WriteUInt32(result, _SectorOffset(sector) + entry * sizeof(uint), entries[i]);
    }
  }

  private void _WriteDirectory(
    byte[] result, int directoryStart, int directorySectorCount,
    int[] miniStarts, int[] regularStarts, int miniStreamStart, int miniStreamLength) {

    var directory = result.AsSpan(_SectorOffset(directoryStart), checked(directorySectorCount * _SectorSize));
    for (var i = 0; i < _nodes.Count; ++i) {
      var node = _nodes[i];
      var entry = directory.Slice(i * _DirectoryEntrySize, _DirectoryEntrySize);
      var name = Encoding.Unicode.GetBytes(node.Name + '\0');
      name.CopyTo(entry);
      BinaryPrimitives.WriteUInt16LittleEndian(entry.Slice(64, 2), (ushort)name.Length);
      entry[66] = node.Type;
      entry[67] = node.Color;
      BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(68, 4), node.Left);
      BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(72, 4), node.Right);
      BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(76, 4), node.Child);
      node.ClassId.TryWriteBytes(entry.Slice(80, 16));

      uint start;
      ulong size;
      if (i == 0) {
        start = miniStreamStart >= 0 ? (uint)miniStreamStart : _EndOfChain;
        size = (ulong)miniStreamLength;
      } else if (node.Type == CompoundFile.EntryStream && node.Data is { } data) {
        start = data.Length == 0
          ? _EndOfChain
          : miniStarts[i] >= 0
            ? (uint)miniStarts[i]
            : (uint)regularStarts[i];
        size = (ulong)data.Length;
      } else {
        start = _EndOfChain;
        size = 0;
      }

      BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(116, 4), start);
      BinaryPrimitives.WriteUInt64LittleEndian(entry.Slice(120, 8), size);
    }
  }

  private static void _WriteMiniFat(byte[] result, int miniFatStart, int miniFatSectorCount, uint[] miniFatEntries) {
    var capacity = checked(miniFatSectorCount * _FatEntriesPerSector);
    for (var i = 0; i < capacity; ++i) {
      var sector = miniFatStart + i / _FatEntriesPerSector;
      var entry = i % _FatEntriesPerSector;
      _WriteUInt32(result, _SectorOffset(sector) + entry * sizeof(uint), i < miniFatEntries.Length ? miniFatEntries[i] : _FreeSector);
    }
  }

  private static void _WriteChain(uint[] entries, int start, int count) {
    for (var i = 0; i < count; ++i)
      entries[start + i] = i + 1 < count ? (uint)(start + i + 1) : _EndOfChain;
  }

  private static int _DivideRoundUp(int value, int divisor)
    => checked((int)(((long)value + divisor - 1) / divisor));

  private static int _SectorOffset(int sector)
    => checked((sector + 1) * _SectorSize);

  private static void _WriteUInt16(byte[] data, int offset, ushort value)
    => BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, sizeof(ushort)), value);

  private static void _WriteUInt32(byte[] data, int offset, uint value)
    => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)), value);

  private sealed class Node(string name, byte type, Guid classId, int parent, byte[]? data) {
    internal string Name { get; } = name;
    internal byte Type { get; } = type;
    internal Guid ClassId { get; } = classId;
    internal int Parent { get; } = parent;
    internal byte[]? Data { get; } = data;
    internal byte Color { get; set; } = _Black;
    internal uint Left { get; set; } = _NoStream;
    internal uint Right { get; set; } = _NoStream;
    internal uint Child { get; set; } = _NoStream;
  }
}
