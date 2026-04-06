using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FileFormat.Core;

namespace Optimizer.Image;

/// <summary>Data-driven registry mapping <see cref="ImageFormat"/> to format-specific operations, discovered automatically from split interface implementations.</summary>
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

  /// <summary>Lightweight entry for formats without full implementation (e.g. GIF).</summary>
  private sealed record DetectionOnlyEntry(
    ImageFormat Format,
    string Name,
    MagicSignature[] MagicSignatures,
    Func<byte[], bool?>? MatchesSignature,
    int DetectionPriority
  );

  private static readonly Dictionary<ImageFormat, FormatEntry> _byFormat = new();
  private static readonly Dictionary<string, ImageFormat> _byExtension = new(StringComparer.OrdinalIgnoreCase);

  private static readonly SignatureEntry[] _signatureEntries;

  private readonly record struct SignatureEntry(
    ImageFormat Format,
    MagicSignature[] MagicSignatures,
    Func<byte[], bool?>? MatchesSignature,
    int DetectionPriority
  );

  private static readonly MethodInfo _registerReaderMethod =
    typeof(FormatRegistry).GetMethod(nameof(_RegisterReader), BindingFlags.NonPublic | BindingFlags.Static)!;

  private static readonly MethodInfo _registerFullMethod =
    typeof(FormatRegistry).GetMethod(nameof(_RegisterFull), BindingFlags.NonPublic | BindingFlags.Static)!;

  private static readonly MethodInfo _augmentInfoReaderMethod =
    typeof(FormatRegistry).GetMethod(nameof(_AugmentInfoReader), BindingFlags.NonPublic | BindingFlags.Static)!;

  private static readonly MethodInfo _registerMultiImageReaderMethod =
    typeof(FormatRegistry).GetMethod(nameof(_RegisterMultiImageReader), BindingFlags.NonPublic | BindingFlags.Static)!;

  private static readonly MethodInfo _registerMultiImageFullMethod =
    typeof(FormatRegistry).GetMethod(nameof(_RegisterMultiImageFull), BindingFlags.NonPublic | BindingFlags.Static)!;

  static FormatRegistry() {
    var baseDir = AppContext.BaseDirectory;
    foreach (var dll in Directory.GetFiles(baseDir, "FileFormat.*.dll")) {
      try {
        var asmName = AssemblyName.GetAssemblyName(dll);
        Assembly.Load(asmName);
      } catch { }
    }

    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
      _ScanAssembly(assembly);

    var detectionOnly = new List<DetectionOnlyEntry>();
    _RegisterDetectionOnly(
      detectionOnly, ImageFormat.Gif, "Gif",
      [".gif", ".giff"],
      [new([0x47, 0x49, 0x46, 0x38], 0, 4)],
      null, 100
    );

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

    var readerType = typeof(IImageFormatReader<>);
    var toRawType = typeof(IImageToRawImage<>);
    var fromRawType = typeof(IImageFromRawImage<>);
    var writerType = typeof(IImageFormatWriter<>);
    var multiType = typeof(IMultiImageFileFormat<>);
    var infoReaderType = typeof(IImageInfoReader<>);

    foreach (var type in types) {
      if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
        continue;

      var hasReader = false;
      var hasToRaw = false;
      var hasFromRaw = false;
      var hasWriter = false;
      var hasMulti = false;
      var hasInfoReader = false;

      foreach (var iface in type.GetInterfaces()) {
        if (!iface.IsGenericType || iface.GetGenericArguments()[0] != type)
          continue;

        var def = iface.GetGenericTypeDefinition();
        if (def == readerType) hasReader = true;
        else if (def == toRawType) hasToRaw = true;
        else if (def == fromRawType) hasFromRaw = true;
        else if (def == writerType) hasWriter = true;
        else if (def == multiType) hasMulti = true;
        else if (def == infoReaderType) hasInfoReader = true;
      }

      if (!hasReader || !hasToRaw)
        continue;

      try {
        if (hasFromRaw && hasWriter)
          _registerFullMethod.MakeGenericMethod(type).Invoke(null, null);
        else
          _registerReaderMethod.MakeGenericMethod(type).Invoke(null, null);
      } catch { }

      if (hasInfoReader)
        try {
          _augmentInfoReaderMethod.MakeGenericMethod(type).Invoke(null, null);
        } catch { }

      if (hasMulti)
        try {
          if (hasFromRaw && hasWriter)
            _registerMultiImageFullMethod.MakeGenericMethod(type).Invoke(null, null);
          else
            _registerMultiImageReaderMethod.MakeGenericMethod(type).Invoke(null, null);
        } catch { }
    }
  }

  private static (string name, ImageFormat format, MagicSignature[] magic, int priority, Func<byte[], bool?>? matchSig) _ExtractMetadata<T>() where T : IImageFormatMetadata<T> {
    var type = typeof(T);
    var name = type.Name.EndsWith("File") ? type.Name[..^4] : type.Name;
    var format = Enum.TryParse<ImageFormat>(name, out var f) ? f : ImageFormat.Unknown;

    var magic = type.GetCustomAttributes<FormatMagicBytesAttribute>()
      .Select(a => new MagicSignature(a.Signature, a.Offset, a.MinHeaderLength))
      .ToArray();

    var priority = type.GetCustomAttribute<FormatDetectionPriorityAttribute>()?.Priority ?? 100;

    var hasMatch = type
      .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
      .Any(m => m.Name.Contains("MatchesSignature"));

    Func<byte[], bool?>? matchSig = hasMatch ? header => T.MatchesSignature(header) : null;

    return (name, format, magic, priority, matchSig);
  }

  private static void _RegisterReader<T>() where T : IImageFormatReader<T>, IImageToRawImage<T> {
    var (name, format, magic, priority, matchSig) = _ExtractMetadata<T>();

    var entry = new FormatEntry(
      Format: format,
      Name: name,
      PrimaryExtension: T.PrimaryExtension,
      AllExtensions: T.FileExtensions,
      LoadRawImage: file => {
        try { return FormatIO.Decode<T>(file); } catch { return null; }
      },
      LoadRawImageFromBytes: bytes => {
        try { return FormatIO.Decode<T>(bytes); } catch { return null; }
      },
      ConvertFromRawImage: null,
      Capabilities: T.Capabilities,
      MagicSignatures: magic,
      MatchesSignature: matchSig,
      DetectionPriority: priority
    );

    if (format != ImageFormat.Unknown)
      _byFormat.TryAdd(format, entry);
    foreach (var ext in T.FileExtensions)
      _byExtension.TryAdd(ext, format);
  }

  private static void _RegisterFull<T>() where T : IImageFormatReader<T>, IImageToRawImage<T>, IImageFromRawImage<T>, IImageFormatWriter<T> {
    var (name, format, magic, priority, matchSig) = _ExtractMetadata<T>();

    var entry = new FormatEntry(
      Format: format,
      Name: name,
      PrimaryExtension: T.PrimaryExtension,
      AllExtensions: T.FileExtensions,
      LoadRawImage: file => {
        try { return FormatIO.Decode<T>(file); } catch { return null; }
      },
      LoadRawImageFromBytes: bytes => {
        try { return FormatIO.Decode<T>(bytes); } catch { return null; }
      },
      ConvertFromRawImage: raw => FormatIO.Encode<T>(raw),
      Capabilities: T.Capabilities,
      MagicSignatures: magic,
      MatchesSignature: matchSig,
      DetectionPriority: priority
    );

    if (format != ImageFormat.Unknown)
      _byFormat.TryAdd(format, entry);
    foreach (var ext in T.FileExtensions)
      _byExtension.TryAdd(ext, format);
  }

  private static void _AugmentInfoReader<T>() where T : IImageInfoReader<T> {
    var name = typeof(T).Name.EndsWith("File") ? typeof(T).Name[..^4] : typeof(T).Name;
    var format = Enum.TryParse<ImageFormat>(name, out var f) ? f : ImageFormat.Unknown;
    if (format == ImageFormat.Unknown || !_byFormat.TryGetValue(format, out var existing))
      return;

    _byFormat[format] = existing with {
      ReadImageInfo = data => { try { return T.ReadImageInfo(data); } catch { return null; } }
    };
  }

  private static void _RegisterMultiImageReader<T>() where T : IImageFormatReader<T>, IImageToRawImage<T>, IMultiImageFileFormat<T> {
    var name = typeof(T).Name.EndsWith("File") ? typeof(T).Name[..^4] : typeof(T).Name;
    var format = Enum.TryParse<ImageFormat>(name, out var f) ? f : ImageFormat.Unknown;
    if (format == ImageFormat.Unknown || !_byFormat.TryGetValue(format, out var existing))
      return;

    _byFormat[format] = existing with {
      GetImageCount = file => { try { return T.ImageCount(FormatIO.Read<T>(file)); } catch { return 0; } },
      LoadRawImageAtIndex = (file, index) => { try { return T.ToRawImage(FormatIO.Read<T>(file), index); } catch { return null; } },
      LoadAllRawImages = file => { try { return T.ToRawImages(FormatIO.Read<T>(file)); } catch { return null; } }
    };
  }

  private static void _RegisterMultiImageFull<T>() where T : IImageFormatReader<T>, IImageToRawImage<T>, IImageFromRawImage<T>, IImageFormatWriter<T>, IMultiImageFileFormat<T> {
    _RegisterMultiImageReader<T>();
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
