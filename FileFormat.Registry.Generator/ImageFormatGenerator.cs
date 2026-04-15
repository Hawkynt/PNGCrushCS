using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace FileFormat.Registry.Generator;

[Generator]
public sealed class ImageFormatGenerator : IIncrementalGenerator {

  private const string _IMAGE_FORMAT_READER = "FileFormat.Core.IImageFormatReader`1";
  private const string _IMAGE_TO_RAW_IMAGE = "FileFormat.Core.IImageToRawImage`1";
  private const string _IMAGE_FROM_RAW_IMAGE = "FileFormat.Core.IImageFromRawImage`1";
  private const string _IMAGE_FORMAT_WRITER = "FileFormat.Core.IImageFormatWriter`1";
  private const string _MULTI_IMAGE_FILE_FORMAT = "FileFormat.Core.IMultiImageFileFormat`1";
  private const string _IMAGE_INFO_READER = "FileFormat.Core.IImageInfoReader`1";
  private const string _ADDITIONAL_IMAGE_FORMAT = "FileFormat.Core.AdditionalImageFormatAttribute";
  private const string _FORMAT_MAGIC_BYTES = "FileFormat.Core.FormatMagicBytesAttribute";
  private const string _FORMAT_DETECTION_PRIORITY = "FileFormat.Core.FormatDetectionPriorityAttribute";

  public void Initialize(IncrementalGeneratorInitializationContext context) {
    var formatTypes = context.CompilationProvider.Select(static (compilation, ct) => _DiscoverFormats(compilation, ct));
    context.RegisterSourceOutput(formatTypes, static (spc, formats) => _GenerateOutput(spc, formats));
  }

  private static ImmutableArray<FormatInfo> _DiscoverFormats(Compilation compilation, System.Threading.CancellationToken ct) {
    var results = new List<FormatInfo>();
    var visited = new HashSet<string>();

    // Resolve interface symbols by metadata name for reliable comparison
    var imageFormatReader = compilation.GetTypeByMetadataName(_IMAGE_FORMAT_READER);
    var imageToRawImage = compilation.GetTypeByMetadataName(_IMAGE_TO_RAW_IMAGE);
    var imageFromRawImage = compilation.GetTypeByMetadataName(_IMAGE_FROM_RAW_IMAGE);
    var imageFormatWriter = compilation.GetTypeByMetadataName(_IMAGE_FORMAT_WRITER);
    var multiImageFileFormat = compilation.GetTypeByMetadataName(_MULTI_IMAGE_FILE_FORMAT);
    var imageInfoReader = compilation.GetTypeByMetadataName(_IMAGE_INFO_READER);
    var magicBytesAttr = compilation.GetTypeByMetadataName(_FORMAT_MAGIC_BYTES);
    var detectionPriorityAttr = compilation.GetTypeByMetadataName(_FORMAT_DETECTION_PRIORITY);

    if (imageFormatReader == null)
      return ImmutableArray<FormatInfo>.Empty;

    // Scan all referenced assemblies + current compilation
    var allSymbols = _GetAllNamedTypes(compilation, ct);

    foreach (var type in allSymbols) {
      ct.ThrowIfCancellationRequested();

      if (type.IsAbstract || (type.TypeKind != TypeKind.Class && type.TypeKind != TypeKind.Struct))
        continue;
      if (type.DeclaredAccessibility != Accessibility.Public)
        continue;

      var hasFormatReader = false;
      var hasToRawImage = false;
      var hasFromRawImage = false;
      var hasFormatWriter = false;
      var hasMultiImage = false;
      var hasImageInfoReader = false;

      foreach (var iface in type.AllInterfaces) {
        if (!iface.IsGenericType)
          continue;

        var typeArgs = iface.TypeArguments;
        if (typeArgs.Length != 1)
          continue;

        // Check that the type argument is the type itself (self-referential constraint)
        if (!SymbolEqualityComparer.Default.Equals(typeArgs[0], type))
          continue;

        var def = iface.OriginalDefinition;
        if (imageFormatReader != null && SymbolEqualityComparer.Default.Equals(def, imageFormatReader))
          hasFormatReader = true;
        else if (imageToRawImage != null && SymbolEqualityComparer.Default.Equals(def, imageToRawImage))
          hasToRawImage = true;
        else if (imageFromRawImage != null && SymbolEqualityComparer.Default.Equals(def, imageFromRawImage))
          hasFromRawImage = true;
        else if (imageFormatWriter != null && SymbolEqualityComparer.Default.Equals(def, imageFormatWriter))
          hasFormatWriter = true;
        else if (multiImageFileFormat != null && SymbolEqualityComparer.Default.Equals(def, multiImageFileFormat))
          hasMultiImage = true;
        else if (imageInfoReader != null && SymbolEqualityComparer.Default.Equals(def, imageInfoReader))
          hasImageInfoReader = true;
      }

      // Must implement at least one of the format interfaces
      if (!hasFormatReader)
        continue;

      var typeName = type.Name;
      var formatId = typeName.EndsWith("File") ? typeName.Substring(0, typeName.Length - 4) : typeName;

      // Deduplicate (same type from multiple assemblies)
      if (!visited.Add(formatId))
        continue;

      var fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

      // Extract [FormatMagicBytes] attributes at compile time
      var magicSignatures = new List<MagicBytesInfo>();
      if (magicBytesAttr != null) {
        foreach (var attr in type.GetAttributes()) {
          if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, magicBytesAttr))
            continue;
          if (attr.ConstructorArguments.Length < 1)
            continue;
          var sigArg = attr.ConstructorArguments[0];
          if (sigArg.Kind != TypedConstantKind.Array)
            continue;
          var bytes = new List<byte>();
          foreach (var element in sigArg.Values)
            if (element.Value is byte b)
              bytes.Add(b);
          var offset = 0;
          if (attr.ConstructorArguments.Length >= 2 && attr.ConstructorArguments[1].Value is int off)
            offset = off;
          else {
            foreach (var named in attr.NamedArguments)
              if (named.Key == "offset" && named.Value.Value is int namedOff)
                offset = namedOff;
          }
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
        detectionPriority
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

        results.Add(new FormatInfo(formatId, null, false, false, false, false, false));
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

  private static void _GenerateOutput(SourceProductionContext spc, ImmutableArray<FormatInfo> formats) {
    _GenerateEnum(spc, formats);
    _GenerateRegistration(spc, formats);
  }

  private static void _GenerateEnum(SourceProductionContext spc, ImmutableArray<FormatInfo> formats) {
    var sb = new StringBuilder();
    sb.AppendLine("// <auto-generated />");
    sb.AppendLine("#nullable enable");
    sb.AppendLine();
    sb.AppendLine("namespace Optimizer.Image;");
    sb.AppendLine();
    sb.AppendLine("/// <summary>Supported image formats, auto-generated from discovered IImageFormatReader implementations.</summary>");
    sb.AppendLine("public enum ImageFormat {");
    sb.AppendLine("  Unknown,");

    foreach (var format in formats)
      sb.Append("  ").Append(format.FormatId).AppendLine(",");

    sb.AppendLine("}");

    spc.AddSource("ImageFormat.g.cs", sb.ToString());
  }

  private static string _FormatMagicArray(MagicBytesInfo magic) {
    var sb = new StringBuilder();
    sb.Append("new FormatRegistry.MagicSignature(new byte[] { ");
    for (var i = 0; i < magic.Signature.Length; ++i) {
      if (i > 0) sb.Append(", ");
      sb.Append("0x").Append(magic.Signature[i].ToString("X2"));
    }
    sb.Append(" }, ").Append(magic.Offset).Append(", ").Append(magic.Offset + magic.Signature.Length).Append(')');
    return sb.ToString();
  }

  private static void _EmitMagicArray(StringBuilder sb, FormatInfo format) {
    if (format.MagicSignatures.Length == 0) {
      sb.Append("System.Array.Empty<FormatRegistry.MagicSignature>()");
      return;
    }

    sb.Append("new FormatRegistry.MagicSignature[] { ");
    for (var i = 0; i < format.MagicSignatures.Length; ++i) {
      if (i > 0) sb.Append(", ");
      sb.Append(_FormatMagicArray(format.MagicSignatures[i]));
    }
    sb.Append(" }");
  }

  private static void _GenerateRegistration(SourceProductionContext spc, ImmutableArray<FormatInfo> formats) {
    var sb = new StringBuilder();
    sb.AppendLine("// <auto-generated />");
    sb.AppendLine("#nullable enable");
    sb.AppendLine();
    sb.AppendLine("namespace Optimizer.Image;");
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
      sb.Append(", ").Append(format.DetectionPriority).AppendLine(");");
    }

    sb.AppendLine();
    sb.AppendLine("    // Multi-image registrations");
    foreach (var format in formats) {
      if (format.FullTypeName == null || !format.HasMultiImage)
        continue;

      if (format.HasFormatReader && format.HasToRawImage)
        sb.Append("    _RegisterMultiImageReader<").Append(format.FullTypeName).Append(">(ImageFormat.").Append(format.FormatId).AppendLine(");");
    }

    sb.AppendLine();
    sb.AppendLine("    // Info reader registrations");
    foreach (var format in formats) {
      if (format.FullTypeName == null || !format.HasImageInfoReader)
        continue;

      sb.Append("    _AugmentInfoReader<").Append(format.FullTypeName).Append(">(ImageFormat.").Append(format.FormatId).AppendLine(");");
    }

    sb.AppendLine("  }");
    sb.AppendLine("}");

    spc.AddSource("FormatRegistration.g.cs", sb.ToString());
  }

  private sealed class MagicBytesInfo {
    public byte[] Signature { get; }
    public int Offset { get; }

    public MagicBytesInfo(byte[] signature, int offset) {
      Signature = signature;
      Offset = offset;
    }
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

    public FormatInfo(
      string formatId, string? fullTypeName,
      bool hasFormatReader, bool hasToRawImage,
      bool hasFromRawImage, bool hasFormatWriter, bool hasMultiImage,
      bool hasImageInfoReader = false,
      MagicBytesInfo[]? magicSignatures = null,
      int detectionPriority = 100
    ) {
      FormatId = formatId;
      FullTypeName = fullTypeName;
      HasFormatReader = hasFormatReader;
      HasToRawImage = hasToRawImage;
      HasFromRawImage = hasFromRawImage;
      HasFormatWriter = hasFormatWriter;
      HasMultiImage = hasMultiImage;
      HasImageInfoReader = hasImageInfoReader;
      MagicSignatures = magicSignatures ?? Array.Empty<MagicBytesInfo>();
      DetectionPriority = detectionPriority;
    }
  }
}
