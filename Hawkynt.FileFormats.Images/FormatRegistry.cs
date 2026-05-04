using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Images;

/// <summary>
/// Public, zero-runtime-reflection registry of all image formats discovered at compile time
/// by <c>FileFormat.Registry.Generator</c>. Provides:
/// <list type="bullet">
///   <item>Format detection from <see cref="ReadOnlySpan{T}">bytes</see>, <see cref="Stream"/>s, file paths, extensions, or MIME types</item>
///   <item>Format → file-extension and format → MIME-type lookups (and reverse)</item>
///   <item>High-level <see cref="Read(FileInfo)"/> / <see cref="Write(RawImage, ImageFormat)"/> convenience helpers</item>
///   <item>Programmatic enumeration of every supported format</item>
/// </list>
/// </summary>
public static class FormatRegistry {

  private static readonly Dictionary<ImageFormat, FormatEntry> _byFormat = new();
  private static readonly Dictionary<string, ImageFormat> _byExtension = new(StringComparer.OrdinalIgnoreCase);
  private static readonly Dictionary<string, ImageFormat> _byMimeType = new(StringComparer.OrdinalIgnoreCase);
  private static SignatureEntry[] _signatureEntries = Array.Empty<SignatureEntry>();
  private static readonly List<SignatureEntry> _detectionOnlyEntries = new();

  private readonly record struct SignatureEntry(
    ImageFormat Format,
    MagicSignature[] MagicSignatures,
    Func<byte[], bool?>? MatchesSignature,
    int DetectionPriority);

  static FormatRegistry() => FormatRegistration.Initialize();

  // ============================================================================================
  // Internal registration API — called only by source-generated code in FormatRegistration.g.cs
  // ============================================================================================

  internal static void Register(FormatEntry entry) {
    if (entry.Format != ImageFormat.Unknown) _byFormat.TryAdd(entry.Format, entry);
    foreach (var ext in entry.AllExtensions) _byExtension.TryAdd(ext, entry.Format);
    foreach (var mime in entry.MimeTypes) _byMimeType.TryAdd(mime, entry.Format);
  }

  internal static void RegisterDetectionOnly(
    ImageFormat format, string name, string[] extensions, string[] mimeTypes,
    MagicSignature[] magicSignatures, Func<byte[], bool?>? matchesSignature, int detectionPriority) {
    foreach (var ext in extensions) _byExtension.TryAdd(ext, format);
    foreach (var mime in mimeTypes) _byMimeType.TryAdd(mime, format);
    _detectionOnlyEntries.Add(new(format, magicSignatures, matchesSignature, detectionPriority));
  }

  internal static void AugmentMultiImage(
    ImageFormat format,
    Func<FileInfo, int> getImageCount,
    Func<FileInfo, int, RawImage?> loadRawImageAtIndex,
    Func<FileInfo, IReadOnlyList<RawImage>?> loadAllRawImages) {
    if (!_byFormat.TryGetValue(format, out var existing)) return;
    _byFormat[format] = existing with {
      GetImageCount = getImageCount,
      LoadRawImageAtIndex = loadRawImageAtIndex,
      LoadAllRawImages = loadAllRawImages,
    };
  }

  internal static void AugmentInfoReader(ImageFormat format, Func<byte[], ImageInfo?> readImageInfo) {
    if (!_byFormat.TryGetValue(format, out var existing)) return;
    _byFormat[format] = existing with { ReadImageInfo = readImageInfo };
  }

  /// <summary>Builds the priority-sorted signature table after all registrations are complete.
  /// Called by <see cref="FormatRegistration.Initialize"/>.</summary>
  internal static void BuildSignatureTable() {
    var sigFromFull = _byFormat.Values
      .Where(e => e.MagicSignatures.Length > 0 || e.MatchesSignature != null)
      .Select(e => new SignatureEntry(e.Format, e.MagicSignatures, e.MatchesSignature, e.DetectionPriority));
    _signatureEntries = sigFromFull
      .Concat(_detectionOnlyEntries)
      .OrderBy(e => e.DetectionPriority)
      .ThenBy(e => e.Format.ToString(), StringComparer.Ordinal)
      .ToArray();
  }

  // ============================================================================================
  // Public lookup API
  // ============================================================================================

  /// <summary>Returns the registered <see cref="FormatEntry"/> for a given <see cref="ImageFormat"/>,
  /// or <c>null</c> if the format is unknown or has no registered handler.</summary>
  public static FormatEntry? GetEntry(ImageFormat format) => _byFormat.GetValueOrDefault(format);

  /// <summary>Returns the canonical file extension (e.g. <c>".png"</c>), or empty string if unknown.</summary>
  public static string PrimaryExtension(ImageFormat format) => GetEntry(format)?.PrimaryExtension ?? "";

  /// <summary>All recognized file extensions for a format (including aliases like <c>.tif</c>/<c>.tiff</c>).</summary>
  public static IReadOnlyList<string> AllExtensions(ImageFormat format) =>
    GetEntry(format)?.AllExtensions ?? Array.Empty<string>();

  /// <summary>The primary/preferred MIME type for a format. Returns <c>"application/octet-stream"</c> if unknown.</summary>
  public static string PrimaryMimeType(ImageFormat format) =>
    GetEntry(format)?.PrimaryMimeType ?? "application/octet-stream";

  /// <summary>All MIME types associated with a format, in declaration order.</summary>
  public static IReadOnlyList<string> AllMimeTypes(ImageFormat format) =>
    GetEntry(format)?.MimeTypes ?? Array.Empty<string>();

  /// <summary>Every registered format entry. Useful for building UI pickers or capability tables.</summary>
  public static IEnumerable<FormatEntry> AllFormats => _byFormat.Values;

  /// <summary>Formats that support reading (decoding to <see cref="RawImage"/>).</summary>
  public static IEnumerable<FormatEntry> SupportedReadFormats => _byFormat.Values.Where(e => e.SupportsRead);

  /// <summary>Formats that support writing (encoding from <see cref="RawImage"/>).</summary>
  public static IEnumerable<FormatEntry> SupportedWriteFormats => _byFormat.Values.Where(e => e.SupportsWrite);

  // ============================================================================================
  // Public detection API
  // ============================================================================================

  /// <summary>Identify a format from a file extension (with or without leading dot).</summary>
  public static ImageFormat DetectFromExtension(string extension) {
    if (string.IsNullOrEmpty(extension)) return ImageFormat.Unknown;
    var key = extension[0] == '.' ? extension : "." + extension;
    return _byExtension.GetValueOrDefault(key);
  }

  /// <summary>Identify a format from a MIME type string (e.g. <c>"image/png"</c>). Case-insensitive.</summary>
  public static ImageFormat DetectFromMimeType(string mimeType) {
    if (string.IsNullOrEmpty(mimeType)) return ImageFormat.Unknown;
    return _byMimeType.GetValueOrDefault(mimeType);
  }

  /// <summary>Identify a format from raw header bytes by walking the priority-sorted magic-byte table.
  /// Returns <see cref="ImageFormat.Unknown"/> if no match.</summary>
  public static ImageFormat DetectFromBytes(ReadOnlySpan<byte> header) {
    if (header.Length < 2) return ImageFormat.Unknown;

    byte[]? headerArray = null;
    foreach (var entry in _signatureEntries) {
      if (entry.MatchesSignature != null) {
        headerArray ??= header.ToArray();
        var result = entry.MatchesSignature(headerArray);
        if (result == true) return entry.Format;
        if (result == false) continue;
      }
      foreach (var sig in entry.MagicSignatures)
        if (header.Length >= sig.MinHeaderLength
            && header.Slice(sig.Offset, sig.Signature.Length).SequenceEqual(sig.Signature))
          return entry.Format;
    }
    return ImageFormat.Unknown;
  }

  /// <summary>Identify a format from an arbitrary stream by peeking up to <paramref name="peekBytes"/>.
  /// If the stream is seekable the position is restored on return; otherwise the consumed bytes are
  /// buffered into a returned <see cref="Stream"/> via <paramref name="bufferedStream"/> so callers
  /// can still read the file.</summary>
  /// <param name="stream">Input stream. Must support reading.</param>
  /// <param name="peekBytes">How many bytes to read for header inspection. 64 is enough for every
  /// known image format's signature.</param>
  public static ImageFormat DetectFromStream(Stream stream, int peekBytes = 64)
    => StreamDetector.Detect(stream, peekBytes);

  /// <summary>Identify a format from a stream and return a stream positioned at the start of the data.
  /// For seekable streams the original is returned (re-positioned); for non-seekable streams a buffered
  /// wrapper is returned that re-emits the consumed prefix.</summary>
  public static (ImageFormat Format, Stream RewoundStream) DetectFromStreamRewound(Stream stream, int peekBytes = 64)
    => StreamDetector.DetectAndRewind(stream, peekBytes);

  /// <summary>Identify a format from a file: tries magic-byte detection first, falls back to extension.</summary>
  public static ImageFormat DetectFromFile(FileInfo file) {
    if (file == null || !file.Exists) return ImageFormat.Unknown;
    using var fs = file.OpenRead();
    var byMagic = DetectFromStream(fs);
    return byMagic != ImageFormat.Unknown ? byMagic : DetectFromExtension(file.Extension);
  }

  // ============================================================================================
  // Public high-level read/write API
  // ============================================================================================

  /// <summary>Read a file of any supported format, returning a platform-independent <see cref="RawImage"/>.
  /// Returns <c>null</c> if the format isn't recognized or decoding fails.</summary>
  public static RawImage? Read(FileInfo file) {
    var fmt = DetectFromFile(file);
    var entry = GetEntry(fmt);
    return entry?.LoadRawImage(file);
  }

  /// <summary>Read raw bytes of any supported format, returning a <see cref="RawImage"/>.
  /// Returns <c>null</c> on detection or decode failure.</summary>
  public static RawImage? Read(byte[] data) {
    var fmt = DetectFromBytes(data);
    var entry = GetEntry(fmt);
    return entry?.LoadRawImageFromBytes(data);
  }

  /// <summary>Read a stream of any supported format. Reads the stream's full contents into memory
  /// for buffer-based decoders.</summary>
  public static RawImage? Read(Stream stream) {
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return Read(ms.ToArray());
  }

  /// <summary>Encode a <see cref="RawImage"/> as the given format. Returns the encoded bytes,
  /// or <c>null</c> if the target format does not support writing.</summary>
  public static byte[]? Write(RawImage image, ImageFormat format) {
    var entry = GetEntry(format);
    return entry?.ConvertFromRawImage?.Invoke(image);
  }

  /// <summary>Encode a <see cref="RawImage"/> directly into a stream. Returns <c>true</c> on success.</summary>
  public static bool Write(RawImage image, ImageFormat format, Stream output) {
    var bytes = Write(image, format);
    if (bytes == null) return false;
    output.Write(bytes, 0, bytes.Length);
    return true;
  }
}
