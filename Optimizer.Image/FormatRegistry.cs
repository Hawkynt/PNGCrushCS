using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FileFormat.Core;

namespace Optimizer.Image;

/// <summary>Data-driven registry mapping <see cref="ImageFormat"/> to format-specific operations, discovered automatically from <see cref="IImageFileFormat{TSelf}"/> implementations.</summary>
internal static class FormatRegistry {

  internal sealed record FormatEntry(
    ImageFormat Format,
    string Name,
    string PrimaryExtension,
    string[] AllExtensions,
    Func<FileInfo, RawImage?> LoadRawImage,
    Func<byte[], RawImage?> LoadRawImageFromBytes,
    Func<RawImage, byte[]> ConvertFromRawImage,
    FormatCapability Capabilities,
    MagicSignature[] MagicSignatures,
    Func<byte[], bool?>? MatchesSignature,
    int DetectionPriority,
    Func<FileInfo, int>? GetImageCount = null,
    Func<FileInfo, int, RawImage?>? LoadRawImageAtIndex = null,
    Func<FileInfo, IReadOnlyList<RawImage>?>? LoadAllRawImages = null
  );

  internal readonly record struct MagicSignature(byte[] Signature, int Offset, int MinHeaderLength);

  /// <summary>Lightweight entry for formats without <see cref="IImageFileFormat{TSelf}"/> (e.g. GIF).</summary>
  private sealed record DetectionOnlyEntry(
    ImageFormat Format,
    string Name,
    MagicSignature[] MagicSignatures,
    Func<byte[], bool?>? MatchesSignature,
    int DetectionPriority
  );

  private static readonly Dictionary<ImageFormat, FormatEntry> _byFormat = new();
  private static readonly Dictionary<string, ImageFormat> _byExtension = new(StringComparer.OrdinalIgnoreCase);

  /// <summary>Pre-sorted list of all entries (full + detection-only) that can participate in signature detection.</summary>
  private static readonly SignatureEntry[] _signatureEntries;

  /// <summary>Unified type for signature detection iteration.</summary>
  private readonly record struct SignatureEntry(
    ImageFormat Format,
    MagicSignature[] MagicSignatures,
    Func<byte[], bool?>? MatchesSignature,
    int DetectionPriority
  );

  private static readonly MethodInfo _registerGenericMethod =
    typeof(FormatRegistry).GetMethod(nameof(_RegisterGeneric), BindingFlags.NonPublic | BindingFlags.Static)!;

  private static readonly MethodInfo _registerMultiImageMethod =
    typeof(FormatRegistry).GetMethod(nameof(_RegisterMultiImage), BindingFlags.NonPublic | BindingFlags.Static)!;

  static FormatRegistry() {
    // Force-load all FileFormat assemblies from the output directory.
    // GetReferencedAssemblies() only returns assemblies the compiler detected as actually used in code;
    // since format types are discovered via reflection (not direct usage), we must load them from disk.
    var baseDir = AppContext.BaseDirectory;
    foreach (var dll in Directory.GetFiles(baseDir, "FileFormat.*.dll")) {
      try {
        var asmName = AssemblyName.GetAssemblyName(dll);
        Assembly.Load(asmName);
      } catch {
        // ignore load failures
      }
    }

    // Scan all loaded assemblies for IImageFileFormat<T> implementations
    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
      _ScanAssembly(assembly);

    // Manual detection entries for formats without IImageFileFormat<T> (GIF uses external GifFileFormat repo)
    var detectionOnly = new List<DetectionOnlyEntry>();

    _RegisterDetectionOnly(
      detectionOnly, ImageFormat.Gif, "Gif",
      [".gif", ".giff"],
      [new([0x47, 0x49, 0x46, 0x38], 0, 4)],
      null, 100
    );

    // Build sorted signature detection index from both full entries and detection-only entries
    var sigFromFull = _byFormat.Values
      .Where(e => e.MagicSignatures.Length > 0 || e.MatchesSignature != null)
      .Select(e => new SignatureEntry(e.Format, e.MagicSignatures, e.MatchesSignature, e.DetectionPriority));

    var sigFromDetection = detectionOnly
      .Where(e => e.MagicSignatures.Length > 0 || e.MatchesSignature != null)
      .Select(e => new SignatureEntry(e.Format, e.MagicSignatures, e.MatchesSignature, e.DetectionPriority));

    _signatureEntries = sigFromFull
      .Concat(sigFromDetection)
      .OrderBy(e => e.DetectionPriority)
      .ThenBy(e => e.Format.ToString())
      .ToArray();
  }

  private static void _ScanAssembly(Assembly assembly) {
    Type[] types;
    try {
      types = assembly.GetTypes();
    } catch {
      return;
    }

    var ifaceType = typeof(IImageFileFormat<>);
    var multiIfaceType = typeof(IMultiImageFileFormat<>);
    foreach (var type in types) {
      if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
        continue;

      var hasImageFormat = false;
      var hasMultiImage = false;
      foreach (var iface in type.GetInterfaces()) {
        if (!iface.IsGenericType)
          continue;
        if (iface.GetGenericArguments()[0] != type)
          continue;

        var def = iface.GetGenericTypeDefinition();
        if (def == ifaceType)
          hasImageFormat = true;
        else if (def == multiIfaceType)
          hasMultiImage = true;
      }

      if (!hasImageFormat)
        continue;

      try {
        _registerGenericMethod.MakeGenericMethod(type).Invoke(null, null);
      } catch {
        // Skip types that fail to register (e.g. missing dependencies)
      }

      if (hasMultiImage)
        try {
          _registerMultiImageMethod.MakeGenericMethod(type).Invoke(null, null);
        } catch {
          // Skip types that fail to register
        }
    }
  }

  private static void _RegisterGeneric<T>() where T : IImageFileFormat<T> {
    var type = typeof(T);
    var name = type.Name.EndsWith("File") ? type.Name[..^4] : type.Name;

    // Bridge to ImageFormat enum via naming convention
    var format = Enum.TryParse<ImageFormat>(name, out var f) ? f : ImageFormat.Unknown;

    // Read attribute-based metadata
    var magicSignatures = type.GetCustomAttributes<FormatMagicBytesAttribute>()
      .Select(a => new MagicSignature(a.Signature, a.Offset, a.MinHeaderLength))
      .ToArray();

    var priority = type.GetCustomAttribute<FormatDetectionPriorityAttribute>()?.Priority ?? 100;

    // Detect if MatchesSignature is explicitly overridden (not the default null-returning implementation)
    var hasMatchOverride = type
      .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
      .Any(m => m.Name.Contains("MatchesSignature"));

    Func<byte[], bool?>? matchesSignature = hasMatchOverride
      ? header => T.MatchesSignature(header)
      : null;

    var entry = new FormatEntry(
      Format: format,
      Name: name,
      PrimaryExtension: T.PrimaryExtension,
      AllExtensions: T.FileExtensions,
      LoadRawImage: file => {
        try {
          return T.ToRawImage(T.FromFile(file));
        } catch {
          return null;
        }
      },
      LoadRawImageFromBytes: bytes => {
        try {
          return T.ToRawImage(T.FromBytes(bytes));
        } catch {
          return null;
        }
      },
      ConvertFromRawImage: raw => T.ToBytes(T.FromRawImage(raw)),
      Capabilities: T.Capabilities,
      MagicSignatures: magicSignatures,
      MatchesSignature: matchesSignature,
      DetectionPriority: priority
    );

    if (format != ImageFormat.Unknown)
      _byFormat.TryAdd(format, entry);

    foreach (var ext in T.FileExtensions)
      _byExtension.TryAdd(ext, format);
  }

  private static void _RegisterMultiImage<T>() where T : IImageFileFormat<T>, IMultiImageFileFormat<T> {
    var type = typeof(T);
    var name = type.Name.EndsWith("File") ? type.Name[..^4] : type.Name;
    var format = Enum.TryParse<ImageFormat>(name, out var f) ? f : ImageFormat.Unknown;
    if (format == ImageFormat.Unknown || !_byFormat.TryGetValue(format, out var existing))
      return;

    _byFormat[format] = existing with {
      GetImageCount = file => {
        try {
          return T.ImageCount(T.FromFile(file));
        } catch {
          return 0;
        }
      },
      LoadRawImageAtIndex = (file, index) => {
        try {
          return T.ToRawImage(T.FromFile(file), index);
        } catch {
          return null;
        }
      },
      LoadAllRawImages = file => {
        try {
          return T.ToRawImages(T.FromFile(file));
        } catch {
          return null;
        }
      }
    };
  }

  private static void _RegisterDetectionOnly(
    List<DetectionOnlyEntry> list,
    ImageFormat format, string name,
    string[] extensions,
    MagicSignature[] magicSignatures,
    Func<byte[], bool?>? matchesSignature,
    int detectionPriority
  ) {
    list.Add(new(format, name, magicSignatures, matchesSignature, detectionPriority));
    foreach (var ext in extensions)
      _byExtension.TryAdd(ext, format);
  }

  internal static FormatEntry? GetEntry(ImageFormat format)
    => _byFormat.GetValueOrDefault(format);

  internal static string GetExtension(ImageFormat format)
    => GetEntry(format)?.PrimaryExtension ?? "";

  internal static ImageFormat DetectFromExtension(string extension)
    => _byExtension.GetValueOrDefault(extension);

  /// <summary>Detects image format from magic bytes using pre-sorted signature entries.</summary>
  internal static FormatEntry? DetectFromSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 2)
      return null;

    byte[]? headerArray = null;

    foreach (var entry in _signatureEntries) {
      // Check MatchesSignature first — complex logic can match or explicitly reject
      if (entry.MatchesSignature != null) {
        headerArray ??= header.ToArray();
        var result = entry.MatchesSignature(headerArray);
        if (result == true)
          return _byFormat.GetValueOrDefault(entry.Format);
        if (result == false)
          continue;
      }

      // Check magic byte signatures
      foreach (var sig in entry.MagicSignatures) {
        if (header.Length >= sig.MinHeaderLength && header.Slice(sig.Offset, sig.Signature.Length).SequenceEqual(sig.Signature))
          return _byFormat.GetValueOrDefault(entry.Format);
      }
    }

    return null;
  }

  /// <summary>Detects image format enum from magic bytes. Returns <see cref="ImageFormat.Unknown"/> if unrecognized.</summary>
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
    => _byFormat.Values.Where(e => (e.Capabilities & FormatCapability.HasDedicatedOptimizer) == 0);
}
