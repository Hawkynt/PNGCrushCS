using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using Hawkynt.ColorProcessing.Dithering;
using Hawkynt.ColorProcessing.Quantization;
using Hawkynt.Drawing;

namespace Hawkynt.ImageTransformUI;

/// <summary>Dispatch for <see cref="BitmapQuantizationExtensions.ReduceColors{TQ,TD}"/> using FrameworkExtensions registries.
/// Self-contained — no dependency on Optimizer.Image. Caches generic method delegates per quantizer+ditherer pair.</summary>
public static class ReduceColorsDispatch {

  private static readonly ConcurrentDictionary<(Type, Type), Func<Bitmap, object, object, int, bool, Bitmap>> _cache = new();

  /// <summary>All available quantizer names from FrameworkExtensions registry.</summary>
  public static IEnumerable<string> QuantizerNames => QuantizerRegistry.All.Select(q => q.Name);

  /// <summary>All available ditherer names from FrameworkExtensions registry.</summary>
  public static IEnumerable<string> DithererNames => DithererRegistry.All.Select(d => d.Name);

  /// <summary>Reduce colors of a bitmap using the named quantizer and ditherer from the FrameworkExtensions registry.</summary>
  public static Bitmap ReduceColors(
    Bitmap source,
    string quantizerName,
    string dithererName,
    int colorCount,
    bool isHighQuality = true,
    Dictionary<string, object?>? quantizerParams = null,
    Dictionary<string, object?>? dithererParams = null
  ) {
    ArgumentNullException.ThrowIfNull(source);

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

  /// <summary>Attempts to create an instance of the given type using constructor parameters from the dictionary.</summary>
  private static object? _CreateWithParams(Type type, Dictionary<string, object?> paramValues) {
    var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
    if (ctors.Length == 0) return null;

    // Find best matching constructor (most parameters matched)
    var bestCtor = ctors.OrderByDescending(c => c.GetParameters().Length).First();
    var parameters = bestCtor.GetParameters();
    if (parameters.Length == 0) return null;

    var args = new object?[parameters.Length];
    for (var i = 0; i < parameters.Length; ++i) {
      var p = parameters[i];
      if (p.Name != null && paramValues.TryGetValue(p.Name, out var value) && value != null) {
        try {
          args[i] = _ConvertValue(value, p.ParameterType);
        } catch {
          args[i] = p.HasDefaultValue ? p.DefaultValue : (p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null);
        }
      } else {
        args[i] = p.HasDefaultValue ? p.DefaultValue : (p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null);
      }
    }

    try {
      return bestCtor.Invoke(args);
    } catch {
      return null;
    }
  }

  private static object? _ConvertValue(object value, Type targetType) {
    // Pass-through when the value already matches (handles arrays of complex types like (byte,byte,byte)[]
    // that Convert.ChangeType can't deal with — needed e.g. for CustomPaletteQuantizer's palette param).
    if (targetType.IsInstanceOfType(value)) return value;

    var underlying = Nullable.GetUnderlyingType(targetType);
    if (underlying != null) {
      return value == null! ? null : Convert.ChangeType(value, underlying);
    }
    if (targetType.IsEnum && value is string s)
      return Enum.Parse(targetType, s);
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
