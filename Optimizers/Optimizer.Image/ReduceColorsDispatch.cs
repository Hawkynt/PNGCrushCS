using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using Hawkynt.ColorProcessing.Dithering;
using Hawkynt.ColorProcessing.Quantization;
using Hawkynt.Drawing;

namespace Optimizer.Image;

/// <summary>Shared dispatch for <see cref="BitmapQuantizationExtensions.ReduceColors{TQ,TD}"/> using FrameworkExtensions registries.</summary>
public static class ReduceColorsDispatch {

  private static readonly ConcurrentDictionary<(Type, Type), Func<Bitmap, object, object, int, bool, Bitmap>> _cache = new();

  /// <summary>All available quantizer names from FrameworkExtensions registry.</summary>
  internal static IEnumerable<string> QuantizerNames => QuantizerRegistry.All.Select(q => q.Name);

  /// <summary>All available ditherer names from FrameworkExtensions registry.</summary>
  internal static IEnumerable<string> DithererNames => DithererRegistry.All.Select(d => d.Name);

  public static Bitmap ReduceColors(
    Bitmap source,
    string quantizerName,
    string dithererName,
    int colorCount,
    bool isHighQuality,
    Dictionary<string, object?>? quantizerParams = null,
    Dictionary<string, object?>? dithererParams = null
  ) {
    var qDescriptor = QuantizerRegistry.FindByName(quantizerName)
      ?? throw new ArgumentException($"Unknown quantizer: '{quantizerName}'. Available: {string.Join(", ", QuantizerNames)}");
    var dDescriptor = DithererRegistry.FindByName(dithererName)
      ?? throw new ArgumentException($"Unknown ditherer: '{dithererName}'. Available: {string.Join(", ", DithererNames)}");

    var qInstance = quantizerParams is { Count: > 0 }
      ? _CreateWithParams(qDescriptor.DeclaringType, quantizerParams) ?? qDescriptor.CreateDefault()
      : qDescriptor.CreateDefault();
    var dInstance = dithererParams is { Count: > 0 }
      ? _CreateWithParams(dDescriptor.DeclaringType, dithererParams) ?? dDescriptor.CreateDefault()
      : dDescriptor.CreateDefault();

    var key = (qDescriptor.DeclaringType, dDescriptor.DeclaringType);
    var invoker = _cache.GetOrAdd(key, static k => _BuildInvoker(k.Item1, k.Item2));
    return invoker(source, qInstance, dInstance, colorCount, isHighQuality);
  }

  // Score-and-rank constructor picker. Critical for overloaded types like CustomPaletteQuantizer where one
  // overload takes (byte,byte,byte)[] and another takes (byte,byte,byte,byte)[] — picking by param count alone
  // silently picked the wrong overload, nulled the palette arg, and produced an empty default palette.
  private static object? _CreateWithParams(Type type, Dictionary<string, object?> paramValues) {
    var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
    if (ctors.Length == 0) return null;

    var ranked = ctors
      .Select(c => new { Ctor = c, Score = _ScoreCtor(c, paramValues) })
      .Where(x => x.Score >= 0)
      .OrderByDescending(x => x.Score)
      .ThenByDescending(x => x.Ctor.GetParameters().Length)
      .ToArray();
    if (ranked.Length == 0) return null;

    foreach (var entry in ranked) {
      var parameters = entry.Ctor.GetParameters();
      var args = new object?[parameters.Length];
      var ok = true;
      for (var i = 0; i < parameters.Length; ++i) {
        var p = parameters[i];
        if (p.Name != null && paramValues.TryGetValue(p.Name, out var value) && value != null) {
          try { args[i] = _ConvertValue(value, p.ParameterType); }
          catch { ok = false; break; }
        } else {
          args[i] = p.HasDefaultValue ? p.DefaultValue : (p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null);
        }
      }
      if (!ok) continue;
      try { return entry.Ctor.Invoke(args); } catch { /* try next */ }
    }
    return null;
  }

  private static int _ScoreCtor(ConstructorInfo ctor, Dictionary<string, object?> paramValues) {
    var parameters = ctor.GetParameters();
    if (parameters.Length == 0) return 0;
    var score = 0;
    foreach (var p in parameters) {
      if (p.Name != null && paramValues.TryGetValue(p.Name, out var value) && value != null) {
        if (p.ParameterType.IsInstanceOfType(value)) {
          score += 2;
        } else if (p.ParameterType.IsEnum && value is string) {
          score += 1;
        } else if (value is IConvertible && (p.ParameterType.IsPrimitive || p.ParameterType == typeof(string) || p.ParameterType == typeof(decimal))) {
          score += 1;
        } else {
          return -1;
        }
      }
    }
    return score;
  }

  private static object? _ConvertValue(object value, Type targetType) {
    if (targetType.IsInstanceOfType(value)) return value;
    var underlying = Nullable.GetUnderlyingType(targetType);
    if (underlying != null) return value == null! ? null : Convert.ChangeType(value, underlying);
    if (targetType.IsEnum && value is string s) return Enum.Parse(targetType, s);
    return Convert.ChangeType(value, targetType);
  }

  private static Func<Bitmap, object, object, int, bool, Bitmap> _BuildInvoker(Type quantizerType, Type dithererType) {
    var method = typeof(BitmapQuantizationExtensions)
      .GetMethods(BindingFlags.Public | BindingFlags.Static)
      .First(m =>
        m.Name == nameof(BitmapQuantizationExtensions.ReduceColors)
        && m.IsGenericMethod
        && m.GetGenericArguments().Length == 2
        && m.GetParameters().Length == 5
        && m.GetParameters()[1].ParameterType.IsGenericParameter
      );
    var generic = method.MakeGenericMethod(quantizerType, dithererType);
    return (bmp, q, d, colors, hq) => (Bitmap)generic.Invoke(null, [bmp, q, d, colors, hq])!;
  }
}
