using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace FileFormat.Registry.Generator;

[Generator]
public sealed class ImageFormatGenerator : IIncrementalGenerator {

  private const string _IMAGE_FILE_FORMAT = "FileFormat.Core.IImageFileFormat`1";
  private const string _IMAGE_FORMAT_READER = "FileFormat.Core.IImageFormatReader`1";
  private const string _IMAGE_TO_RAW_IMAGE = "FileFormat.Core.IImageToRawImage`1";
  private const string _IMAGE_FROM_RAW_IMAGE = "FileFormat.Core.IImageFromRawImage`1";
  private const string _IMAGE_FORMAT_WRITER = "FileFormat.Core.IImageFormatWriter`1";
  private const string _MULTI_IMAGE_FILE_FORMAT = "FileFormat.Core.IMultiImageFileFormat`1";
  private const string _ADDITIONAL_IMAGE_FORMAT = "FileFormat.Core.AdditionalImageFormatAttribute";

  public void Initialize(IncrementalGeneratorInitializationContext context) {
    var formatTypes = context.CompilationProvider.Select(static (compilation, ct) => _DiscoverFormats(compilation, ct));
    context.RegisterSourceOutput(formatTypes, static (spc, formats) => _GenerateOutput(spc, formats));
  }

  private static ImmutableArray<FormatInfo> _DiscoverFormats(Compilation compilation, System.Threading.CancellationToken ct) {
    var results = new List<FormatInfo>();
    var visited = new HashSet<string>();

    // Resolve interface symbols by metadata name for reliable comparison
    var imageFileFormat = compilation.GetTypeByMetadataName(_IMAGE_FILE_FORMAT);
    var imageFormatReader = compilation.GetTypeByMetadataName(_IMAGE_FORMAT_READER);
    var imageToRawImage = compilation.GetTypeByMetadataName(_IMAGE_TO_RAW_IMAGE);
    var imageFromRawImage = compilation.GetTypeByMetadataName(_IMAGE_FROM_RAW_IMAGE);
    var imageFormatWriter = compilation.GetTypeByMetadataName(_IMAGE_FORMAT_WRITER);
    var multiImageFileFormat = compilation.GetTypeByMetadataName(_MULTI_IMAGE_FILE_FORMAT);

    // If we can't resolve IImageFileFormat, the core library isn't referenced
    if (imageFileFormat == null && imageFormatReader == null)
      return ImmutableArray<FormatInfo>.Empty;

    // Scan all referenced assemblies + current compilation
    var allSymbols = _GetAllNamedTypes(compilation, ct);

    foreach (var type in allSymbols) {
      ct.ThrowIfCancellationRequested();

      if (type.IsAbstract || (type.TypeKind != TypeKind.Class && type.TypeKind != TypeKind.Struct))
        continue;
      if (type.DeclaredAccessibility != Accessibility.Public)
        continue;

      var hasImageFileFormat = false;
      var hasFormatReader = false;
      var hasToRawImage = false;
      var hasFromRawImage = false;
      var hasFormatWriter = false;
      var hasMultiImage = false;

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
        if (imageFileFormat != null && SymbolEqualityComparer.Default.Equals(def, imageFileFormat))
          hasImageFileFormat = true;
        else if (imageFormatReader != null && SymbolEqualityComparer.Default.Equals(def, imageFormatReader))
          hasFormatReader = true;
        else if (imageToRawImage != null && SymbolEqualityComparer.Default.Equals(def, imageToRawImage))
          hasToRawImage = true;
        else if (imageFromRawImage != null && SymbolEqualityComparer.Default.Equals(def, imageFromRawImage))
          hasFromRawImage = true;
        else if (imageFormatWriter != null && SymbolEqualityComparer.Default.Equals(def, imageFormatWriter))
          hasFormatWriter = true;
        else if (multiImageFileFormat != null && SymbolEqualityComparer.Default.Equals(def, multiImageFileFormat))
          hasMultiImage = true;
      }

      // Must implement at least one of the format interfaces
      if (!hasImageFileFormat && !hasFormatReader)
        continue;

      var typeName = type.Name;
      var formatId = typeName.EndsWith("File") ? typeName.Substring(0, typeName.Length - 4) : typeName;

      // Deduplicate (same type from multiple assemblies)
      if (!visited.Add(formatId))
        continue;

      var fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

      results.Add(new FormatInfo(
        formatId,
        fullName,
        hasImageFileFormat,
        hasFormatReader,
        hasToRawImage,
        hasFromRawImage,
        hasFormatWriter,
        hasMultiImage
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

        results.Add(new FormatInfo(formatId, null, false, false, false, false, false, false));
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
    sb.AppendLine("/// <summary>Supported image formats, auto-generated from discovered IImageFileFormat implementations.</summary>");
    sb.AppendLine("public enum ImageFormat {");
    sb.AppendLine("  Unknown,");

    foreach (var format in formats)
      sb.Append("  ").Append(format.FormatId).AppendLine(",");

    sb.AppendLine("}");

    spc.AddSource("ImageFormat.g.cs", sb.ToString());
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

      if (format.HasImageFileFormat)
        sb.Append("    _Register<").Append(format.FullTypeName).Append(">(ImageFormat.").Append(format.FormatId).AppendLine(");");
      else if (format.HasFormatReader && format.HasToRawImage && format.HasFromRawImage && format.HasFormatWriter)
        sb.Append("    _RegisterReaderWriter<").Append(format.FullTypeName).Append(">(ImageFormat.").Append(format.FormatId).AppendLine(");");
      else if (format.HasFormatReader && format.HasToRawImage)
        sb.Append("    _RegisterReader<").Append(format.FullTypeName).Append(">(ImageFormat.").Append(format.FormatId).AppendLine(");");
      else if (format.HasFormatReader)
        sb.Append("    _RegisterReader<").Append(format.FullTypeName).Append(">(ImageFormat.").Append(format.FormatId).AppendLine(");");
    }

    sb.AppendLine();
    sb.AppendLine("    // Multi-image registrations");
    foreach (var format in formats) {
      if (format.FullTypeName == null || !format.HasMultiImage)
        continue;

      if (format.HasImageFileFormat)
        sb.Append("    _RegisterMultiImage<").Append(format.FullTypeName).Append(">(ImageFormat.").Append(format.FormatId).AppendLine(");");
      else if (format.HasFormatReader && format.HasToRawImage)
        sb.Append("    _RegisterMultiImageReader<").Append(format.FullTypeName).Append(">(ImageFormat.").Append(format.FormatId).AppendLine(");");
    }

    sb.AppendLine("  }");
    sb.AppendLine("}");

    spc.AddSource("FormatRegistration.g.cs", sb.ToString());
  }

  private sealed class FormatInfo {
    public string FormatId { get; }
    public string? FullTypeName { get; }
    public bool HasImageFileFormat { get; }
    public bool HasFormatReader { get; }
    public bool HasToRawImage { get; }
    public bool HasFromRawImage { get; }
    public bool HasFormatWriter { get; }
    public bool HasMultiImage { get; }

    public FormatInfo(
      string formatId, string? fullTypeName,
      bool hasImageFileFormat, bool hasFormatReader, bool hasToRawImage,
      bool hasFromRawImage, bool hasFormatWriter, bool hasMultiImage
    ) {
      FormatId = formatId;
      FullTypeName = fullTypeName;
      HasImageFileFormat = hasImageFileFormat;
      HasFormatReader = hasFormatReader;
      HasToRawImage = hasToRawImage;
      HasFromRawImage = hasFromRawImage;
      HasFormatWriter = hasFormatWriter;
      HasMultiImage = hasMultiImage;
    }
  }
}
