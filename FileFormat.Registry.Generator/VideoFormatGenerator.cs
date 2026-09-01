using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace FileFormat.Registry.Generator;

/// <summary>
/// Emits the video registry: a <c>VideoFormat</c> enum over the discovered container readers and a
/// <c>VideoFormatRegistration.RegisterAll()</c> that registers every container and every codec
/// decoder by static-interface dispatch.
/// </summary>
/// <remarks>
/// A generator of its own rather than more branches inside <see cref="ImageFormatGenerator"/>,
/// because what it discovers is disjoint: no type is both an image format and a container, and the
/// two registries have nothing in common but the namespace they are emitted into.
/// <para/>
/// Containers and decoders are registered separately and never reference each other. That is the
/// whole point of the split — a codec added later is discovered and registered without a container
/// being recompiled to mention it, and a container added later serves every codec already here.
/// <para/>
/// Emits nothing at all when a compilation holds neither, so that a project which only reads images
/// does not acquire an empty video registry it would then have to supply the plumbing for.
/// </remarks>
[Generator]
public sealed class VideoFormatGenerator : IIncrementalGenerator {

  private const string _VIDEO_CONTAINER_READER = "FileFormat.Core.IVideoContainerReader`1";
  private const string _VIDEO_CONTAINER_WRITER = "FileFormat.Core.IVideoContainerWriter`1";
  private const string _VIDEO_CODEC_DECODER = "FileFormat.Core.IVideoCodecDecoder`1";
  private const string _VIDEO_CODEC_ENCODER = "FileFormat.Core.IVideoCodecEncoder`1";
  private const string _FORMAT_MAGIC_BYTES = "FileFormat.Core.FormatMagicBytesAttribute";
  private const string _FORMAT_DETECTION_PRIORITY = "FileFormat.Core.FormatDetectionPriorityAttribute";
  private const string _FORMAT_MIME_TYPE = "FileFormat.Core.FormatMimeTypeAttribute";

  private const string _NAMESPACE_PROPERTY = "build_property.FileFormatRegistryNamespace";
  private const string _DEFAULT_NAMESPACE = "Optimizer.Image";

  public void Initialize(IncrementalGeneratorInitializationContext context) {
    var nsOption = context.AnalyzerConfigOptionsProvider.Select(static (options, _) => {
      options.GlobalOptions.TryGetValue(_NAMESPACE_PROPERTY, out var ns);
      return string.IsNullOrWhiteSpace(ns) ? _DEFAULT_NAMESPACE : ns!;
    });

    var discovered = context.CompilationProvider.Select(static (compilation, ct) => _Discover(compilation, ct));
    context.RegisterSourceOutput(discovered.Combine(nsOption), static (spc, pair) => _GenerateOutput(spc, pair.Left, pair.Right));
  }

  private static VideoDiscovery _Discover(Compilation compilation, CancellationToken ct) {
    var containerReader = compilation.GetTypeByMetadataName(_VIDEO_CONTAINER_READER);
    var containerWriter = compilation.GetTypeByMetadataName(_VIDEO_CONTAINER_WRITER);
    var codecDecoder = compilation.GetTypeByMetadataName(_VIDEO_CODEC_DECODER);
    var codecEncoder = compilation.GetTypeByMetadataName(_VIDEO_CODEC_ENCODER);

    // FileFormat.Core is not referenced at all: nothing to discover and nothing to emit.
    if (containerReader == null && codecDecoder == null)
      return VideoDiscovery.Empty;

    var magicBytesAttr = compilation.GetTypeByMetadataName(_FORMAT_MAGIC_BYTES);
    var detectionPriorityAttr = compilation.GetTypeByMetadataName(_FORMAT_DETECTION_PRIORITY);
    var mimeTypeAttr = compilation.GetTypeByMetadataName(_FORMAT_MIME_TYPE);

    var containers = new List<ContainerInfo>();
    var codecs = new List<CodecInfo>();
    var seenContainers = new HashSet<string>();
    var seenCodecs = new HashSet<string>();

    foreach (var type in _GetAllNamedTypes(compilation, ct)) {
      ct.ThrowIfCancellationRequested();

      if (type.IsAbstract || (type.TypeKind != TypeKind.Class && type.TypeKind != TypeKind.Struct))
        continue;
      if (type.DeclaredAccessibility != Accessibility.Public)
        continue;

      var isContainerReader = false;
      var isContainerWriter = false;
      var isDecoder = false;
      var isEncoder = false;

      foreach (var iface in type.AllInterfaces) {
        if (!iface.IsGenericType)
          continue;

        var typeArgs = iface.TypeArguments;
        if (typeArgs.Length != 1)
          continue;

        // The self-referential constraint: an interface parameterised on something else is not this
        // type's declaration of itself.
        if (!SymbolEqualityComparer.Default.Equals(typeArgs[0], type))
          continue;

        var def = iface.OriginalDefinition;
        if (containerReader != null && SymbolEqualityComparer.Default.Equals(def, containerReader))
          isContainerReader = true;
        else if (containerWriter != null && SymbolEqualityComparer.Default.Equals(def, containerWriter))
          isContainerWriter = true;
        else if (codecDecoder != null && SymbolEqualityComparer.Default.Equals(def, codecDecoder))
          isDecoder = true;
        else if (codecEncoder != null && SymbolEqualityComparer.Default.Equals(def, codecEncoder))
          isEncoder = true;
      }

      var fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

      if (isDecoder || isEncoder) {
        var codecId = _TrimSuffix(_TrimSuffix(type.Name, "Decoder"), "Encoder");
        if (seenCodecs.Add(fullName))
          codecs.Add(new CodecInfo(codecId, fullName, isDecoder, isEncoder));
      }

      if (!isContainerReader && !isContainerWriter)
        continue;

      var formatId = _TrimSuffix(_TrimSuffix(type.Name, "Container"), "File");
      if (!seenContainers.Add(formatId))
        continue;

      containers.Add(new ContainerInfo(
        formatId,
        fullName,
        isContainerReader,
        isContainerWriter,
        _ReadMagicBytes(type, magicBytesAttr),
        _ReadDetectionPriority(type, detectionPriorityAttr),
        _ReadMimeTypes(type, mimeTypeAttr)));
    }

    containers.Sort(static (a, b) => StringComparer.Ordinal.Compare(a.FormatId, b.FormatId));
    codecs.Sort(static (a, b) => StringComparer.Ordinal.Compare(a.CodecId, b.CodecId));

    return new VideoDiscovery(containers.ToImmutableArray(), codecs.ToImmutableArray());
  }

  private static string _TrimSuffix(string value, string suffix)
    => value.Length > suffix.Length && value.EndsWith(suffix, StringComparison.Ordinal)
      ? value.Substring(0, value.Length - suffix.Length)
      : value;

  private static MagicBytesInfo[] _ReadMagicBytes(INamedTypeSymbol type, INamedTypeSymbol? attribute) {
    if (attribute == null)
      return Array.Empty<MagicBytesInfo>();

    var result = new List<MagicBytesInfo>();
    foreach (var attr in type.GetAttributes()) {
      if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attribute))
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
        result.Add(new MagicBytesInfo(bytes.ToArray(), offset));
    }

    return result.ToArray();
  }

  private static int _ReadDetectionPriority(INamedTypeSymbol type, INamedTypeSymbol? attribute) {
    if (attribute == null)
      return 100;

    foreach (var attr in type.GetAttributes()) {
      if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attribute))
        continue;
      if (attr.ConstructorArguments.Length >= 1 && attr.ConstructorArguments[0].Value is int priority)
        return priority;
    }

    return 100;
  }

  private static string[] _ReadMimeTypes(INamedTypeSymbol type, INamedTypeSymbol? attribute) {
    if (attribute == null)
      return Array.Empty<string>();

    var result = new List<string>();
    foreach (var attr in type.GetAttributes()) {
      if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attribute))
        continue;
      if (attr.ConstructorArguments.Length < 1)
        continue;

      var arg = attr.ConstructorArguments[0];
      if (arg.Kind != TypedConstantKind.Array)
        continue;

      foreach (var element in arg.Values)
        if (element.Value is string s && !string.IsNullOrEmpty(s))
          result.Add(s);
    }

    return result.ToArray();
  }

  private static IEnumerable<INamedTypeSymbol> _GetAllNamedTypes(Compilation compilation, CancellationToken ct) {
    foreach (var type in _GetTypesFromNamespace(compilation.GlobalNamespace, ct))
      yield return type;

    foreach (var reference in compilation.References) {
      ct.ThrowIfCancellationRequested();
      if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
        continue;

      foreach (var type in _GetTypesFromNamespace(assembly.GlobalNamespace, ct))
        yield return type;
    }
  }

  private static IEnumerable<INamedTypeSymbol> _GetTypesFromNamespace(INamespaceSymbol ns, CancellationToken ct) {
    ct.ThrowIfCancellationRequested();

    foreach (var member in ns.GetTypeMembers())
      yield return member;

    foreach (var child in ns.GetNamespaceMembers())
      foreach (var type in _GetTypesFromNamespace(child, ct))
        yield return type;
  }

  private static void _GenerateOutput(SourceProductionContext spc, VideoDiscovery discovery, string targetNamespace) {
    if (discovery.Containers.Length == 0 && discovery.Codecs.Length == 0)
      return;

    _GenerateEnum(spc, discovery, targetNamespace);
    _GenerateRegistration(spc, discovery, targetNamespace);
  }

  private static void _GenerateEnum(SourceProductionContext spc, VideoDiscovery discovery, string targetNamespace) {
    var sb = new StringBuilder();
    sb.AppendLine("// <auto-generated />");
    sb.AppendLine("#nullable enable");
    sb.AppendLine();
    sb.Append("namespace ").Append(targetNamespace).AppendLine(";");
    sb.AppendLine();
    sb.AppendLine("/// <summary>Supported video containers, auto-generated from discovered IVideoContainerReader/IVideoContainerWriter implementations.</summary>");
    sb.AppendLine("public enum VideoFormat {");
    sb.AppendLine("  /// <summary>Represents an unknown or unspecified video container format.</summary>");
    sb.AppendLine("  Unknown,");

    foreach (var container in discovery.Containers) {
      sb.Append("  /// <summary>Represents the ").Append(container.FormatId).AppendLine(" video container format.</summary>");
      sb.Append("  ").Append(container.FormatId).AppendLine(",");
    }

    sb.AppendLine("}");

    spc.AddSource("VideoFormat.g.cs", sb.ToString());
  }

  private static void _GenerateRegistration(SourceProductionContext spc, VideoDiscovery discovery, string targetNamespace) {
    var sb = new StringBuilder();
    sb.AppendLine("// <auto-generated />");
    sb.AppendLine("#nullable enable");
    sb.AppendLine();
    sb.Append("namespace ").Append(targetNamespace).AppendLine(";");
    sb.AppendLine();
    sb.AppendLine("internal static partial class VideoFormatRegistration {");
    sb.AppendLine("  static partial void RegisterAll() {");

    foreach (var container in discovery.Containers) {
      if (!container.HasReader)
        continue;

      sb.Append("    _RegisterContainer<").Append(container.FullTypeName).Append(">(VideoFormat.").Append(container.FormatId).Append(", ");
      _EmitMagicArray(sb, container.MagicSignatures);
      sb.Append(", ").Append(container.DetectionPriority).Append(", ");
      _EmitStringArray(sb, container.MimeTypes);
      sb.AppendLine(");");
    }

    sb.AppendLine();
    sb.AppendLine("    // Codec registrations — one per decoder, keyed by nothing the containers know about.");
    foreach (var codec in discovery.Codecs) {
      if (!codec.HasDecoder)
        continue;

      sb.Append("    _RegisterDecoder<").Append(codec.FullTypeName).AppendLine(">();");
    }

    sb.AppendLine("  }");
    sb.AppendLine("}");

    spc.AddSource("VideoFormatRegistration.g.cs", sb.ToString());
  }

  private static void _EmitMagicArray(StringBuilder sb, MagicBytesInfo[] signatures) {
    if (signatures.Length == 0) {
      sb.Append("System.Array.Empty<MagicSignature>()");
      return;
    }

    sb.Append("new MagicSignature[] { ");
    for (var i = 0; i < signatures.Length; ++i) {
      if (i > 0)
        sb.Append(", ");

      var magic = signatures[i];
      sb.Append("new MagicSignature(new byte[] { ");
      for (var j = 0; j < magic.Signature.Length; ++j) {
        if (j > 0)
          sb.Append(", ");
        sb.Append("0x").Append(magic.Signature[j].ToString("X2"));
      }

      sb.Append(" }, ").Append(magic.Offset).Append(", ").Append(magic.Offset + magic.Signature.Length).Append(')');
    }

    sb.Append(" }");
  }

  private static void _EmitStringArray(StringBuilder sb, string[] values) {
    if (values.Length == 0) {
      sb.Append("System.Array.Empty<string>()");
      return;
    }

    sb.Append("new string[] { ");
    for (var i = 0; i < values.Length; ++i) {
      if (i > 0)
        sb.Append(", ");
      sb.Append('\"').Append(values[i].Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('\"');
    }

    sb.Append(" }");
  }

  private sealed class VideoDiscovery {

    public static readonly VideoDiscovery Empty = new(ImmutableArray<ContainerInfo>.Empty, ImmutableArray<CodecInfo>.Empty);

    public ImmutableArray<ContainerInfo> Containers { get; }
    public ImmutableArray<CodecInfo> Codecs { get; }

    public VideoDiscovery(ImmutableArray<ContainerInfo> containers, ImmutableArray<CodecInfo> codecs) {
      this.Containers = containers;
      this.Codecs = codecs;
    }
  }

  private sealed class ContainerInfo {

    public string FormatId { get; }
    public string FullTypeName { get; }
    public bool HasReader { get; }
    public bool HasWriter { get; }
    public MagicBytesInfo[] MagicSignatures { get; }
    public int DetectionPriority { get; }
    public string[] MimeTypes { get; }

    public ContainerInfo(
      string formatId, string fullTypeName, bool hasReader, bool hasWriter,
      MagicBytesInfo[] magicSignatures, int detectionPriority, string[] mimeTypes) {
      this.FormatId = formatId;
      this.FullTypeName = fullTypeName;
      this.HasReader = hasReader;
      this.HasWriter = hasWriter;
      this.MagicSignatures = magicSignatures;
      this.DetectionPriority = detectionPriority;
      this.MimeTypes = mimeTypes;
    }
  }

  private sealed class CodecInfo {

    public string CodecId { get; }
    public string FullTypeName { get; }
    public bool HasDecoder { get; }
    public bool HasEncoder { get; }

    public CodecInfo(string codecId, string fullTypeName, bool hasDecoder, bool hasEncoder) {
      this.CodecId = codecId;
      this.FullTypeName = fullTypeName;
      this.HasDecoder = hasDecoder;
      this.HasEncoder = hasEncoder;
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
