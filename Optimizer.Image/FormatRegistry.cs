using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;

namespace Optimizer.Image;

/// <summary>Data-driven registry mapping <see cref="ImageFormat"/> to format-specific operations. Zero runtime reflection — all registrations are source-generated.</summary>
internal static class FormatRegistry {

  internal sealed record FormatEntry(
    ImageFormat Format,
    string Name,
    string PrimaryExtension,
    string[] AllExtensions,
    Func<FileInfo, RawImage?> LoadRawImage,
    Func<byte[], RawImage?> LoadRawImageFromBytes,
    Func<RawImage, byte[]>? ConvertFromRawImage,
    FormatCapability Capabilities,
    MagicSignature[] MagicSignatures,
    Func<byte[], bool?>? MatchesSignature,
    int DetectionPriority,
    Func<byte[], ImageInfo?>? ReadImageInfo = null,
    Func<FileInfo, int>? GetImageCount = null,
    Func<FileInfo, int, RawImage?>? LoadRawImageAtIndex = null,
    Func<FileInfo, IReadOnlyList<RawImage>?>? LoadAllRawImages = null
  );

  internal readonly record struct MagicSignature(byte[] Signature, int Offset, int MinHeaderLength);

  private static readonly Dictionary<ImageFormat, FormatEntry> _byFormat = new();
  private static readonly Dictionary<string, ImageFormat> _byExtension = new(StringComparer.OrdinalIgnoreCase);

  private static SignatureEntry[] _signatureEntries = [];

  private readonly record struct SignatureEntry(
    ImageFormat Format,
    MagicSignature[] MagicSignatures,
    Func<byte[], bool?>? MatchesSignature,
    int DetectionPriority
  );

  static FormatRegistry() => FormatRegistration.Initialize();

  // --- Registration API (called by FormatRegistration, not by reflection) ---

  internal static void Register(FormatEntry entry) {
    if (entry.Format != ImageFormat.Unknown)
      _byFormat.TryAdd(entry.Format, entry);
    foreach (var ext in entry.AllExtensions)
      _byExtension.TryAdd(ext, entry.Format);
  }

  internal static void RegisterDetectionOnly(
    ImageFormat format, string name,
    string[] extensions,
    MagicSignature[] magicSignatures,
    Func<byte[], bool?>? matchesSignature,
    int detectionPriority
  ) {
    foreach (var ext in extensions)
      _byExtension.TryAdd(ext, format);
    // Detection-only entries are tracked via _detectionOnly for signature table building
    _detectionOnlyEntries.Add(new(format, magicSignatures, matchesSignature, detectionPriority));
  }

  private static readonly List<SignatureEntry> _detectionOnlyEntries = [];

  internal static void AugmentMultiImage(
    ImageFormat format,
    Func<FileInfo, int> getImageCount,
    Func<FileInfo, int, RawImage?> loadRawImageAtIndex,
    Func<FileInfo, IReadOnlyList<RawImage>?> loadAllRawImages
  ) {
    if (!_byFormat.TryGetValue(format, out var existing))
      return;

    _byFormat[format] = existing with {
      GetImageCount = getImageCount,
      LoadRawImageAtIndex = loadRawImageAtIndex,
      LoadAllRawImages = loadAllRawImages
    };
  }

  internal static void AugmentInfoReader(
    ImageFormat format,
    Func<byte[], ImageInfo?> readImageInfo
  ) {
    if (!_byFormat.TryGetValue(format, out var existing))
      return;

    _byFormat[format] = existing with { ReadImageInfo = readImageInfo };
  }

  /// <summary>Builds the sorted signature table after all registrations are complete.</summary>
  internal static void BuildSignatureTable() {
    var sigFromFull = _byFormat.Values
      .Where(e => e.MagicSignatures.Length > 0 || e.MatchesSignature != null)
      .Select(e => new SignatureEntry(e.Format, e.MagicSignatures, e.MatchesSignature, e.DetectionPriority));

    _signatureEntries = sigFromFull
      .Concat(_detectionOnlyEntries)
      .OrderBy(e => e.DetectionPriority)
      .ThenBy(e => e.Format.ToString())
      .ToArray();
  }

  // --- Lookup API (unchanged) ---

  internal static FormatEntry? GetEntry(ImageFormat format)
    => _byFormat.GetValueOrDefault(format);

  internal static string GetExtension(ImageFormat format)
    => GetEntry(format)?.PrimaryExtension ?? "";

  internal static ImageFormat DetectFromExtension(string extension)
    => _byExtension.GetValueOrDefault(extension);

  internal static FormatEntry? DetectFromSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 2)
      return null;

    byte[]? headerArray = null;
    foreach (var entry in _signatureEntries) {
      if (entry.MatchesSignature != null) {
        headerArray ??= header.ToArray();
        var result = entry.MatchesSignature(headerArray);
        if (result == true)
          return _byFormat.GetValueOrDefault(entry.Format);
        if (result == false)
          continue;
      }

      foreach (var sig in entry.MagicSignatures) {
        if (header.Length >= sig.MinHeaderLength && header.Slice(sig.Offset, sig.Signature.Length).SequenceEqual(sig.Signature))
          return _byFormat.GetValueOrDefault(entry.Format);
      }
    }
    return null;
  }

  internal static ImageFormat DetectFormatFromSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 2)
      return ImageFormat.Unknown;

    byte[]? headerArray = null;
    foreach (var entry in _signatureEntries) {
      if (entry.MatchesSignature != null) {
        headerArray ??= header.ToArray();
        var result = entry.MatchesSignature(headerArray);
        if (result == true)
          return entry.Format;
        if (result == false)
          continue;
      }

      foreach (var sig in entry.MagicSignatures) {
        if (header.Length >= sig.MinHeaderLength && header.Slice(sig.Offset, sig.Signature.Length).SequenceEqual(sig.Signature))
          return entry.Format;
      }
    }
    return ImageFormat.Unknown;
  }

  internal static IEnumerable<FormatEntry> ConversionTargets
    => _byFormat.Values.Where(e =>
      (e.Capabilities & FormatCapability.HasDedicatedOptimizer) == 0
      && e.ConvertFromRawImage != null);
}
