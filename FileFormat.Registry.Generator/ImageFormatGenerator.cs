using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace FileFormat.Registry.Generator;

/// <summary>
/// Incremental source generator that builds the image format registry from the formats present in the compilation.
/// </summary>
[Generator]
public sealed class ImageFormatGenerator : IIncrementalGenerator {

  private const string _IMAGE_FORMAT_READER = "FileFormat.Core.IImageFormatReader`1";
  private const string _IMAGE_FORMAT_WRITER = "FileFormat.Core.IImageFormatWriter`1";
  private const string _IMAGE_FORMAT_TO_RAW = "FileFormat.Core.IImageFormatToRaw`1";
  private const string _IMAGE_FORMAT_FROM_RAW = "FileFormat.Core.IImageFormatFromRaw`1";
  private const string _MULTI_IMAGE_FORMAT = "FileFormat.Core.IMultiImageFormat`1";
  private const string _IMAGE_INFO_READER = "FileFormat.Core.IImageInfoReader`1";
  private const string _CHUNK_LAYOUT_PROVIDER = "FileFormat.Core.IChunkLayoutProvider`1";
  private const string _CHUNK_REWRITER = "FileFormat.Core.IChunkRewriter`1";
  private const string _CHUNK_PLAN_REWRITER = "FileFormat.Core.IChunkPlanRewriter`1";
  private const string _FORMAT_MAGIC_BYTES = "FileFormat.Core.FormatMagicBytesAttribute";
  private const string _FORMAT_DETECTION_PRIORITY = "FileFormat.Core.FormatDetectionPriorityAttribute";
  private const string _FORMAT_MIME_TYPE = "FileFormat.Core.FormatMimeTypeAttribute";
  private const string _ADDITIONAL_IMAGE_FORMAT = "FileFormat.Core.AdditionalImageFormatAttribute";

  private const string _NAMESPACE_PROPERTY = "build_property.FileFormatRegistryNamespace";
  private const string _DEFAULT_NAMESPACE = "Hawkynt.FileFormats.Images";

  public void Initialize(IncrementalGeneratorInitializationContext context) {
    var nsOption = context.AnalyzerConfigOptionsProvider.Select(static (options, _) => {
      options.GlobalOptions.TryGetValue(_NAMESPACE_PROPERTY, out var ns);
      return string.IsNullOrWhiteSpace(ns) ? _DEFAULT_NAMESPACE : ns!;
    });

    var discovered = context.CompilationProvider.Select(static (compilation, ct) => _Discover(compilation, ct));
    context.RegisterSourceOutput(discovered.Combine(nsOption), static (spc, pair) => _GenerateOutput(spc, pair.Left, pair.Right));
  }

  private static ImmutableArray<FormatInfo> _Discover(Compilation compilation, System.Threading.CancellationToken ct) {
    var imageFormatReader = compilation.GetTypeByMetadataName(_IMAGE_FORMAT_READER);
    if (imageFormatReader == null)
      return ImmutableArray<FormatInfo>.Empty;

    var imageFormatWriter = compilation.GetTypeByMetadataName(_IMAGE_FORMAT_WRITER);
    var imageFormatToRaw = compilation.GetTypeByMetadataName(_IMAGE_FORMAT_TO_RAW);
    var imageFormatFromRaw = compilation.GetTypeByMetadataName(_IMAGE_FORMAT_FROM_RAW);
    var multiImageFormat = compilation.GetTypeByMetadataName(_MULTI_IMAGE_FORMAT);
    var imageInfoReader = compilation.GetTypeByMetadataName(_IMAGE_INFO_READER);
    var chunkLayoutProvider = compilation.GetTypeByMetadataName(_CHUNK_LAYOUT_PROVIDER);
    var chunkRewriter = compilation.GetTypeByMetadataName(_CHUNK_REWRITER);
    var chunkPlanRewriter = compilation.GetTypeByMetadataName(_CHUNK_PLAN_REWRITER);
    var magicBytesAttr = compilation.GetTypeByMetadataName(_FORMAT_MAGIC_BYTES);
    var detectionPriorityAttr = compilation.GetTypeByMetadataName(_FORMAT_DETECTION_PRIORITY);
    var mimeTypeAttr = compilation.GetTypeByMetadataName(_FORMAT_MIME_TYPE);

    var results = new List<FormatInfo>();
    var visited = new HashSet<string>();

    foreach (var type in _GetAllNamedTypes(compilation, ct)) {
      ct.ThrowIfCancellationRequested();

      if (type.IsAbstract || (type.TypeKind != TypeKind.Class && type.TypeKind != TypeKind.Struct))
        continue;
      if (type.DeclaredAccessibility != Accessibility.Public)
        continue;

      var hasFormatReader = false;
      var hasFormatWriter = false;
      var hasToRawImage = false;
      var hasFromRawImage = false;
      var hasMultiImage = false;
      var hasImageInfoReader = false;
      var hasChunkLayout = false;
      var hasChunkRewriter = false;
      var hasChunkPlanRewriter = false;

      foreach (var iface in type.AllInterfaces) {
        if (!iface.IsGenericType || iface.TypeArguments.Length != 1)
          continue;
        if (!SymbolEqualityComparer.Default.Equals(iface.TypeArguments[0], type))
          continue;

        var def = iface.OriginalDefinition;
        if (SymbolEqualityComparer.Default.Equals(def, imageFormatReader))
          hasFormatReader = true;
        else if (imageFormatWriter != null && SymbolEqualityComparer.Default.Equals(def, imageFormatWriter))
          hasFormatWriter = true;
        else if (imageFormatToRaw != null && SymbolEqualityComparer.Default.Equals(def, imageFormatToRaw))
          hasToRawImage = true;
        else if (imageFormatFromRaw != null && SymbolEqualityComparer.Default.Equals(def, imageFormatFromRaw))
          hasFromRawImage = true;
        else if (multiImageFormat != null && SymbolEqualityComparer.Default.Equals(def, multiImageFormat))
          hasMultiImage = true;
        else if (imageInfoReader != null && SymbolEqualityComparer.Default.Equals(def, imageInfoReader))
          hasImageInfoReader = true;
        else if (chunkLayoutProvider != null && SymbolEqualityComparer.Default.Equals(def, chunkLayoutProvider))
          hasChunkLayout = true;
        else if (chunkRewriter != null && SymbolEqualityComparer.Default.Equals(def, chunkRewriter))
          hasChunkRewriter = true;
        else if (chunkPlanRewriter != null && SymbolEqualityComparer.Default.Equals(def, chunkPlanRewriter))
          hasChunkPlanRewriter = true;
      }

      if (!hasFormatReader && !hasFormatWriter && !hasToRawImage && !hasFromRawImage && !hasMultiImage && !hasImageInfoReader)
        continue;

      var formatId = type.Name;
      if (formatId.EndsWith("File", StringComparison.Ordinal))
        formatId = formatId.Substring(0, formatId.Length - 4);
      if (!visited.Add(formatId))
        continue;

      var fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

      var magicSignatures = new List<MagicBytesInfo>();
      if (magicBytesAttr != null) {
        foreach (var attr in type.GetAttributes()) {
          if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, magicBytesAttr))
            continue;
          if (attr.ConstructorArguments.Length < 1)
            continue;

          var signature = attr.ConstructorArguments[0];
          if (signature.Kind != TypedConstantKind.Array)
            continue;

          var bytes = new List<byte>();
          foreach (var element in signature.Values)
            if (element.Value is byte b)
              bytes.Add(b);

          var offset = 0;
          if (attr.ConstructorArguments.Length >= 2 && attr.ConstructorArguments[1].Value is int positional)
            offset = positional;
          else
            foreach (var named in attr.NamedArguments)
              if (named.Key == "offset" && named.Value.Value is int namedOffset)
                offset = namedOffset;

          if (bytes.Count > 0)
            magicSignatures.Add(new MagicBytesInfo(bytes.ToArray(), offset));
        }
      }

      // Extract [FormatDetectionPriority] attribute at compile time
      var detectionPriority = 100; // default
      if (detectionPriorityAttr != null) {
        foreach (var attr in type.GetAttributes()) {
          if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, detectionPriorityAttr))
            continue;
          if (attr.ConstructorArguments.Length >= 1 && attr.ConstructorArguments[0].Value is int prio)
            detectionPriority = prio;
        }
      }

      // Extract [FormatMimeType] attribute (single, params string[]) at compile time.
      var mimeTypes = new List<string>();
      if (mimeTypeAttr != null) {
        foreach (var attr in type.GetAttributes()) {
          if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, mimeTypeAttr))
            continue;
          if (attr.ConstructorArguments.Length < 1)
            continue;
          var arg = attr.ConstructorArguments[0];
          if (arg.Kind != TypedConstantKind.Array)
            continue;
          foreach (var element in arg.Values)
            if (element.Value is string s && !string.IsNullOrEmpty(s))
              mimeTypes.Add(s);
        }
      }

      results.Add(new FormatInfo(
        formatId,
        fullName,
        hasFormatReader,
        hasToRawImage,
        hasFromRawImage,
        hasFormatWriter,
        hasMultiImage,
        hasImageInfoReader,
        magicSignatures.ToArray(),
        detectionPriority,
        mimeTypes.ToArray(),
        hasChunkLayout,
        hasChunkRewriter,
        hasChunkPlanRewriter
      ));
    }

    // Discover additional enum-only format IDs from [assembly: AdditionalImageFormat("...")] attributes
    var additionalAttr = compilation.GetTypeByMetadataName(_ADDITIONAL_IMAGE_FORMAT);
    if (additionalAttr != null) {
      foreach (var attr in compilation.Assembly.GetAttributes()) {
        if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, additionalAttr))
          continue;
        if (attr.ConstructorArguments.Length != 1 || attr.ConstructorArguments[0].Value is not string formatId)
          continue;
        if (!visited.Add(formatId))
          continue;

        results.Add(new FormatInfo(formatId, null, false, false, false, false, false, false, Array.Empty<MagicBytesInfo>(), 100, Array.Empty<string>()));
      }
    }

    results.Sort((a, b) => StringComparer.Ordinal.Compare(a.FormatId, b.FormatId));
    return results.ToImmutableArray();
  }

  private static IEnumerable<INamedTypeSymbol> _GetAllNamedTypes(Compilation compilation, System.Threading.CancellationToken ct) {
    // Current compilation types
    foreach (var type in _GetTypesFromNamespace(compilation.GlobalNamespace, ct))
      yield return type;

    // Referenced assembly types
    foreach (var reference in compilation.References) {
      ct.ThrowIfCancellationRequested();
      if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
        continue;
      foreach (var type in _GetTypesFromNamespace(assembly.GlobalNamespace, ct))
        yield return type;
    }
  }

  private static IEnumerable<INamedTypeSymbol> _GetTypesFromNamespace(INamespaceSymbol ns, System.Threading.CancellationToken ct) {
    ct.ThrowIfCancellationRequested();

    foreach (var member in ns.GetTypeMembers())
      yield return member;

    foreach (var child in ns.GetNamespaceMembers())
      foreach (var type in _GetTypesFromNamespace(child, ct))
        yield return type;
  }

  private static void _GenerateOutput(SourceProductionContext spc, ImmutableArray<FormatInfo> formats, string targetNamespace) {
    _GenerateEnum(spc, formats, targetNamespace);
    _GenerateRegistration(spc, formats, targetNamespace);
  }

  private static void _GenerateEnum(SourceProductionContext spc, ImmutableArray<FormatInfo> formats, string targetNamespace) {
    var sb = new StringBuilder();
    sb.AppendLine("// <auto-generated />");
    sb.AppendLine("#nullable enable");
    sb.AppendLine();
    sb.Append("namespace ").Append(targetNamespace).AppendLine(";");
    sb.AppendLine();
    sb.AppendLine("/// <summary>Supported image formats, auto-generated from discovered IImageFormatReader implementations.</summary>");
    sb.AppendLine("public enum ImageFormat {");
    sb.AppendLine("  /// <summary>Represents an unknown or unsupported image format.</summary>");
    sb.AppendLine("  Unknown,");

    foreach (var format in formats) {
      sb.Append("  /// <summary>Represents the ").Append(format.FormatId).AppendLine(" image format.</summary>");
      sb.Append("  ").Append(format.FormatId).AppendLine(",");
    }

    sb.AppendLine("}");

    spc.AddSource("ImageFormat.g.cs", sb.ToString());
  }

  private static string _FormatMagicArray(MagicBytesInfo magic) {
    var sb = new StringBuilder();
    sb.Append("new MagicSignature(new byte[] { ");
    for (var i = 0; i < magic.Signature.Length; ++i) {
      if (i > 0) sb.Append(", ");
      sb.Append("0x").Append(magic.Signature[i].ToString("X2"));
    }
    sb.Append(" }, ").Append(magic.Offset).Append(", ").Append(magic.Offset + magic.Signature.Length).Append(')');
    return sb.ToString();
  }

  private static void _EmitMagicArray(StringBuilder sb, FormatInfo format) {
    if (format.MagicSignatures.Length == 0) {
      sb.Append("System.Array.Empty<MagicSignature>()");
      return;
    }

    sb.Append("new MagicSignature[] { ");
    for (var i = 0; i < format.MagicSignatures.Length; ++i) {
      if (i > 0) sb.Append(", ");
      sb.Append(_FormatMagicArray(format.MagicSignatures[i]));
    }
    sb.Append(" }");
  }

  private static void _GenerateRegistration(SourceProductionContext spc, ImmutableArray<FormatInfo> formats, string targetNamespace) {
    var sb = new StringBuilder();
    sb.AppendLine("// <auto-generated />");
    sb.AppendLine("#nullable enable");
    sb.AppendLine();
    sb.Append("namespace ").Append(targetNamespace).AppendLine(";");
    sb.AppendLine();
    sb.AppendLine("internal static partial class FormatRegistration {");
    sb.AppendLine("  static partial void RegisterAll() {");

    foreach (var format in formats) {
      if (format.FullTypeName == null)
        continue; // Enum-only entry from [AdditionalImageFormat]

      var method = format.HasFormatReader && format.HasToRawImage && format.HasFromRawImage && format.HasFormatWriter
        ? "_RegisterReaderWriter"
        : "_RegisterReader";

      sb.Append("    ").Append(method).Append('<').Append(format.FullTypeName).Append(">(ImageFormat.").Append(format.FormatId).Append(", ");
      _EmitMagicArray(sb, format);
      sb.Append(", ").Append(format.DetectionPriority).Append(", ");
      _EmitMimeArray(sb, format);
      sb.AppendLine(");");
    }

    sb.AppendLine();
    sb.AppendLine("    // Multi-image registrations");
    foreach (var format in formats) {
      if (format.FullTypeName == null || !format.HasMultiImage)
        continue;
      sb.Append("    _RegisterMultiImage<").Append(format.FullTypeName).Append(">(ImageFormat.").Append(format.FormatId).AppendLine(");");
    }

    sb.AppendLine();
    sb.AppendLine("    // Image-info registrations");
    foreach (var format in formats) {
      if (format.FullTypeName == null || !format.HasImageInfoReader)
        continue;
      sb.Append("    _RegisterImageInfoReader<").Append(format.FullTypeName).Append(">(ImageFormat.").Append(format.FormatId).AppendLine(");");
    }

    sb.AppendLine();
    sb.AppendLine("    // Chunk-layout registrations");
    foreach (var format in formats) {
      if (format.FullTypeName == null || !format.HasChunkLayout)
        continue;
      sb.Append("    _RegisterChunkLayout<").Append(format.FullTypeName).Append(">(ImageFormat.").Append(format.FormatId).AppendLine(");");
    }

    sb.AppendLine();
    sb.AppendLine("    // Chunk-rewriter registrations");
    foreach (var format in formats) {
      if (format.FullTypeName == null || !format.HasChunkRewriter)
        continue;
      sb.Append("    _RegisterChunkRewriter<").Append(format.FullTypeName).Append(">(ImageFormat.").Append(format.FormatId).AppendLine(");");
    }

    sb.AppendLine();
    sb.AppendLine("    // Chunk-plan-rewriter registrations");
    foreach (var format in formats) {
      if (format.FullTypeName == null || !format.HasChunkPlanRewriter)
        continue;
      sb.Append("    _RegisterChunkPlanRewriter<").Append(format.FullTypeName).Append(">(ImageFormat.").Append(format.FormatId).AppendLine(");");
    }

    sb.AppendLine("  }");
    sb.AppendLine("}");

    spc.AddSource("FormatRegistration.g.cs", sb.ToString());
  }

  private static void _EmitMimeArray(StringBuilder sb, FormatInfo format) {
    if (format.MimeTypes.Length == 0) {
      sb.Append("System.Array.Empty<string>()");
      return;
    }

    sb.Append("new string[] { ");
    for (var i = 0; i < format.MimeTypes.Length; ++i) {
      if (i > 0) sb.Append(", ");
      sb.Append('\"').Append(format.MimeTypes[i].Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('\"');
    }
    sb.Append(" }");
  }

  private sealed class FormatInfo {
    public string FormatId { get; }
    public string? FullTypeName { get; }
    public bool HasFormatReader { get; }
    public bool HasToRawImage { get; }
    public bool HasFromRawImage { get; }
    public bool HasFormatWriter { get; }
    public bool HasMultiImage { get; }
    public bool HasImageInfoReader { get; }
    public MagicBytesInfo[] MagicSignatures { get; }
    public int DetectionPriority { get; }
    public string[] MimeTypes { get; }
    public bool HasChunkLayout { get; }
    public bool HasChunkRewriter { get; }
    public bool HasChunkPlanRewriter { get; }

    public FormatInfo(
      string formatId, string? fullTypeName,
      bool hasFormatReader, bool hasToRawImage, bool hasFromRawImage, bool hasFormatWriter,
      bool hasMultiImage, bool hasImageInfoReader,
      MagicBytesInfo[] magicSignatures, int detectionPriority, string[] mimeTypes,
      bool hasChunkLayout = false, bool hasChunkRewriter = false, bool hasChunkPlanRewriter = false) {
      this.FormatId = formatId;
      this.FullTypeName = fullTypeName;
      this.HasFormatReader = hasFormatReader;
      this.HasToRawImage = hasToRawImage;
      this.HasFromRawImage = hasFromRawImage;
      this.HasFormatWriter = hasFormatWriter;
      this.HasMultiImage = hasMultiImage;
      this.HasImageInfoReader = hasImageInfoReader;
      this.MagicSignatures = magicSignatures;
      this.DetectionPriority = detectionPriority;
      this.MimeTypes = mimeTypes;
      this.HasChunkLayout = hasChunkLayout;
      this.HasChunkRewriter = hasChunkRewriter;
      this.HasChunkPlanRewriter = hasChunkPlanRewriter;
    }
  }

  private sealed class MagicBytesInfo {
    public byte[] Signature { get; }
    public int Offset { get; }
    public MagicBytesInfo(byte[] signature, int offset) {
      this.Signature = signature;
      this.Offset = offset;
    }
  }
}
