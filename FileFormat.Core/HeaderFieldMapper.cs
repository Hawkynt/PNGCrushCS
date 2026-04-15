using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace FileFormat.Core;

/// <summary>Builds <see cref="HeaderFieldDescriptor"/> arrays from generated or attribute-based metadata with per-type caching.</summary>
public static class HeaderFieldMapper {

  private static readonly ConcurrentDictionary<Type, HeaderFieldDescriptor[]> _Cache = new();

  /// <summary>Returns the field map for the given struct type, using cached results on subsequent calls.</summary>
  public static HeaderFieldDescriptor[] GetFieldMap<T>() where T : struct
    => _Cache.GetOrAdd(typeof(T), static t => _BuildFieldMap(t));

  private static HeaderFieldDescriptor[] _BuildFieldMap(Type type) {
    var descriptors = new List<HeaderFieldDescriptor>();

    // Source-generated field map (zero reflection for inference-based headers)
    var genMethod = type.GetMethod("GetGeneratedFieldMap", BindingFlags.Public | BindingFlags.Static);
    if (genMethod != null && genMethod.Invoke(null, null) is HeaderFieldDescriptor[] generated && generated.Length > 0)
      descriptors.AddRange(generated);
    else {
      // Fallback: read [Field] attributes (explicit fixed-layout headers)
      foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
        var attr = prop.GetCustomAttribute<FieldAttribute>();
        if (attr == null) continue;
        descriptors.Add(new(attr.Name ?? prop.Name, attr.Offset, attr.Size));
      }
    }

    // Always include [Filler] entries. Anonymous fillers are reported as padding; named
    // ones are treated as composite fields and override any byte-level generated entries
    // that fall inside their span (the grouped name is more useful to consumers).
    var fillers = type.GetCustomAttributes<FillerAttribute>().ToList();
    foreach (var filler in fillers) {
      if (filler.Name != null) {
        descriptors.RemoveAll(d => d.Offset >= filler.Offset && d.Offset + d.Size <= filler.Offset + filler.Size);
        descriptors.Add(new(filler.Name, filler.Offset, filler.Size));
      } else {
        descriptors.Add(new("(padding)", filler.Offset, filler.Size));
      }
    }

    descriptors.Sort((a, b) => a.Offset.CompareTo(b.Offset));
    return descriptors.ToArray();
  }
}
