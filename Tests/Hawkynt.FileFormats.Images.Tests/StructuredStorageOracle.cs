using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>
/// Opens a Compound File Binary container with the implementation Windows itself ships, and reports
/// what that implementation found in it.
/// </summary>
/// <remarks>
/// Four formats in this package write CFB containers — FlashPix, and the legacy binary forms of
/// Word, Excel and PowerPoint — and all four go through one writer. Every test that had ever looked
/// inside one of those files read it back with the reader sitting beside that writer, which is the
/// circular check this repository keeps paying for: the two halves share one reading of the format
/// and agree with each other perfectly while nothing else can open the file.
/// <para/>
/// <c>ole32.dll</c> is the other party. Structured storage is Microsoft's own implementation of the
/// container that [MS-CFB] documents, it is the one the applications these files target sit on, and
/// it is on every Windows machine, so no download or install stands between this suite and an
/// outside opinion. It reads the header, walks the DIFAT, the FAT, the MiniFAT and the red/black
/// directory tree, and refuses files whose chains or sibling trees do not hold together — which is
/// exactly the part of a container that a writer and its own reader can get wrong together.
/// <para/>
/// What it does <em>not</em> answer: whether Word opens the .doc. Structured storage sees a
/// container and its streams; the FIB, the piece table and the BIFF record stream inside them mean
/// nothing to it. That claim needs the application, or LibreOffice standing in for it, and is
/// recorded as unverified until one of them has been asked.
/// <para/>
/// Everywhere but Windows this is absent rather than failing, on the rule the rest of the oracles
/// here follow: a machine that has not got the tool has not disagreed, it has not been asked.
/// </remarks>
internal static class StructuredStorageOracle {

  private const uint _STGM_READ_SHARE_EXCLUSIVE = 0x00000000 | 0x00000010;
  private const int _S_OK = 0;

  /// <summary>Whether this machine has an implementation to ask.</summary>
  public static bool IsAvailable => OperatingSystem.IsWindows();

  /// <summary>One element structured storage found directly under the root.</summary>
  /// <param name="Name">Its name, as the directory entry spells it.</param>
  /// <param name="IsStream">Whether it is a stream rather than a storage.</param>
  /// <param name="Length">The declared byte length, for a stream.</param>
  internal readonly record struct Element(string Name, bool IsStream, long Length);

  /// <summary>
  /// Hands a container to structured storage and returns what it enumerated under the root, or
  /// skips the calling test where there is no structured storage to ask.
  /// </summary>
  /// <param name="container">The bytes this package wrote.</param>
  /// <returns>The root's elements, in the order the enumerator produced them.</returns>
  public static IReadOnlyList<Element> RootElementsOrIgnore(byte[] container) {
    var (storage, path) = _OpenOrIgnore(container);
    try {
      return _Enumerate(storage);
    } finally {
      _Release(storage, path);
    }
  }

  /// <summary>
  /// Asks structured storage to read one stream out of a container, or skips the calling test where
  /// there is no structured storage to ask.
  /// </summary>
  /// <param name="container">The bytes this package wrote.</param>
  /// <param name="name">The stream wanted, as the writer named it.</param>
  /// <returns>Everything that stream holds, as structured storage walked its chain.</returns>
  public static byte[] ReadStreamOrIgnore(byte[] container, string name) {
    var (storage, path) = _OpenOrIgnore(container);
    try {
      return _ReadStream(storage, name);
    } finally {
      _Release(storage, path);
    }
  }

  /// <summary>
  /// Asks structured storage what class the root storage declares, or skips the calling test where
  /// there is no structured storage to ask.
  /// </summary>
  /// <param name="container">The bytes this package wrote.</param>
  /// <returns>The root CLSID, which is how a shell or an application picks the program that owns the file.</returns>
  public static Guid RootClassIdOrIgnore(byte[] container) {
    var (storage, path) = _OpenOrIgnore(container);
    try {
      storage.Stat(out var status, 1 /* STATFLAG_NONAME */);
      return status.clsid;
    } finally {
      _Release(storage, path);
    }
  }

  [SupportedOSPlatform("windows")]
  private static IReadOnlyList<Element> _Enumerate(IStorage storage) {
    storage.EnumElements(0, IntPtr.Zero, 0, out var enumerator);
    try {
      var result = new List<Element>();
      var found = new ComTypes.STATSTG[1];
      while (enumerator.Next(1, found, out var fetched) == _S_OK && fetched == 1)
        result.Add(new(found[0].pwcsName, found[0].type == 2, found[0].cbSize));

      return result;
    } finally {
      Marshal.ReleaseComObject(enumerator);
    }
  }

  [SupportedOSPlatform("windows")]
  private static byte[] _ReadStream(IStorage storage, string name) {
    storage.OpenStream(name, IntPtr.Zero, _STGM_READ_SHARE_EXCLUSIVE, 0, out var stream);
    try {
      stream.Stat(out var status, 1 /* STATFLAG_NONAME */);
      var result = new byte[status.cbSize];
      if (result.Length == 0)
        return result;

      // IStream is free to answer with less than was asked for, so this keeps asking until it stops
      // producing bytes rather than treating one short answer as a disagreement.
      var read = Marshal.AllocCoTaskMem(sizeof(int));
      try {
        var at = 0;
        while (at < result.Length) {
          var chunk = new byte[result.Length - at];
          stream.Read(chunk, chunk.Length, read);
          var count = Marshal.ReadInt32(read);
          if (count <= 0)
            throw new IOException($"Structured storage stopped after {at} of the {result.Length} bytes it said '{name}' holds.");

          Array.Copy(chunk, 0, result, at, count);
          at += count;
        }
      } finally {
        Marshal.FreeCoTaskMem(read);
      }

      return result;
    } finally {
      Marshal.ReleaseComObject(stream);
    }
  }

  [SupportedOSPlatform("windows")]
  private static void _Release(IStorage storage, string path) {
    Marshal.ReleaseComObject(storage);
    try {
      File.Delete(path);
    } catch (IOException) {
      // A leftover temporary file is not a test result.
    }
  }

  private static (IStorage Storage, string Path) _OpenOrIgnore(byte[] container) {
    ArgumentNullException.ThrowIfNull(container);
    if (!IsAvailable)
      Assert.Ignore("no structured storage on this machine to ask");

    var path = Path.Combine(Path.GetTempPath(), $"cfb-oracle-{Guid.NewGuid():N}.bin");
    File.WriteAllBytes(path, container);
    try {
      var isStorage = StgIsStorageFile(path);
      if (isStorage != _S_OK)
        throw new IOException($"Structured storage does not consider this a compound file (HRESULT 0x{isStorage:X8}).");

      var opened = StgOpenStorage(path, null, _STGM_READ_SHARE_EXCLUSIVE, IntPtr.Zero, 0, out var storage);
      if (opened != _S_OK)
        throw new IOException($"Structured storage would not open this compound file (HRESULT 0x{opened:X8}).");

      return (storage, path);
    } catch {
      File.Delete(path);
      throw;
    }
  }

  [DllImport("ole32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
  private static extern int StgIsStorageFile([MarshalAs(UnmanagedType.LPWStr)] string name);

  [DllImport("ole32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
  private static extern int StgOpenStorage(
    [MarshalAs(UnmanagedType.LPWStr)] string name,
    IStorage? priority,
    uint mode,
    IntPtr excluded,
    uint reserved,
    out IStorage storage);

  /// <summary>As much of <c>IStorage</c> as this needs, with the slots before it kept in order.</summary>
  /// <remarks>
  /// A COM interface is a vtable, so every method ahead of one that gets called has to occupy its
  /// slot even when nothing here ever invokes it. The unused slots are declared as bare
  /// <see cref="void"/> methods for that reason and for no other; calling one would not do what its
  /// name says.
  /// </remarks>
  [ComImport]
  [Guid("0000000B-0000-0000-C000-000000000046")]
  [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  private interface IStorage {

    void _CreateStream();

    void OpenStream(
      [MarshalAs(UnmanagedType.LPWStr)] string name,
      IntPtr reserved1,
      uint mode,
      uint reserved2,
      out ComTypes.IStream stream);

    void _CreateStorage();

    void _OpenStorage();

    void _CopyTo();

    void _MoveElementTo();

    void _Commit();

    void _Revert();

    void EnumElements(uint reserved1, IntPtr reserved2, uint reserved3, out IEnumSTATSTG enumerator);

    void _DestroyElement();

    void _RenameElement();

    void _SetElementTimes();

    void _SetClass();

    void _SetStateBits();

    void Stat(out ComTypes.STATSTG status, uint flags);
  }

  /// <summary>
  /// <c>IEnumSTATSTG</c>, which .NET declares only for the Windows-only surface this project does
  /// not target.
  /// </summary>
  [ComImport]
  [Guid("0000000D-0000-0000-C000-000000000046")]
  [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  private interface IEnumSTATSTG {

    [PreserveSig]
    int Next(uint wanted, [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] ComTypes.STATSTG[] found, out uint fetched);

    [PreserveSig]
    int Skip(uint count);

    [PreserveSig]
    int Reset();

    [PreserveSig]
    int Clone(out IEnumSTATSTG enumerator);
  }
}
