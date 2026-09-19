using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace Crush.Viewer;

/// <summary>The pages, frames and sub-images inside one file.</summary>
/// <remarks>
/// <para>
/// Thirty-five registered formats hold more than one picture — icon files, animations, fax and
/// document formats — and the registry exposes them through <see cref="FormatEntry.GetImageCount"/>,
/// <see cref="FormatEntry.LoadRawImageAtIndex"/> and <see cref="FormatEntry.LoadAllRawImages"/>.
/// </para>
/// <para>
/// Going the other way is not possible yet. <c>IMultiImageFileFormat</c> declares
/// <c>ImageCount</c>, <c>ToRawImage</c> and <c>ToRawImages</c> and nothing that takes a list of
/// pictures, and <see cref="FormatEntry.ConvertFromRawImage"/> encodes exactly one picture, so no
/// registered writer can be handed a second page. Assembling a multi-page file needs a writing
/// counterpart on the format side first; guessing at a container here would mean writing an encoder
/// the library already owns the right place for.
/// </para>
/// </remarks>
internal static class PageService {

  /// <summary>Why a multi-page file cannot be assembled, in a sentence fit for a tooltip.</summary>
  internal const string AssemblyUnavailable =
    "The format registry reads multi-page files but cannot write them: IMultiImageFileFormat has no "
    + "counterpart that takes several pictures, and every registered writer encodes exactly one.";

  /// <summary>How many pictures a file holds; 1 for an ordinary single-picture file.</summary>
  internal static int PageCount(FileInfo file, FormatEntry? entry) {
    if (entry?.GetImageCount == null)
      return 1;

    try {
      return Math.Max(1, entry.GetImageCount(file));
    } catch {
      return 1;
    }
  }

  /// <summary>Reads one page out of a file.</summary>
  internal static RawImage? LoadPage(FileInfo file, FormatEntry? entry, int index) {
    if (entry?.LoadRawImageAtIndex is { } atIndex)
      return atIndex(file, index);

    return index <= 0 ? FormatRegistry.Read(file) : null;
  }

  /// <summary>Writes every page of <paramref name="source"/> into its own file.</summary>
  internal static IReadOnlyList<ConversionResult> ExtractPages(
    FileInfo source,
    FormatEntry? sourceEntry,
    FormatEntry target,
    DirectoryInfo destination,
    bool overwrite,
    Action<int, int>? progress = null,
    CancellationToken token = default) {
    destination.Create();
    var stem = Path.GetFileNameWithoutExtension(source.Name);
    var results = new List<ConversionResult>();

    var pages = _ReadAll(source, sourceEntry);
    for (var i = 0; i < pages.Count; ++i) {
      token.ThrowIfCancellationRequested();
      progress?.Invoke(i, pages.Count);

      var file = ConversionService.PageFile(destination, stem, i, pages.Count, target.PrimaryExtension);
      file.Refresh();
      if (!overwrite && file.Exists) {
        results.Add(new(source, file.FullName, false, "A file of that name is already there."));
        continue;
      }

      var page = pages[i];
      results.Add(page == null
        ? new ConversionResult(source, file.FullName, false, $"Page {i + 1} could not be decoded.")
        : ConversionService.Write(page, target, file, source));
    }

    progress?.Invoke(pages.Count, pages.Count);
    return results;
  }

  private static IReadOnlyList<RawImage?> _ReadAll(FileInfo source, FormatEntry? entry) {
    if (entry?.LoadAllRawImages != null)
      try {
        if (entry.LoadAllRawImages(source) is { Count: > 0 } all) {
          var decoded = new RawImage?[all.Count];
          for (var i = 0; i < all.Count; ++i)
            decoded[i] = all[i];
          return decoded;
        }
      } catch {
        // Fall through to the per-page reader, which reports the failing page rather than the file.
      }

    var count = PageCount(source, entry);
    var pages = new RawImage?[count];
    for (var i = 0; i < count; ++i)
      try {
        pages[i] = LoadPage(source, entry, i);
      } catch {
        pages[i] = null;
      }

    return pages;
  }
}
