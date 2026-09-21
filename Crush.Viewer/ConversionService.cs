using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace Crush.Viewer;

/// <summary>What became of one file a conversion was asked to produce.</summary>
internal readonly record struct ConversionResult(FileInfo Source, string Target, bool Succeeded, string Message);

/// <summary>Reads through the registry and writes back through it, one file or a folder at a time.</summary>
/// <remarks>
/// Every decode and encode here goes through <see cref="FormatRegistry"/>; this type owns naming,
/// overwrite policy and error reporting, and nothing else. Conversion is therefore whatever the
/// registry's readers and writers can do between them, which is the point.
/// </remarks>
internal static class ConversionService {

  /// <summary>Decodes one file and re-encodes it as <paramref name="target"/>.</summary>
  internal static ConversionResult Convert(FileInfo source, FormatEntry target, FileInfo destination, bool overwrite) {
    // FileInfo.Exists is answered from a snapshot taken when the object was made, so a destination
    // that was written a moment ago still reports as absent and the overwrite guard lets it through.
    destination.Refresh();
    if (!overwrite && destination.Exists)
      return new(source, destination.FullName, false, "A file of that name is already there.");

    try {
      var raw = FormatRegistry.Read(source);
      if (raw == null)
        return new(source, destination.FullName, false, "No registered reader could decode it.");

      return Write(raw, target, destination, source);
    } catch (Exception ex) {
      return new(source, destination.FullName, false, ex.Message);
    }
  }

  /// <summary>Encodes a picture already in hand.</summary>
  internal static ConversionResult Write(RawImage image, FormatEntry target, FileInfo destination, FileInfo? source = null) {
    var attributed = source ?? destination;
    try {
      destination.Directory?.Create();
      return FormatRegistry.Write(image, target.Format, destination)
        ? new(attributed, destination.FullName, true, $"{image.Width} x {image.Height}")
        : new(attributed, destination.FullName, false, $"The {target.Name} writer refused the picture.");
    } catch (Exception ex) {
      return new(attributed, destination.FullName, false, ex.Message);
    }
  }

  /// <summary>Converts every file in <paramref name="sources"/> into <paramref name="destination"/>.</summary>
  /// <remarks>
  /// Runs to the end whatever happens: one unreadable file in a folder of two hundred is a line in
  /// the report, not a reason to stop and leave the caller guessing how far it got.
  /// </remarks>
  internal static IReadOnlyList<ConversionResult> ConvertBatch(
    IReadOnlyList<FileInfo> sources,
    FormatEntry target,
    DirectoryInfo destination,
    bool overwrite,
    Action<int, int>? progress = null,
    CancellationToken token = default) {
    var results = new List<ConversionResult>(sources.Count);
    var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    destination.Create();

    for (var i = 0; i < sources.Count; ++i) {
      token.ThrowIfCancellationRequested();
      progress?.Invoke(i, sources.Count);
      var source = sources[i];
      var file = new FileInfo(Path.Combine(destination.FullName, _BatchName(source, target, taken)));
      results.Add(Convert(source, target, file, overwrite));
    }

    progress?.Invoke(sources.Count, sources.Count);
    return results;
  }

  /// <summary>
  /// Names one output of a batch, keeping two sources from landing on the same file.
  /// </summary>
  /// <remarks>
  /// Dropping the source extension means <c>shot.png</c> and <c>shot.jpg</c> in one folder both want
  /// to be <c>shot.webp</c> — and with overwriting on, the second would quietly replace the first
  /// while the report claimed both were written. A name already claimed in this run therefore keeps
  /// the source extension in front of the new one, which stays predictable and loses nothing.
  /// </remarks>
  private static string _BatchName(FileInfo source, FormatEntry target, HashSet<string> taken) {
    var name = Path.GetFileNameWithoutExtension(source.Name) + target.PrimaryExtension;
    if (taken.Add(name))
      return name;

    var qualified = source.Name + target.PrimaryExtension;
    if (taken.Add(qualified))
      return qualified;

    for (var suffix = 2; ; ++suffix) {
      var numbered = $"{Path.GetFileNameWithoutExtension(source.Name)}-{suffix}{target.PrimaryExtension}";
      if (taken.Add(numbered))
        return numbered;
    }
  }

  /// <summary>Builds "name.0001.ext" style names so an extracted page sorts back into its own order.</summary>
  internal static FileInfo PageFile(DirectoryInfo destination, string stem, int page, int pageCount, string extension) {
    var digits = Math.Max(2, pageCount.ToString(CultureInfo.InvariantCulture).Length);
    var number = (page + 1).ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
    return new(Path.Combine(destination.FullName, $"{stem}.{number}{extension}"));
  }
}
