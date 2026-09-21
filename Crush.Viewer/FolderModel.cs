using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hawkynt.FileFormats.Images;

namespace Crush.Viewer;

/// <summary>What the arrow keys do when they run out of folder.</summary>
internal enum WalkMode {

  /// <summary>Stop on the first and last picture.</summary>
  Stop,

  /// <summary>Carry on from the other end.</summary>
  Wrap,
}

/// <summary>The readable pictures in one folder, in the order a person expects to see them.</summary>
internal sealed class FolderModel {

  private static readonly Lazy<HashSet<string>> _readableExtensions = new(() =>
    FormatRegistry.SupportedReadFormats.SelectMany(e => e.AllExtensions).ToHashSet(StringComparer.OrdinalIgnoreCase));

  private FileInfo[] _files = [];

  /// <summary>The folder currently listed, or <c>null</c> before anything is opened.</summary>
  internal DirectoryInfo? Folder { get; private set; }

  /// <summary>The listed pictures, sorted the way a file manager sorts them.</summary>
  internal IReadOnlyList<FileInfo> Files => this._files;

  /// <summary>The picture being shown, or -1 when none of the listed ones is.</summary>
  internal int Index { get; private set; } = -1;

  /// <summary>Whether walking past an end continues from the other one.</summary>
  internal WalkMode WalkMode { get; set; } = WalkMode.Stop;

  /// <summary>The picture being shown, or <c>null</c>.</summary>
  internal FileInfo? Current => (uint)this.Index < (uint)this._files.Length ? this._files[this.Index] : null;

  /// <summary>Every extension any registered reader claims.</summary>
  internal static IReadOnlySet<string> ReadableExtensions => _readableExtensions.Value;

  /// <summary>Whether a registered reader claims this file's extension.</summary>
  internal static bool IsReadable(FileInfo file) => _readableExtensions.Value.Contains(file.Extension);

  /// <summary>Lists a folder and selects <paramref name="select"/> in it if it is there.</summary>
  internal void Open(DirectoryInfo folder, FileInfo? select = null) {
    this.Folder = folder;
    this._files = _Enumerate(folder);
    this.Select(select);
  }

  /// <summary>Lists the folder again, keeping the current picture selected if it survived.</summary>
  internal void Refresh() {
    if (this.Folder == null)
      return;

    var current = this.Current;
    this._files = _Enumerate(this.Folder);
    this.Select(current);
  }

  /// <summary>Points at a file by path, or at nothing when it is not in the listing.</summary>
  internal void Select(FileInfo? file)
    => this.Index = file == null
      ? -1
      : Array.FindIndex(this._files, f => string.Equals(f.FullName, file.FullName, StringComparison.OrdinalIgnoreCase));

  /// <summary>Points at a file by position.</summary>
  internal void SelectIndex(int index) => this.Index = (uint)index < (uint)this._files.Length ? index : -1;

  /// <summary>
  /// The position <paramref name="delta"/> steps away under the current <see cref="WalkMode"/>,
  /// or -1 when there is nowhere to go.
  /// </summary>
  internal int Step(int delta) {
    if (this._files.Length == 0)
      return -1;

    if (this.Index < 0)
      return delta >= 0 ? 0 : this._files.Length - 1;

    var next = this.Index + delta;
    if (this.WalkMode == WalkMode.Wrap)
      return (next % this._files.Length + this._files.Length) % this._files.Length;

    return next < 0 || next >= this._files.Length ? -1 : next;
  }

  private static FileInfo[] _Enumerate(DirectoryInfo folder) {
    if (!folder.Exists)
      return [];

    try {
      return folder
        .EnumerateFiles()
        .Where(IsReadable)
        .OrderBy(f => f.Name, NaturalStringComparer.Instance)
        .ToArray();
    } catch (UnauthorizedAccessException) {
      return [];
    } catch (IOException) {
      return [];
    }
  }
}

/// <summary>Orders names the way a file manager does, so image10 follows image9 rather than image1.</summary>
internal sealed class NaturalStringComparer : IComparer<string> {

  internal static readonly NaturalStringComparer Instance = new();

  public int Compare(string? x, string? y) {
    if (ReferenceEquals(x, y)) return 0;
    if (x == null) return -1;
    if (y == null) return 1;

    int ix = 0, iy = 0;
    while (ix < x.Length && iy < y.Length) {
      if (char.IsDigit(x[ix]) && char.IsDigit(y[iy])) {
        long nx = 0, ny = 0;
        while (ix < x.Length && char.IsDigit(x[ix])) nx = Math.Min(long.MaxValue / 10, nx) * 10 + (x[ix++] - '0');
        while (iy < y.Length && char.IsDigit(y[iy])) ny = Math.Min(long.MaxValue / 10, ny) * 10 + (y[iy++] - '0');
        var digits = nx.CompareTo(ny);
        if (digits != 0) return digits;
        continue;
      }

      var letters = char.ToUpperInvariant(x[ix++]).CompareTo(char.ToUpperInvariant(y[iy++]));
      if (letters != 0) return letters;
    }

    return x.Length.CompareTo(y.Length);
  }
}
