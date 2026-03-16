using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using Hawkynt.ColorProcessing.Dithering;
using Hawkynt.ColorProcessing.Quantization;
using Hawkynt.Drawing;

namespace Optimizer.Png;

/// <summary>Shared dispatch for <see cref="BitmapQuantizationExtensions.ReduceColors{TQ,TD}"/> using FrameworkExtensions registries.</summary>
internal static class ReduceColorsDispatch {

  private static readonly ConcurrentDictionary<(Type, Type), Func<Bitmap, object, object, int, bool, Bitmap>> _cache = new();

  /// <summary>All available quantizer names from FrameworkExtensions registry.</summary>
  internal static IEnumerable<string> QuantizerNames => QuantizerRegistry.All.Select(q => q.Name);

  /// <summary>All available ditherer names from FrameworkExtensions registry.</summary>
  internal static IEnumerable<string> DithererNames => DithererRegistry.All.Select(d => d.Name);

  internal static Bitmap ReduceColors(Bitmap source, string quantizerName, string dithererName, int colorCount, bool isHighQuality) {
    var qDescriptor = QuantizerRegistry.FindByName(quantizerName)
      ?? throw new ArgumentException($"Unknown quantizer: '{quantizerName}'. Available: {string.Join(", ", QuantizerNames)}");
    var dDescriptor = DithererRegistry.FindByName(dithererName)
      ?? throw new ArgumentException($"Unknown ditherer: '{dithererName}'. Available: {string.Join(", ", DithererNames)}");

    var qInstance = qDescriptor.CreateDefault();
    var dInstance = dDescriptor.CreateDefault();
    var key = (qDescriptor.DeclaringType, dDescriptor.DeclaringType);
    var invoker = _cache.GetOrAdd(key, static k => _BuildInvoker(k.Item1, k.Item2));
    return invoker(source, qInstance, dInstance, colorCount, isHighQuality);
  }

  private static Func<Bitmap, object, object, int, bool, Bitmap> _BuildInvoker(Type quantizerType, Type dithererType) {
    var method = typeof(BitmapQuantizationExtensions)
      .GetMethods(BindingFlags.Public | BindingFlags.Static)
      .First(m =>
        m.Name == "ReduceColors"
        && m.IsGenericMethod
        && m.GetGenericArguments().Length == 2
        && m.GetParameters().Length == 5
        && m.GetParameters()[1].ParameterType.IsGenericParameter
      );
    var generic = method.MakeGenericMethod(quantizerType, dithererType);
    return (bmp, q, d, colors, hq) => (Bitmap)generic.Invoke(null, [bmp, q, d, colors, hq])!;
  }
}
