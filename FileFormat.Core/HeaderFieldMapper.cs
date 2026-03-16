using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace FileFormat.Core;

/// <summary>Builds <see cref="HeaderFieldDescriptor"/> arrays from <see cref="HeaderFieldAttribute"/> and <see cref="HeaderFillerAttribute"/> metadata with per-type caching.</summary>
public static class HeaderFieldMapper {

  private static readonly ConcurrentDictionary<Type, HeaderFieldDescriptor[]> _Cache = new();

  /// <summary>Returns the field map for the given struct type, using cached results on subsequent calls.</summary>
  public static HeaderFieldDescriptor[] GetFieldMap<T>() where T : struct
    => _Cache.GetOrAdd(typeof(T), static t => _BuildFieldMap(t));

  private static HeaderFieldDescriptor[] _BuildFieldMap(Type type) {
    var descriptors = new List<HeaderFieldDescriptor>();

    foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
      var attr = prop.GetCustomAttribute<HeaderFieldAttribute>();
      if (attr == null)
        continue;

      var name = attr.Name ?? prop.Name;
      descriptors.Add(new(name, attr.Offset, attr.Size));
    }

    foreach (var filler in type.GetCustomAttributes<HeaderFillerAttribute>())
      descriptors.Add(new(filler.Name, filler.Offset, filler.Size));

    descriptors.Sort((a, b) => a.Offset.CompareTo(b.Offset));
    return descriptors.ToArray();
  }
}
