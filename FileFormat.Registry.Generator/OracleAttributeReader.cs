using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace FileFormat.Registry.Generator;

/// <summary>
/// Reads <c>[VerifiedBy]</c> off a format type and turns it back into source.
/// </summary>
/// <remarks>
/// Shared by both registries because the claim means the same thing on either side of them: a tool
/// nobody here wrote has read what this writer produced. An enum argument arrives as its underlying
/// number, so the member it was written as has to be recovered from the enum's own fields — emitting
/// the number would compile and would make the generated registration unreadable, which is the sort
/// of thing that stops being checked.
/// </remarks>
internal static class OracleAttributeReader {

  /// <summary>The oracle members named by <c>[VerifiedBy]</c> on this type, in declaration order.</summary>
  public static string[] Read(INamedTypeSymbol type, INamedTypeSymbol? attribute) {
    if (attribute == null)
      return Array.Empty<string>();

    var names = new List<string>();

    foreach (var attr in type.GetAttributes()) {
      if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attribute))
        continue;
      if (attr.ConstructorArguments.Length < 1)
        continue;

      var arg = attr.ConstructorArguments[0];
      if (arg.Kind != TypedConstantKind.Array)
        continue;

      foreach (var element in arg.Values) {
        var name = _MemberName(element);
        if (name != null && !names.Contains(name))
          names.Add(name);
      }
    }

    return names.ToArray();
  }

  /// <summary>Emits the array literal the registration call takes, or the empty array.</summary>
  public static void Emit(StringBuilder sb, string[] oracleMembers) {
    if (oracleMembers.Length == 0) {
      sb.Append("System.Array.Empty<global::FileFormat.Core.ConformanceOracle>()");
      return;
    }

    sb.Append("new global::FileFormat.Core.ConformanceOracle[] { ");
    for (var i = 0; i < oracleMembers.Length; ++i) {
      if (i > 0)
        sb.Append(", ");

      sb.Append("global::FileFormat.Core.ConformanceOracle.").Append(oracleMembers[i]);
    }

    sb.Append(" }");
  }

  private static string? _MemberName(TypedConstant constant) {
    if (constant.Type is not INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType || constant.Value == null)
      return null;

    var value = Convert.ToInt64(constant.Value);

    return enumType
      .GetMembers()
      .OfType<IFieldSymbol>()
      .Where(field => field.HasConstantValue && field.ConstantValue != null)
      .Where(field => Convert.ToInt64(field.ConstantValue) == value)
      .Select(field => field.Name)
      .FirstOrDefault(name => name != "None");
  }
}
