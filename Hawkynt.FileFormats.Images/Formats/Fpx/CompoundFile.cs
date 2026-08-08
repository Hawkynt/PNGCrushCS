using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Fpx;

/// <summary>Reads a Microsoft Compound File Binary container: the header, the allocation tables and the directory.</summary>
/// <remarks>
/// A compound file is a filesystem in a file. Everything past the 512-byte header is divided into
/// sectors, and a stream is a chain of them threaded through the file allocation table — the same
/// idea as a FAT disk, one entry per sector holding the number of the next. Streams shorter than
/// the header's cutoff live in a second, finer allocation inside a stream of the root entry, so
/// there are two tables and two sector sizes.
/// <para/>
/// The structure is Microsoft's [MS-CFB], and it is what a FlashPix picture, a Picture It! document
/// and a PhotoDraw document are all wrapped in.
/// <para/>
/// Every field that says how far to go is checked before it is followed: a sector number past the
/// end of the file, a chain that visits a sector twice, a directory entry naming more bytes than
/// its chain can hold. That is what lets a caller tell a compound file from something that merely
/// starts with the same eight bytes.
/// </remarks>
internal sealed class CompoundFile {

  /// <summary>The eight bytes every compound file opens with.</summary>
  internal static ReadOnlySpan<byte> Signature => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

  private const int _HeaderSize = 512;
  private const int _DirectoryEntrySize = 128;

  /// <summary>Marks the end of a chain.</summary>
  private const uint _EndOfChain = 0xFFFFFFFE;

  /// <summary>The lowest value that is a marker rather than a sector number.</summary>
  private const uint _FirstSpecial = 0xFFFFFFFA;

  /// <summary>How many allocation table sectors the header itself lists.</summary>
  private const int _HeaderTableEntries = 109;

  /// <summary>A storage: a directory with other entries under it.</summary>
  internal const byte EntryStorage = 1;

  /// <summary>A stream: bytes.</summary>
  internal const byte EntryStream = 2;

  /// <summary>The root, whose own chain holds every short stream.</summary>
  internal const byte EntryRoot = 5;

  private readonly byte[] _data;
  private readonly int _sectorSize;
  private readonly int _miniSectorSize;
  private readonly uint _miniStreamCutoff;
  private readonly uint[] _fat;
  private readonly uint[] _miniFat;
  private readonly byte[] _miniStream;

  /// <summary>One entry of the directory.</summary>
  internal readonly record struct Entry(string Name, byte Type, uint Start, long Size, uint Left, uint Right, uint Child);

  private readonly Entry[] _entries;

  internal static bool HasSignature(ReadOnlySpan<byte> data)
    => data.Length >= Signature.Length && data[..Signature.Length].SequenceEqual(Signature);

  internal CompoundFile(ReadOnlySpan<byte> data) {

    if (!HasSignature(data))
      throw new InvalidDataException("Not a compound file: it does not open with the eight-byte signature.");

    if (data.Length < _HeaderSize)
      throw new InvalidDataException(
        $"Compound file too small: its header takes {_HeaderSize} bytes and the file is {data.Length}.");

    var sectorShift = BinaryPrimitives.ReadUInt16LittleEndian(data[30..]);
    var miniSectorShift = BinaryPrimitives.ReadUInt16LittleEndian(data[32..]);
    if (sectorShift is < 7 or > 20 || miniSectorShift is < 4 or > 12 || miniSectorShift >= sectorShift)
      throw new InvalidDataException(
        $"Compound file states sector sizes of 2^{sectorShift} and 2^{miniSectorShift}, which are not usable.");

    this._data = data.ToArray();
    this._sectorSize = 1 << sectorShift;
    this._miniSectorSize = 1 << miniSectorShift;
    this._miniStreamCutoff = BinaryPrimitives.ReadUInt32LittleEndian(data[56..]);

    var tableSectorCount = BinaryPrimitives.ReadUInt32LittleEndian(data[44..]);
    var directoryStart = BinaryPrimitives.ReadUInt32LittleEndian(data[48..]);
    var miniTableStart = BinaryPrimitives.ReadUInt32LittleEndian(data[60..]);
    var extensionStart = BinaryPrimitives.ReadUInt32LittleEndian(data[68..]);
    var extensionCount = BinaryPrimitives.ReadUInt32LittleEndian(data[72..]);

    var sectors = (this._data.Length - _HeaderSize) / this._sectorSize;
    if (sectors <= 0)
      throw new InvalidDataException("Compound file holds no sectors behind its header.");

    if (tableSectorCount > sectors)
      throw new InvalidDataException(
        $"Compound file states {tableSectorCount} allocation table sectors in a file holding {sectors}.");

    this._fat = this._ReadAllocationTable(tableSectorCount, extensionStart, extensionCount, sectors);
    this._entries = this._ReadDirectory(directoryStart);

    var root = this._entries[0];
    this._miniStream = root.Size > 0 ? this._ReadChain(root.Start, root.Size) : [];
    this._miniFat = miniTableStart < _FirstSpecial
      ? _AsSectorNumbers(this._ReadChain(miniTableStart, long.MaxValue))
      : [];
  }

  /// <summary>Every stream in the file, named by the path of storages leading to it.</summary>
  internal IEnumerable<KeyValuePair<string, Entry>> Streams() {
    var found = new List<KeyValuePair<string, Entry>>();
    var seen = new HashSet<uint>();
    this._Walk(this._entries[0].Child, string.Empty, found, seen);
    return found;
  }

  /// <summary>The bytes of one entry.</summary>
  internal byte[] Read(Entry entry)
    => entry.Size < this._miniStreamCutoff
      ? this._ReadMiniChain(entry.Start, entry.Size)
      : this._ReadChain(entry.Start, entry.Size);

  private void _Walk(uint at, string path, List<KeyValuePair<string, Entry>> found, HashSet<uint> seen) {
    if (at >= _FirstSpecial || at >= this._entries.Length || !seen.Add(at))
      return;

    var entry = this._entries[at];
    this._Walk(entry.Left, path, found, seen);

    var here = path + "/" + entry.Name;
    found.Add(new(here, entry));
    if (entry.Type is EntryStorage or EntryRoot)
      this._Walk(entry.Child, here, found, seen);

    this._Walk(entry.Right, path, found, seen);
  }

  private uint[] _ReadAllocationTable(uint tableSectorCount, uint extensionStart, uint extensionCount, int sectors) {
    var perSector = this._sectorSize / 4;
    var list = new List<uint>(_HeaderTableEntries + (int)extensionCount * perSector);

    for (var i = 0; i < _HeaderTableEntries; ++i)
      list.Add(BinaryPrimitives.ReadUInt32LittleEndian(this._data.AsSpan(76 + i * 4)));

    var at = extensionStart;
    var seen = new HashSet<uint>();
    for (var i = 0; i < extensionCount && at < _FirstSpecial; ++i) {
      if (!seen.Add(at))
        throw new InvalidDataException("Compound file's allocation table extension chain visits a sector twice.");

      var offset = this._SectorOffset(at);
      for (var j = 0; j < perSector - 1; ++j)
        list.Add(BinaryPrimitives.ReadUInt32LittleEndian(this._data.AsSpan(offset + j * 4)));

      at = BinaryPrimitives.ReadUInt32LittleEndian(this._data.AsSpan(offset + this._sectorSize - 4));
    }

    var table = new List<uint>(sectors + perSector);
    var used = 0;
    foreach (var sector in list) {
      if (used >= tableSectorCount)
        break;

      ++used;
      if (sector >= _FirstSpecial)
        continue;

      var offset = this._SectorOffset(sector);
      for (var j = 0; j < perSector; ++j)
        table.Add(BinaryPrimitives.ReadUInt32LittleEndian(this._data.AsSpan(offset + j * 4)));
    }

    if (table.Count == 0)
      throw new InvalidDataException("Compound file has no allocation table.");

    return table.ToArray();
  }

  private Entry[] _ReadDirectory(uint directoryStart) {
    var directory = this._ReadChain(directoryStart, long.MaxValue);
    if (directory.Length < _DirectoryEntrySize)
      throw new InvalidDataException("Compound file's directory is shorter than one entry.");

    var entries = new Entry[directory.Length / _DirectoryEntrySize];
    for (var i = 0; i < entries.Length; ++i) {
      var at = i * _DirectoryEntrySize;
      var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(directory.AsSpan(at + 64));
      if (nameLength > 64)
        throw new InvalidDataException(
          $"Compound file's directory entry {i} states a name of {nameLength} bytes where 64 is the most it can be.");

      var name = nameLength >= 2
        ? System.Text.Encoding.Unicode.GetString(directory, at, nameLength - 2)
        : string.Empty;

      entries[i] = new(
        name.TrimEnd('\0'),
        directory[at + 66],
        BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(at + 116)),
        (long)BinaryPrimitives.ReadUInt64LittleEndian(directory.AsSpan(at + 120)),
        BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(at + 68)),
        BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(at + 72)),
        BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(at + 76)));
    }

    if (entries[0].Type != EntryRoot)
      throw new InvalidDataException(
        $"Compound file's first directory entry is of type {entries[0].Type} rather than the root's 5.");

    return entries;
  }

  private int _SectorOffset(uint sector) {
    var offset = _HeaderSize + (long)sector * this._sectorSize;
    if (offset < 0 || offset + this._sectorSize > this._data.Length)
      throw new InvalidDataException(
        $"Compound file names sector {sector}, which lies past the end of its {this._data.Length} bytes.");

    return (int)offset;
  }

  private byte[] _ReadChain(uint start, long size) {
    var output = new MemoryStream();
    var at = start;
    var seen = new HashSet<uint>();

    while (at < _FirstSpecial && (size == long.MaxValue || output.Length < size)) {
      if (!seen.Add(at))
        throw new InvalidDataException($"Compound file's sector chain visits sector {at} twice.");

      output.Write(this._data, this._SectorOffset(at), this._sectorSize);
      at = at < this._fat.Length ? this._fat[at] : _EndOfChain;
    }

    var bytes = output.ToArray();
    if (size == long.MaxValue)
      return bytes;

    if (size < 0 || size > bytes.Length)
      throw new InvalidDataException(
        $"Compound file states a stream of {size} bytes whose chain holds {bytes.Length}.");

    return bytes[..(int)size];
  }

  private byte[] _ReadMiniChain(uint start, long size) {
    if (size < 0 || size > this._miniStream.Length)
      throw new InvalidDataException(
        $"Compound file states a short stream of {size} bytes where the whole short area is {this._miniStream.Length}.");

    var output = new byte[(int)size];
    var at = start;
    var filled = 0;
    var seen = new HashSet<uint>();

    while (at < _FirstSpecial && filled < output.Length) {
      if (!seen.Add(at))
        throw new InvalidDataException($"Compound file's short sector chain visits sector {at} twice.");

      var offset = (long)at * this._miniSectorSize;
      if (offset + this._miniSectorSize > this._miniStream.Length)
        throw new InvalidDataException(
          $"Compound file names short sector {at}, which lies past the end of the short area.");

      var take = Math.Min(this._miniSectorSize, output.Length - filled);
      Array.Copy(this._miniStream, (int)offset, output, filled, take);
      filled += take;
      at = at < this._miniFat.Length ? this._miniFat[at] : _EndOfChain;
    }

    if (filled != output.Length)
      throw new InvalidDataException(
        $"Compound file's short stream ran out after {filled} of the {output.Length} bytes it states.");

    return output;
  }

  private static uint[] _AsSectorNumbers(byte[] bytes) {
    var numbers = new uint[bytes.Length / 4];
    for (var i = 0; i < numbers.Length; ++i)
      numbers[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i * 4));

    return numbers;
  }
}
