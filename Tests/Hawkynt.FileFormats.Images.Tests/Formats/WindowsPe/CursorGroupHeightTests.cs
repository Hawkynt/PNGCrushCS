using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace FileFormat.WindowsPe.Tests;

/// <summary>
/// Holds the RT_GROUP_CURSOR height convention to producers outside this repository.
/// </summary>
/// <remarks>
/// <para>
/// A cursor or icon image is one DIB with the XOR bitmap stacked on top of the AND mask, so
/// <c>BITMAPINFOHEADER.biHeight</c> is twice the height that gets displayed. A standalone
/// <c>.cur</c> file's directory entry states the display height; <c>RT_GROUP_CURSOR</c>'s
/// <c>CURSORDIR</c> states the doubled one. <c>RT_GROUP_ICON</c>'s <c>ICONRESDIR</c> does not
/// double, which is why the two directories disagree about the same picture.
/// </para>
/// <para>
/// Only the first half of that is documented. Microsoft's <c>CURSORDIR</c> reference calls the
/// field "the height of the cursor, in pixels" and mentions no doubling; read literally it denies
/// this fixture. Which is why these tests exist as tests rather than as a comment citing a page:
/// the convention is what Microsoft's tools emit, and the tools are here to be asked.
/// </para>
/// <para>
/// None of that can be checked by writing a PE and reading it back: this package got the doubling
/// wrong in the writer and the halving wrong in the reader at the same time, the two errors were
/// exact inverses, and every round trip came back clean while the files agreed with nothing
/// Microsoft produces. So the evidence here is external in both directions -- what rc.exe and
/// link.exe made of a cursor, what shipped Windows binaries contain, and what the Windows loader
/// sees in a PE this writer produced.
/// </para>
/// </remarks>
[TestFixture]
public sealed class CursorGroupHeightTests {

  private const int _RtCursor = 1;
  private const int _RtIcon = 3;
  private const int _RtGroupCursor = 12;
  private const int _RtGroupIcon = 14;

  private static string _FixturePath(string name)
    => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "WindowsPe", name);

  private static byte[] _Fixture(string name) => File.ReadAllBytes(_FixturePath(name));

  /// <summary>
  /// The reader, handed a DLL link.exe built, has to hand back the directory the .cur started with.
  /// </summary>
  /// <remarks>
  /// The 128-pixel entry is the one that matters. Microsoft states it in the group directory as
  /// 256; a reader that copies that number into a <c>.cur</c>, where the field is a single byte,
  /// writes 0 -- and 0 in that field means 256 by the format's own convention, so the corruption
  /// changes the meaning of the entry rather than merely its value.
  /// </remarks>
  [Test]
  public void Reader_CursorGroupFromLinkExe_RebuildsTheSourceCurDirectory() {
    var source = _Fixture("oracle-cursor.cur");
    var produced = PeResourceReader.FromBytes(_Fixture("oracle.dll"));

    var cursors = new List<PeImageResource>();
    foreach (var resource in produced.ImageResources)
      if (resource.ResourceType == PeImageResourceType.Cursor)
        cursors.Add(resource);

    Assert.That(cursors, Has.Count.EqualTo(1), "the fixture carries exactly one cursor group");
    var rebuilt = cursors[0].Data;

    var sourceEntries = _ReadCurDirectory(source);
    var rebuiltEntries = _ReadCurDirectory(rebuilt);

    Assert.Multiple(() => {
      Assert.That(rebuiltEntries, Has.Count.EqualTo(sourceEntries.Count));
      for (var i = 0; i < sourceEntries.Count; ++i) {
        Assert.That(rebuiltEntries[i].Width, Is.EqualTo(sourceEntries[i].Width), $"entry {i} width");
        Assert.That(rebuiltEntries[i].Height, Is.EqualTo(sourceEntries[i].Height), $"entry {i} height");
        Assert.That(rebuiltEntries[i].HotspotX, Is.EqualTo(sourceEntries[i].HotspotX), $"entry {i} hotspot x");
        Assert.That(rebuiltEntries[i].HotspotY, Is.EqualTo(sourceEntries[i].HotspotY), $"entry {i} hotspot y");
        Assert.That(rebuiltEntries[i].Payload, Is.EqualTo(sourceEntries[i].Payload), $"entry {i} DIB");
      }

      // Spelled out rather than only compared, so a failure names the convention.
      Assert.That(rebuiltEntries[0].Height, Is.EqualTo(128), "a 128-high cursor must not be written as 0");
      Assert.That(rebuiltEntries[1].Height, Is.EqualTo(32));

      // The raw byte, before _ReadCurDirectory applies the 0-means-256 rule. Copying Microsoft's
      // 256 straight into this single-byte field stores 0, and 0 here means 256 -- so the entry
      // would come back claiming a size it never had rather than merely a wrong number. Asserted
      // literally so that a regression reports "But was: 0" and names the overflow.
      Assert.That(rebuilt[6 + 1], Is.EqualTo(128), "the 128-pixel entry's height byte");
      Assert.That(rebuilt[6 + 16 + 1], Is.EqualTo(32), "the 32-pixel entry's height byte");
    });
  }

  /// <summary>
  /// The group directory the writer emits has to be the one link.exe emits for the same cursor.
  /// </summary>
  /// <remarks>
  /// Parsed here with this fixture's own minimal PE walk rather than with
  /// <see cref="PeResourceReader"/>, because a reader that shares the writer's mistake cannot
  /// witness against it.
  /// </remarks>
  [Test]
  public void Writer_CursorGroup_StatesTheHeightLinkExeStates() {
    var written = PeResourceWriter.ToBytes(new PeResourceFile {
      ImageResources = [
        new PeImageResource {
          ResourceType = PeImageResourceType.Cursor,
          ResourceId = 1,
          Data = _Fixture("oracle-cursor.cur"),
        }
      ],
      ModuleKind = PeResourceModuleKind.Dll,
    });

    var ours = _ReadGroupDirectory(written, _RtGroupCursor);
    var microsofts = _ReadGroupDirectory(_Fixture("oracle.dll"), _RtGroupCursor);

    Assert.Multiple(() => {
      Assert.That(ours, Has.Count.EqualTo(microsofts.Count));
      for (var i = 0; i < microsofts.Count; ++i) {
        Assert.That(ours[i].Width, Is.EqualTo(microsofts[i].Width), $"entry {i} CURSORDIR.wWidth");
        Assert.That(ours[i].Height, Is.EqualTo(microsofts[i].Height), $"entry {i} CURSORDIR.wHeight");
        Assert.That(ours[i].Planes, Is.EqualTo(microsofts[i].Planes), $"entry {i} wPlanes");
        Assert.That(ours[i].BitCount, Is.EqualTo(microsofts[i].BitCount), $"entry {i} wBitCount");
      }

      Assert.That(ours[0].Height, Is.EqualTo(256), "a 128-high cursor is 256 in CURSORDIR");
      Assert.That(ours[1].Height, Is.EqualTo(64), "a 32-high cursor is 64 in CURSORDIR");
    });
  }

  /// <summary>
  /// RT_GROUP_ICON does not double, and must not start doing so because the cursor path does.
  /// </summary>
  /// <remarks>
  /// The fixture's icon and its cursor are the same two geometries built by the same rc.exe run,
  /// so this is the doubling and its absence measured against one tool at one moment.
  /// </remarks>
  [Test]
  public void IconGroupFromLinkExe_KeepsTheUndoubledHeight() {
    var microsofts = _ReadGroupDirectory(_Fixture("oracle.dll"), _RtGroupIcon);
    var cursors = _ReadGroupDirectory(_Fixture("oracle.dll"), _RtGroupCursor);

    Assert.Multiple(() => {
      Assert.That(microsofts, Has.Count.EqualTo(2));
      Assert.That(microsofts[0].Height, Is.EqualTo(128), "ICONRESDIR states the display height");
      Assert.That(microsofts[1].Height, Is.EqualTo(32));
      Assert.That(cursors[0].Height, Is.EqualTo(256), "CURSORDIR states the DIB height");
      Assert.That(cursors[1].Height, Is.EqualTo(64));
    });
  }

  /// <summary>The icon reader keeps the height link.exe wrote, undoubled.</summary>
  [Test]
  public void Reader_IconGroupFromLinkExe_RebuildsTheSourceIcoDirectory() {
    var source = _Fixture("oracle-icon.ico");
    var produced = PeResourceReader.FromBytes(_Fixture("oracle.dll"));

    byte[]? rebuilt = null;
    foreach (var resource in produced.ImageResources)
      if (resource.ResourceType == PeImageResourceType.Icon)
        rebuilt = resource.Data;

    Assert.That(rebuilt, Is.Not.Null);
    var sourceEntries = _ReadCurDirectory(source);
    var rebuiltEntries = _ReadCurDirectory(rebuilt!);

    Assert.Multiple(() => {
      Assert.That(rebuiltEntries, Has.Count.EqualTo(sourceEntries.Count));
      for (var i = 0; i < sourceEntries.Count; ++i) {
        Assert.That(rebuiltEntries[i].Width, Is.EqualTo(sourceEntries[i].Width), $"entry {i} width");
        Assert.That(rebuiltEntries[i].Height, Is.EqualTo(sourceEntries[i].Height), $"entry {i} height");
        Assert.That(rebuiltEntries[i].Payload, Is.EqualTo(sourceEntries[i].Payload), $"entry {i} DIB");
      }

      Assert.That(rebuiltEntries[0].Height, Is.EqualTo(128));
      Assert.That(rebuiltEntries[1].Height, Is.EqualTo(32));
    });
  }

  /// <summary>
  /// Every cursor group in a shipped Windows binary states the height of the DIB beside it, and
  /// what the reader extracts from it is half of that.
  /// </summary>
  /// <remarks>
  /// A producer with no connection to the rc.exe run that built the fixture: whatever built
  /// <c>user32.dll</c> did so years ago on Microsoft's own machines.
  /// </remarks>
  [Test]
  [Platform("Win", Reason = "Reads the cursor groups out of the running system's own user32.dll.")]
  public void Reader_ShippedSystemBinary_AgreesWithItsCursorGroups() {
    var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "user32.dll");
    if (!File.Exists(path))
      Assert.Fail($"{path} is missing on a Windows machine.");

    var system = File.ReadAllBytes(path);
    var groups = _ReadGroupDirectory(system, _RtGroupCursor);
    Assert.That(groups, Is.Not.Empty, "user32.dll carries cursor groups");

    var components = _ReadComponents(system, _RtCursor);
    var checkedEntries = 0;

    Assert.Multiple(() => {
      foreach (var entry in groups) {
        if (!components.TryGetValue(entry.ComponentId, out var component) || component.Length < 4 + 12)
          continue;

        // RT_CURSOR is a 4-byte hotspot followed by the DIB.
        var biHeight = BinaryPrimitives.ReadInt32LittleEndian(component.AsSpan(4 + 8));
        Assert.That(entry.Height, Is.EqualTo(biHeight), $"cursor {entry.ComponentId}: CURSORDIR.wHeight is biHeight");
        Assert.That(entry.Height % 2, Is.Zero, $"cursor {entry.ComponentId}: a stacked DIB height is even");
        ++checkedEntries;
      }
    });

    Assert.That(checkedEntries, Is.GreaterThan(10), "enough cursor groups were resolvable to mean something");
  }

  /// <summary>
  /// The Windows loader, reading a DLL this writer produced, finds the height Microsoft writes.
  /// </summary>
  /// <remarks>
  /// <c>LOAD_LIBRARY_AS_DATAFILE</c> because the writer emits PE32/i386 and the test process is
  /// normally 64-bit; the resource loader does not care, which is the part under test.
  /// </remarks>
  [Test]
  [Platform("Win", Reason = "Asks the Windows resource loader what it finds in our PE.")]
  public void Writer_CursorGroup_ReadsBackThroughTheWindowsLoader() {
    var written = PeResourceWriter.ToBytes(new PeResourceFile {
      ImageResources = [
        new PeImageResource {
          ResourceType = PeImageResourceType.Cursor,
          ResourceId = 1,
          Data = _Fixture("oracle-cursor.cur"),
        }
      ],
      ModuleKind = PeResourceModuleKind.Dll,
    });

    var temporary = Path.Combine(Path.GetTempPath(), $"pe-cursor-{Guid.NewGuid():N}.dll");
    File.WriteAllBytes(temporary, written);
    try {
      var group = _LoadGroupThroughWindows(temporary, _RtGroupCursor, 1);
      var entries = _ParseGroupEntries(group);

      Assert.Multiple(() => {
        Assert.That(entries, Has.Count.EqualTo(2));
        Assert.That(entries[0].Width, Is.EqualTo(128));
        Assert.That(entries[0].Height, Is.EqualTo(256), "the loader must see the doubled height");
        Assert.That(entries[1].Width, Is.EqualTo(32));
        Assert.That(entries[1].Height, Is.EqualTo(64));
      });
    } finally {
      try {
        File.Delete(temporary);
      } catch (IOException) {
        // A file the loader still holds open is not this test's problem.
      }
    }
  }

  // ---------------------------------------------------------------------------------------------
  // A deliberately separate, minimal PE walk. PeResourceReader must not be used to judge
  // PeResourceWriter: that is exactly the circularity that let this defect live.
  // ---------------------------------------------------------------------------------------------

  private readonly record struct DirectoryEntry(int Width, int Height, int Planes, int BitCount, int ComponentId);

  private readonly record struct CurEntry(int Width, int Height, int HotspotX, int HotspotY, byte[] Payload);

  private static List<CurEntry> _ReadCurDirectory(byte[] file) {
    var count = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(4));
    var result = new List<CurEntry>(count);
    for (var i = 0; i < count; ++i) {
      var entry = 6 + i * 16;
      var size = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(entry + 8));
      var offset = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(entry + 12));
      result.Add(new CurEntry(
        file[entry] == 0 ? 256 : file[entry],
        file[entry + 1] == 0 ? 256 : file[entry + 1],
        BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(entry + 4)),
        BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(entry + 6)),
        file.AsSpan(offset, size).ToArray()
      ));
    }

    return result;
  }

  private static List<DirectoryEntry> _ReadGroupDirectory(byte[] pe, int typeId) {
    var result = new List<DirectoryEntry>();
    foreach (var (_, payload) in _ReadResources(pe, typeId))
      result.AddRange(_ParseGroupEntries(payload));

    return result;
  }

  private static List<DirectoryEntry> _ParseGroupEntries(byte[] group) {
    var count = BinaryPrimitives.ReadUInt16LittleEndian(group.AsSpan(4));
    var result = new List<DirectoryEntry>(count);
    var isCursor = BinaryPrimitives.ReadUInt16LittleEndian(group.AsSpan(2)) == 2;
    for (var i = 0; i < count; ++i) {
      var entry = 6 + i * 14;

      // ICONRESDIR packs width/height into two bytes, CURSORDIR into two WORDs, over the same four
      // bytes of RESDIR's union.
      var width = isCursor ? BinaryPrimitives.ReadUInt16LittleEndian(group.AsSpan(entry)) : group[entry];
      var height = isCursor ? BinaryPrimitives.ReadUInt16LittleEndian(group.AsSpan(entry + 2)) : group[entry + 1];
      result.Add(new DirectoryEntry(
        width,
        height,
        BinaryPrimitives.ReadUInt16LittleEndian(group.AsSpan(entry + 4)),
        BinaryPrimitives.ReadUInt16LittleEndian(group.AsSpan(entry + 6)),
        BinaryPrimitives.ReadUInt16LittleEndian(group.AsSpan(entry + 12))
      ));
    }

    return result;
  }

  private static Dictionary<int, byte[]> _ReadComponents(byte[] pe, int typeId) {
    var result = new Dictionary<int, byte[]>();
    foreach (var (id, payload) in _ReadResources(pe, typeId))
      result[id] = payload;

    return result;
  }

  /// <summary>Walks the three-level resource tree of a PE and yields one leaf per resource ID.</summary>
  private static List<(int Id, byte[] Payload)> _ReadResources(byte[] pe, int typeId) {
    var result = new List<(int, byte[])>();
    var peOffset = BinaryPrimitives.ReadInt32LittleEndian(pe.AsSpan(60));
    var coff = peOffset + 4;
    var sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(coff + 2));
    var optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(coff + 16));
    var optional = coff + 20;
    var magic = BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(optional));
    var dataDirectory = optional + (magic == 0x20B ? 112 : 96);
    var resourceRva = BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(dataDirectory + 2 * 8));
    if (resourceRva == 0)
      return result;

    var sectionTable = optional + optionalSize;
    var sections = new (uint VirtualAddress, uint Size, uint RawOffset)[sectionCount];
    for (var i = 0; i < sectionCount; ++i) {
      var header = sectionTable + i * 40;
      sections[i] = (
        BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(header + 12)),
        Math.Max(
          BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(header + 8)),
          BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(header + 16))
        ),
        BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(header + 20))
      );
    }

    int Map(uint rva) {
      foreach (var section in sections)
        if (rva >= section.VirtualAddress && rva - section.VirtualAddress < section.Size)
          return (int)(rva - section.VirtualAddress + section.RawOffset);

      return -1;
    }

    var root = Map(resourceRva);
    if (root < 0)
      return result;

    List<(int Id, int Offset, bool IsDirectory)> Entries(int directory) {
      var entries = new List<(int, int, bool)>();
      var total = BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(directory + 12))
                  + BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(directory + 14));
      for (var i = 0; i < total; ++i) {
        var entry = directory + 16 + i * 8;
        var nameOrId = BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(entry));
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(entry + 4));
        entries.Add(((int)(nameOrId & 0x7FFFFFFF), (int)(offset & 0x7FFFFFFF), (offset & 0x80000000) != 0));
      }

      return entries;
    }

    foreach (var (id, typeOffset, isDirectory) in Entries(root)) {
      if (!isDirectory || id != typeId)
        continue;

      foreach (var (resourceId, nameOffset, nameIsDirectory) in Entries(root + typeOffset)) {
        var leaf = nameIsDirectory ? -1 : root + nameOffset;
        if (nameIsDirectory)
          foreach (var (_, languageOffset, languageIsDirectory) in Entries(root + nameOffset)) {
            if (languageIsDirectory)
              continue;

            leaf = root + languageOffset;
            break;
          }

        if (leaf < 0)
          continue;

        var dataOffset = Map(BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(leaf)));
        var dataSize = BinaryPrimitives.ReadInt32LittleEndian(pe.AsSpan(leaf + 4));
        if (dataOffset < 0 || dataSize <= 0 || dataOffset > pe.Length - dataSize)
          continue;

        result.Add((resourceId, pe.AsSpan(dataOffset, dataSize).ToArray()));
      }
    }

    return result;
  }

  // ---------------------------------------------------------------------------------------------
  // The Windows resource loader, asked directly. Test-only interop; the format libraries stay
  // purely managed.
  // ---------------------------------------------------------------------------------------------

  private const uint _LoadLibraryAsDatafile = 0x00000002;

  [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
  private static extern IntPtr LoadLibraryExW(string fileName, IntPtr reserved, uint flags);

  [DllImport("kernel32", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool FreeLibrary(IntPtr module);

  [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
  private static extern IntPtr FindResourceW(IntPtr module, IntPtr name, IntPtr type);

  [DllImport("kernel32", SetLastError = true)]
  private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);

  [DllImport("kernel32")]
  private static extern IntPtr LockResource(IntPtr resourceData);

  [DllImport("kernel32", SetLastError = true)]
  private static extern uint SizeofResource(IntPtr module, IntPtr resource);

  private static byte[] _LoadGroupThroughWindows(string path, int typeId, int resourceId) {
    var module = LoadLibraryExW(path, IntPtr.Zero, _LoadLibraryAsDatafile);
    if (module == IntPtr.Zero)
      Assert.Fail($"LoadLibraryEx refused the produced DLL: Win32 error {Marshal.GetLastWin32Error()}.");

    try {
      var handle = FindResourceW(module, resourceId, typeId);
      if (handle == IntPtr.Zero)
        Assert.Fail($"FindResource found no resource {typeId}/{resourceId}: Win32 error {Marshal.GetLastWin32Error()}.");

      var size = SizeofResource(module, handle);
      var data = LockResource(LoadResource(module, handle));
      if (size == 0 || data == IntPtr.Zero)
        Assert.Fail($"LoadResource/LockResource returned nothing for {typeId}/{resourceId}.");

      var result = new byte[size];
      Marshal.Copy(data, result, 0, result.Length);
      return result;
    } finally {
      // LOAD_LIBRARY_AS_DATAFILE maps the file; it has to be unmapped before it can be deleted.
      FreeLibrary(module);
    }
  }
}
