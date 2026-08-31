using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var arguments = args.ToList();
var checkOnly = arguments.Remove("--check");
var repositoryRoot = Path.GetFullPath(arguments.Count > 0 ? arguments[0] : Directory.GetCurrentDirectory());
var targetRoots = new[] {
  Path.Combine(repositoryRoot, "Hawkynt.FileFormats.Video"),
  Path.Combine(repositoryRoot, "Hawkynt.ImageTransformUI"),
};

var sourceFiles = targetRoots
  .Where(Directory.Exists)
  .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
  .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
  .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
  .OrderBy(path => path, StringComparer.Ordinal)
  .ToArray();

var parseOptions = CSharpParseOptions.Default
  .WithLanguageVersion(LanguageVersion.Preview)
  .WithDocumentationMode(DocumentationMode.Diagnose);
var trees = sourceFiles
  .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), parseOptions, path))
  .ToArray();

var trustedPlatformAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
  .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
  .Select(path => MetadataReference.CreateFromFile(path));
var compilation = CSharpCompilation.Create(
  "ApiDocSummaryFixer",
  trees,
  trustedPlatformAssemblies,
  new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true)
);

var editsByPath = new Dictionary<string, List<TextEdit>>(StringComparer.Ordinal);
var summaryCount = 0;
var constructorCount = 0;
var positionalPropertyCount = 0;
var seenTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

foreach (var tree in trees) {
  var model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
  var root = tree.GetRoot();
  var sourceText = tree.GetText().ToString();
  var path = tree.FilePath;

  foreach (var declaration in root.DescendantNodes().OfType<MemberDeclarationSyntax>()) {
    var symbols = GetDeclaredSymbols(model, declaration);
    var symbol = symbols.FirstOrDefault(IsReferenceVisible);
    if (symbol is null || HasSummary(symbol))
      continue;

    AddEdit(path, new(declaration.SpanStart, DocumentationInsertion(sourceText, declaration.SpanStart, SummaryFor(symbol))));
    ++summaryCount;
  }

  foreach (var typeDeclaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>()) {
    if (model.GetDeclaredSymbol(typeDeclaration) is not INamedTypeSymbol type || !IsReferenceVisible(type))
      continue;

    if (seenTypes.Add(type)) {
      var implicitParameterlessConstructor = type.InstanceConstructors.FirstOrDefault(
        constructor => constructor.IsImplicitlyDeclared
          && constructor.Parameters.Length == 0
          && IsReferenceVisible(constructor)
      );

      if (implicitParameterlessConstructor is not null && type.TypeKind == TypeKind.Class && typeDeclaration.OpenBraceToken.RawKind != 0) {
        var accessibility = AccessibilityKeyword(implicitParameterlessConstructor.DeclaredAccessibility);
        var memberIndent = GetIndent(sourceText, typeDeclaration.SpanStart) + "  ";
        var newline = DetectNewLine(sourceText);
        var insertion = newline
          + memberIndent + "/// <summary>Initializes a new instance of this type.</summary>" + newline
          + memberIndent + accessibility + " " + type.Name + "() { }";
        AddEdit(path, new(typeDeclaration.OpenBraceToken.Span.End, insertion));
        ++constructorCount;
      }
    }

    if (typeDeclaration is not RecordDeclarationSyntax { ParameterList: { } parameters } record
        || record.OpenBraceToken.RawKind == 0)
      continue;

    var propertiesToMaterialize = new List<(ParameterSyntax Parameter, IPropertySymbol Property)>();
    foreach (var parameter in parameters.Parameters) {
      if (model.GetDeclaredSymbol(parameter) is not IParameterSymbol parameterSymbol)
        continue;

      var property = type.GetMembers(parameterSymbol.Name)
        .OfType<IPropertySymbol>()
        .FirstOrDefault(candidate => candidate.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is ParameterSyntax));
      if (property is null || !IsReferenceVisible(property) || HasSummary(property))
        continue;

      propertiesToMaterialize.Add((parameter, property));
    }

    if (propertiesToMaterialize.Count == 0)
      continue;

    var newlineForRecord = DetectNewLine(sourceText);
    var propertyIndent = GetIndent(sourceText, record.SpanStart) + "  ";
    var accessors = type.TypeKind == TypeKind.Struct && !type.IsReadOnly ? "get; set;" : "get; init;";
    var propertyBlock = new StringBuilder();
    foreach (var (parameter, property) in propertiesToMaterialize) {
      propertyBlock.Append(newlineForRecord)
        .Append(propertyIndent).Append("/// <summary>").Append(SummaryFor(property)).Append("</summary>").Append(newlineForRecord)
        .Append(propertyIndent).Append("public ").Append(parameter.Type!.ToString()).Append(' ').Append(property.Name)
        .Append(" { ").Append(accessors).Append(" } = ").Append(parameter.Identifier.ValueText).Append(';');
      ++positionalPropertyCount;
    }

    AddEdit(path, new(record.OpenBraceToken.Span.End, propertyBlock.ToString()));
  }
}

var filesChanged = 0;
foreach (var (path, edits) in editsByPath.OrderBy(pair => pair.Key, StringComparer.Ordinal)) {
  if (edits.Count == 0)
    continue;

  var text = File.ReadAllText(path);
  foreach (var edit in edits.OrderByDescending(edit => edit.Position).ThenByDescending(edit => edit.Text.Length))
    text = text.Insert(edit.Position, edit.Text);

  ++filesChanged;
  if (!checkOnly)
    File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}

var generatorChanges = PatchHeaderSerializerGenerator(repositoryRoot, checkOnly);
if (generatorChanges > 0)
  ++filesChanged;

Console.WriteLine(
  $"API documentation fixer: {summaryCount} summaries, {constructorCount} explicit parameterless constructors, "
  + $"{positionalPropertyCount} materialized record properties, {generatorChanges} serializer-generator documentation patches; "
  + $"{filesChanged} files would change."
);

if (checkOnly && filesChanged > 0)
  return 1;

return 0;

void AddEdit(string path, TextEdit edit) {
  if (!editsByPath.TryGetValue(path, out var edits))
    editsByPath[path] = edits = [];

  if (!edits.Any(existing => existing.Position == edit.Position && existing.Text == edit.Text))
    edits.Add(edit);
}

static IEnumerable<ISymbol> GetDeclaredSymbols(SemanticModel model, MemberDeclarationSyntax declaration) {
  switch (declaration) {
    case BaseTypeDeclarationSyntax type:
      if (model.GetDeclaredSymbol(type) is { } typeSymbol)
        yield return typeSymbol;
      yield break;

    case DelegateDeclarationSyntax @delegate:
      if (model.GetDeclaredSymbol(@delegate) is { } delegateSymbol)
        yield return delegateSymbol;
      yield break;

    case BaseMethodDeclarationSyntax method:
      if (model.GetDeclaredSymbol(method) is { } methodSymbol)
        yield return methodSymbol;
      yield break;

    case PropertyDeclarationSyntax property:
      if (model.GetDeclaredSymbol(property) is { } propertySymbol)
        yield return propertySymbol;
      yield break;

    case IndexerDeclarationSyntax indexer:
      if (model.GetDeclaredSymbol(indexer) is { } indexerSymbol)
        yield return indexerSymbol;
      yield break;

    case EventDeclarationSyntax @event:
      if (model.GetDeclaredSymbol(@event) is { } eventSymbol)
        yield return eventSymbol;
      yield break;

    case FieldDeclarationSyntax field:
      foreach (var variable in field.Declaration.Variables)
        if (model.GetDeclaredSymbol(variable) is { } fieldSymbol)
          yield return fieldSymbol;
      yield break;

    case EventFieldDeclarationSyntax eventField:
      foreach (var variable in eventField.Declaration.Variables)
        if (model.GetDeclaredSymbol(variable) is { } eventFieldSymbol)
          yield return eventFieldSymbol;
      yield break;

    case EnumMemberDeclarationSyntax enumMember:
      if (model.GetDeclaredSymbol(enumMember) is { } enumMemberSymbol)
        yield return enumMemberSymbol;
      yield break;
  }
}

static bool IsReferenceVisible(ISymbol symbol) {
  if (symbol.DeclaredAccessibility is not (
      Accessibility.Public
      or Accessibility.Protected
      or Accessibility.ProtectedOrInternal
      or Accessibility.ProtectedAndInternal))
    return false;

  for (var containingType = symbol.ContainingType; containingType is not null; containingType = containingType.ContainingType)
    if (containingType.DeclaredAccessibility is not (
        Accessibility.Public
        or Accessibility.Protected
        or Accessibility.ProtectedOrInternal
        or Accessibility.ProtectedAndInternal))
      return false;

  return true;
}

static bool HasSummary(ISymbol symbol) {
  var xml = symbol.GetDocumentationCommentXml(expandIncludes: false);
  if (string.IsNullOrWhiteSpace(xml))
    return false;

  try {
    var document = XDocument.Parse(xml);
    return document.Descendants().Any(element => element.Name.LocalName == "summary" && element.Nodes().Any());
  } catch {
    return xml.Contains("<summary", StringComparison.Ordinal);
  }
}

static string DocumentationInsertion(string sourceText, int position, string summary) {
  var newline = DetectNewLine(sourceText);
  var indent = GetIndent(sourceText, position);
  return $"/// <summary>{summary}</summary>{newline}{indent}";
}

static string GetIndent(string text, int position) {
  var lineStart = position <= 0 ? 0 : text.LastIndexOf('\n', Math.Min(position - 1, text.Length - 1));
  lineStart = lineStart < 0 ? 0 : lineStart + 1;
  var length = 0;
  while (lineStart + length < position && text[lineStart + length] is ' ' or '\t')
    ++length;
  return text.Substring(lineStart, length);
}

static string DetectNewLine(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

static string AccessibilityKeyword(Accessibility accessibility) => accessibility switch {
  Accessibility.Public => "public",
  Accessibility.Protected => "protected",
  Accessibility.ProtectedOrInternal => "protected internal",
  Accessibility.ProtectedAndInternal => "private protected",
  _ => throw new InvalidOperationException($"Unsupported visible accessibility {accessibility}."),
};

static string SummaryFor(ISymbol symbol) => symbol switch {
  IMethodSymbol { MethodKind: MethodKind.Constructor } => "Initializes a new instance of this type.",
  IMethodSymbol method => SummaryForMethod(method),
  IPropertySymbol property => SummaryForProperty(property),
  IFieldSymbol field => SummaryForField(field),
  IEventSymbol @event => $"Occurs when {LowerFirst(Humanize(@event.Name))}.",
  INamedTypeSymbol type => SummaryForType(type),
  _ => $"Describes {LowerFirst(Humanize(symbol.Name))}.",
};

static string SummaryForMethod(IMethodSymbol method) {
  return method.Name switch {
    "Accepts" => "Determines whether the specified media stream is supported.",
    "Create" when method.ContainingType.Name.EndsWith("Decoder", StringComparison.Ordinal) => "Creates a decoder for the specified media stream.",
    "Create" when method.ContainingType.Name.EndsWith("Encoder", StringComparison.Ordinal) => "Creates an encoder for the specified media stream.",
    "Create" when method.ContainingType.Name.EndsWith("Writer", StringComparison.Ordinal) => "Creates a writer for the specified stream descriptions and metadata.",
    "Create" => "Creates a new instance for the specified inputs.",
    "TryDecode" => "Attempts to decode the specified coded packet into a raw image frame.",
    "Flush" => "Returns any frames still buffered by the decoder.",
    "FromBytes" => "Reads an instance from the specified byte array.",
    "FromFile" => "Reads an instance from the specified file.",
    "FromSpan" => "Reads an instance from the specified byte span.",
    "FromStream" => "Reads an instance from the specified stream.",
    "MatchesSignature" => "Determines whether the supplied header matches this file format.",
    "Metadata" => "Gets the metadata exposed by the specified container.",
    "ReadPackets" when method.Parameters.Any(parameter => parameter.Name == "streamIndex") => "Enumerates coded packets for the selected stream of the specified container.",
    "ReadPackets" => "Enumerates coded packets from the specified container.",
    "Streams" => "Gets the media streams declared by the specified container.",
    "WritePacket" => "Writes the specified coded packet to the container.",
    "Finish" => "Finishes writing the container and returns its encoded bytes.",
    "ReadFrom" => "Reads an instance from the specified byte span.",
    "WriteTo" => "Writes this instance to the specified byte span.",
    "GetFieldMap" or "GetGeneratedFieldMap" => "Gets descriptors for the serialized fields.",
    "Dispose" => "Releases the resources used by this instance.",
    _ when method.MethodKind is MethodKind.UserDefinedOperator or MethodKind.Conversion => $"Applies the {LowerFirst(Humanize(method.Name))} operator.",
    _ => $"Performs the {LowerFirst(Humanize(method.Name))} operation.",
  };
}

static string SummaryForProperty(IPropertySymbol property) {
  return property.Name switch {
    "CodecName" => "Gets the codec name.",
    "FileExtensions" => "Gets the file extensions supported by this format.",
    "PrimaryExtension" => "Gets the primary file extension for this format.",
    "StreamInfos" => "Gets the media streams declared by the container.",
    "Metadata" => "Gets the associated metadata.",
    _ when property.Name.StartsWith("Is", StringComparison.Ordinal) && property.Type.SpecialType == SpecialType.System_Boolean
      => $"Gets a value indicating whether {LowerFirst(Humanize(property.Name[2..]))}.",
    _ when property.Name.StartsWith("Has", StringComparison.Ordinal) && property.Type.SpecialType == SpecialType.System_Boolean
      => $"Gets a value indicating whether this instance has {LowerFirst(Humanize(property.Name[3..]))}.",
    _ when property.Name.StartsWith("Can", StringComparison.Ordinal) && property.Type.SpecialType == SpecialType.System_Boolean
      => $"Gets a value indicating whether this instance can {LowerFirst(Humanize(property.Name[3..]))}.",
    _ => $"Gets the {LowerFirst(Humanize(property.Name))}.",
  };
}

static string SummaryForField(IFieldSymbol field) {
  if (field.Name is "StructSize" or "StructSizeWithoutFrameRectangle")
    return "The serialized structure size, in bytes.";

  return $"The {LowerFirst(Humanize(field.Name))} value.";
}

static string SummaryForType(INamedTypeSymbol type) {
  var kind = type.TypeKind switch {
    TypeKind.Enum => "enumeration",
    TypeKind.Interface => "interface",
    TypeKind.Struct => "structure",
    TypeKind.Delegate => "delegate",
    _ => "type",
  };
  return $"Represents the {LowerFirst(Humanize(type.Name))} {kind}.";
}

static string Humanize(string identifier) {
  if (identifier.StartsWith("op_", StringComparison.Ordinal))
    identifier = identifier[3..];

  var spaced = Regex.Replace(identifier, "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
  return spaced.Replace('_', ' ').Trim();
}

static string LowerFirst(string value) {
  if (string.IsNullOrEmpty(value))
    return value;
  if (value.Length > 1 && char.IsUpper(value[0]) && char.IsUpper(value[1]))
    return value;
  return char.ToLowerInvariant(value[0]) + value[1..];
}

static int PatchHeaderSerializerGenerator(string repositoryRoot, bool checkOnly) {
  var path = Path.Combine(repositoryRoot, "FileFormat.Core.Generators", "HeaderSerializerGenerator.cs");
  if (!File.Exists(path))
    return 0;

  var text = File.ReadAllText(path);
  var newline = DetectNewLine(text);
  var replacements = new[] {
    (
      "  private static void _GenerateReadFrom(StringBuilder sb, HeaderModel model) {" + newline,
      "  private static void _GenerateReadFrom(StringBuilder sb, HeaderModel model) {" + newline
        + "    sb.AppendLine(\"  /// <summary>Reads an instance from the specified byte span.</summary>\");" + newline
    ),
    (
      "  private static void _GenerateSeqReadFrom(StringBuilder sb, HeaderModel model) {" + newline,
      "  private static void _GenerateSeqReadFrom(StringBuilder sb, HeaderModel model) {" + newline
        + "    sb.AppendLine(\"  /// <summary>Reads an instance from the specified byte span.</summary>\");" + newline
    ),
    (
      "  private static void _GenerateWriteTo(StringBuilder sb, HeaderModel model) {" + newline,
      "  private static void _GenerateWriteTo(StringBuilder sb, HeaderModel model) {" + newline
        + "    sb.AppendLine(\"  /// <summary>Writes this instance to the specified byte span.</summary>\");" + newline
    ),
    (
      "  private static void _GenerateSeqWriteTo(StringBuilder sb, HeaderModel model) {" + newline,
      "  private static void _GenerateSeqWriteTo(StringBuilder sb, HeaderModel model) {" + newline
        + "    sb.AppendLine(\"  /// <summary>Writes this instance to the specified byte span.</summary>\");" + newline
    ),
  };

  var changes = 0;
  foreach (var (before, after) in replacements) {
    if (text.Contains(after, StringComparison.Ordinal))
      continue;
    if (!text.Contains(before, StringComparison.Ordinal))
      throw new InvalidOperationException($"Could not locate serializer-generator insertion point: {before.Trim()}");
    text = text.Replace(before, after, StringComparison.Ordinal);
    ++changes;
  }

  if (changes > 0 && !checkOnly)
    File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

  return changes;
}

readonly record struct TextEdit(int Position, string Text);
