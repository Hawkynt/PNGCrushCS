using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Hawkynt.ColorProcessing.Adapter.Generators;

/// <summary>
/// Emits a name-to-type dispatch for the colour library's quantizers and ditherers.
/// </summary>
/// <remarks>
/// Selecting one of these by name at run time used to mean reflection: find the type, build a
/// closed generic method, invoke it. That cannot be compiled ahead of time and cannot be trimmed,
/// because nothing in the program text says which pairs will be needed — the linker has to keep
/// everything or risk keeping too little.
/// <para/>
/// Doing it here instead turns the same choice into a switch the compiler can see. Every pair that
/// can be selected appears as a real call in the source, so the trimmer keeps exactly those and
/// ahead-of-time compilation has everything it needs. It also means a name that does not exist is
/// a build-time absence rather than a run-time exception.
/// <para/>
/// Built with <c>new T()</c>, never <c>default(T)</c>: these are structs whose settings live in
/// property initialisers that only an explicit parameterless constructor runs, so
/// <c>default(T)</c> zeroes them. <c>PngQuantQuantizer.MedianCutIterations</c> read as 0 that way,
/// its loop never ran, and the palette it fills stayed null. On a struct with no explicit
/// constructor <c>new T()</c> compiles to <c>default(T)</c> anyway.
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class ColorReductionDispatchGenerator : IIncrementalGenerator {

  private const string _QUANTIZER = "Hawkynt.ColorProcessing.IQuantizer";
  private const string _DITHERER = "Hawkynt.ColorProcessing.IDitherer";

  public void Initialize(IncrementalGeneratorInitializationContext context) {
    var models = context.CompilationProvider.Select(static (compilation, _) => _Collect(compilation));
    context.RegisterSourceOutput(models, static (production, model) => _Emit(production, model));
  }

  /// <summary>One selectable entry: the name a caller asks for and the expression that builds it.</summary>
  private readonly struct Entry {

    public Entry(string name, string expression) {
      this.Name = name;
      this.Expression = expression;
    }

    public string Name { get; }
    public string Expression { get; }
  }

  private readonly struct Model {

    public Model(ImmutableArray<Entry> quantizers, ImmutableArray<Entry> ditherers) {
      this.Quantizers = quantizers;
      this.Ditherers = ditherers;
    }

    public ImmutableArray<Entry> Quantizers { get; }
    public ImmutableArray<Entry> Ditherers { get; }
  }

  private static Model _Collect(Compilation compilation) {
    var quantizer = compilation.GetTypeByMetadataName(_QUANTIZER);
    var ditherer = compilation.GetTypeByMetadataName(_DITHERER);
    if (quantizer == null || ditherer == null)
      return new(ImmutableArray<Entry>.Empty, ImmutableArray<Entry>.Empty);

    var quantizers = new List<Entry>();
    var ditherers = new List<Entry>();

    foreach (var type in _AllTypes(compilation)) {
      if (!type.IsValueType || type.DeclaredAccessibility != Accessibility.Public || type.IsGenericType)
        continue;

      var implements = type.AllInterfaces;
      if (implements.Any(i => SymbolEqualityComparer.Default.Equals(i, quantizer)))
        quantizers.Add(new(type.Name, $"new global::{type.ToDisplayString()}()"));
      else if (implements.Any(i => SymbolEqualityComparer.Default.Equals(i, ditherer)))
        _AddDitherer(type, ditherers);
    }

    return new(
      quantizers.OrderBy(e => e.Name, StringComparer.Ordinal).ToImmutableArray(),
      ditherers.OrderBy(e => e.Name, StringComparer.Ordinal).ToImmutableArray());
  }

  /// <summary>
  /// Adds a ditherer, once per configuration it offers.
  /// </summary>
  /// <remarks>
  /// A ditherer that carries settings — a diffusion kernel, an ordered matrix — cannot be built
  /// from nothing: its default is an empty kernel, which indexes past its own array the moment it
  /// runs. Those types publish their configurations as static properties of their own type, and
  /// each of them is a separate thing a caller might ask for, so each becomes its own name.
  /// </remarks>
  private static void _AddDitherer(INamedTypeSymbol type, List<Entry> into) {
    var name = type.ToDisplayString();
    var presets = type.GetMembers()
      .OfType<IPropertySymbol>()
      .Where(p => p.IsStatic
                  && p.DeclaredAccessibility == Accessibility.Public
                  && SymbolEqualityComparer.Default.Equals(p.Type, type))
      .ToList();

    if (presets.Count == 0) {
      into.Add(new(type.Name, $"new global::{name}()"));
      return;
    }

    foreach (var preset in presets)
      into.Add(new($"{type.Name}.{preset.Name}", $"global::{name}.{preset.Name}"));
  }

  private static IEnumerable<INamedTypeSymbol> _AllTypes(Compilation compilation) {
    foreach (var reference in compilation.References) {
      if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
        continue;

      // Only the colour library is walked; every other reference would cost a full namespace walk
      // for nothing.
      if (assembly.Name != "System.Drawing.Extensions")
        continue;

      foreach (var type in _Walk(assembly.GlobalNamespace))
        yield return type;
    }
  }

  private static IEnumerable<INamedTypeSymbol> _Walk(INamespaceSymbol space) {
    foreach (var type in space.GetTypeMembers())
      yield return type;

    foreach (var nested in space.GetNamespaceMembers())
    foreach (var type in _Walk(nested))
      yield return type;
  }

  private static void _Emit(SourceProductionContext production, Model model) {
    if (model.Quantizers.IsDefaultOrEmpty || model.Ditherers.IsDefaultOrEmpty)
      return;

    var text = new StringBuilder();
    text.AppendLine("// <auto-generated/>");
    text.AppendLine("#nullable enable");
    text.AppendLine("using System;");
    text.AppendLine("using FileFormat.Core;");
    text.AppendLine();
    text.AppendLine("namespace Hawkynt.ColorProcessing.Adapter;");
    text.AppendLine();
    text.AppendLine("/// <summary>Selects a quantizer and a ditherer by name, without reflecting.</summary>");
    text.AppendLine("/// <remarks>");
    text.AppendLine("/// Generated from the colour library the build referenced, so the list is whatever that");
    text.AppendLine("/// version offers rather than a copy that can fall behind it.");
    text.AppendLine("/// </remarks>");
    text.AppendLine("public static partial class ColorReductionDispatch {");
    text.AppendLine();

    _EmitNames(text, "QuantizerNames", model.Quantizers);
    _EmitNames(text, "DithererNames", model.Ditherers);

    text.AppendLine("  /// <summary>Reduces a picture using the named quantizer and ditherer.</summary>");
    text.AppendLine("  /// <exception cref=\"ArgumentException\">Thrown when a name is not one that was generated.</exception>");
    text.AppendLine("  public static RawImage Reduce(RawImage image, string quantizer, string ditherer, int colors)");
    text.AppendLine("    => quantizer switch {");

    foreach (var entry in model.Quantizers)
      text.AppendLine($"      \"{entry.Name}\" => _WithQuantizer(image, {entry.Expression}, ditherer, colors),");

    text.AppendLine("      _ => throw new ArgumentException($\"Unknown quantizer '{quantizer}'.\", nameof(quantizer)),");
    text.AppendLine("    };");
    text.AppendLine();
    text.AppendLine("  private static RawImage _WithQuantizer<TQuantizer>(");
    text.AppendLine("    RawImage image, TQuantizer quantizer, string ditherer, int colors)");
    text.AppendLine("    where TQuantizer : struct, IQuantizer");
    text.AppendLine("    => ditherer switch {");

    foreach (var entry in model.Ditherers)
      text.AppendLine(
        $"      \"{entry.Name}\" => RawImageQuantization.Reduce(image, quantizer, {entry.Expression}, colors),");

    text.AppendLine("      _ => throw new ArgumentException($\"Unknown ditherer '{ditherer}'.\", nameof(ditherer)),");
    text.AppendLine("    };");
    text.AppendLine("}");

    production.AddSource("ColorReductionDispatch.g.cs", text.ToString());
  }

  private static void _EmitNames(StringBuilder text, string property, ImmutableArray<Entry> entries) {
    text.AppendLine($"  /// <summary>Every name this build can select.</summary>");
    text.AppendLine($"  public static string[] {property} => [");

    foreach (var entry in entries)
      text.AppendLine($"    \"{entry.Name}\",");

    text.AppendLine("  ];");
    text.AppendLine();
  }
}
