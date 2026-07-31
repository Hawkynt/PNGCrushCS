using System;
using System.Linq;

namespace Hawkynt.ColorProcessing.Adapter;

/// <summary>Accepts the names callers already use, alongside the generated ones.</summary>
/// <remarks>
/// The generated names are the type's own — <c>WuQuantizer</c>, <c>ErrorDiffusion.FloydSteinberg</c>
/// — because that is what can be checked at build time. The names in use predate the generator and
/// were the old registry's: shorter for a quantizer, underscored for a ditherer, and sometimes
/// spaced. Rather than break a command line that already works, those are translated here.
/// <para/>
/// The translation is deliberately narrow. It fixes up spelling, never guesses between two
/// candidates: a name that matches nothing is still refused, which is the whole point of having
/// generated the list.
/// </remarks>
public static partial class ColorReductionDispatch {

  /// <summary>Reduces a picture, accepting either the generated names or the older ones.</summary>
  public static RawImageResult ReduceByName(
    FileFormat.Core.RawImage image, string quantizer, string ditherer, int colors) {
    var resolvedQuantizer = ResolveQuantizer(quantizer)
      ?? throw new ArgumentException($"Unknown quantizer '{quantizer}'.", nameof(quantizer));
    var resolvedDitherer = ResolveDitherer(ditherer)
      ?? throw new ArgumentException($"Unknown ditherer '{ditherer}'.", nameof(ditherer));

    return new(Reduce(image, resolvedQuantizer, resolvedDitherer, colors), resolvedQuantizer, resolvedDitherer);
  }

  /// <summary>The reduced picture and the names that were actually used to make it.</summary>
  public readonly record struct RawImageResult(FileFormat.Core.RawImage Image, string Quantizer, string Ditherer);

  /// <summary>Finds the generated name a caller's quantizer name means, or null.</summary>
  public static string? ResolveQuantizer(string name) => _Resolve(name, QuantizerNames, "Quantizer");

  /// <summary>Finds the generated name a caller's ditherer name means, or null.</summary>
  public static string? ResolveDitherer(string name) => _Resolve(name, DithererNames, "Ditherer");

  private static string? _Resolve(string name, string[] known, string suffix) {
    if (string.IsNullOrWhiteSpace(name))
      return null;

    // Spaces were how the old registry spelled a two-word name; underscores were how it joined a
    // ditherer to its configuration, which is now a dot.
    var candidates = new[] {
      name,
      name.Replace(" ", string.Empty),
      name.Replace("_", "."),
      name.Replace(" ", string.Empty) + suffix,
      name.Replace("_", ".").Replace(" ", string.Empty),
    };

    foreach (var candidate in candidates) {
      var hit = known.FirstOrDefault(k => string.Equals(k, candidate, StringComparison.OrdinalIgnoreCase));
      if (hit != null)
        return hit;
    }

    // A bare type name where only configurations exist: take the first, which is how the old
    // registry behaved when asked for a family rather than a member of one.
    var prefix = name.Replace(" ", string.Empty) + ".";

    return known.FirstOrDefault(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
  }
}
