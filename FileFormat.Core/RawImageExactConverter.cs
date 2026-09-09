using System;

namespace FileFormat.Core;

/// <summary>
/// Exact raw-representation conversions. This layer is deliberately incapable of quantization,
/// dithering, nearest-colour mapping, resampling, or other policy-bearing transformations.
/// </summary>
public static class RawImageExactConverter {

  /// <summary>
  /// Tries to convert between indexed raw representations while preserving every index, palette entry,
  /// palette order, alpha entry, colour interpretation, and metadata item. A conversion that would need
  /// to drop palette entries or remap an out-of-range index fails instead of approximating the image.
  /// </summary>
  public static bool TryConvertIndexed<TSource, TTarget>(
    RawImage<TSource> source,
    out RawImage<TTarget>? result
  )
    where TSource : IRawPixelFormat<TSource>
    where TTarget : IRawPixelFormat<TTarget> {
    ArgumentNullException.ThrowIfNull(source);

    var sourceTraits = TSource.Traits;
    var targetTraits = TTarget.Traits;
    if (!sourceTraits.IsIndexed || !targetTraits.IsIndexed) {
      result = null;
      return false;
    }

    source.Validate();

    if (source.PaletteCount > targetTraits.MaximumPaletteEntries) {
      result = null;
      return false;
    }

    // Logical widths such as Indexed6 and Indexed8 intentionally share byte-per-index compatibility
    // storage. Narrowing or widening inside that family is therefore a validated zero-copy type change.
    if (sourceTraits.LegacyFormat == targetTraits.LegacyFormat)
      return RawImage<TTarget>.TryFromUntyped(source.Untyped, out result);

    var pixelCount = checked(source.Width * source.Height);
    var targetData = _AllocateIndexedStorage(targetTraits.LegacyFormat, pixelCount);
    var maximumTargetIndex = targetTraits.MaximumPaletteEntries - 1;

    for (var i = 0; i < pixelCount; ++i) {
      var index = _ReadIndex(source.PixelData, sourceTraits.LegacyFormat, i);
      if (index > maximumTargetIndex) {
        result = null;
        return false;
      }

      _WriteIndex(targetData, targetTraits.LegacyFormat, i, index);
    }

    result = new(
      source.Width,
      source.Height,
      targetData,
      source.ColorInfo,
      source.Palette,
      source.PaletteCount,
      source.AlphaTable,
      source.Metadata
    );
    return true;
  }

  private static byte[] _AllocateIndexedStorage(PixelFormat format, int pixelCount) => format switch {
    PixelFormat.Indexed1 => new byte[checked((pixelCount + 7) / 8)],
    PixelFormat.Indexed4 => new byte[checked((pixelCount + 1) / 2)],
    PixelFormat.Indexed8 => new byte[pixelCount],
    PixelFormat.Indexed16 => new byte[checked(pixelCount * 2)],
    _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not an indexed compatibility storage format."),
  };

  private static int _ReadIndex(byte[] data, PixelFormat format, int pixel) => format switch {
    PixelFormat.Indexed1 => data[pixel >> 3] >> (7 - (pixel & 7)) & 1,
    PixelFormat.Indexed4 => data[pixel >> 1] >> ((pixel & 1) == 0 ? 4 : 0) & 0x0F,
    PixelFormat.Indexed8 => data[pixel],
    PixelFormat.Indexed16 => data[pixel * 2] | data[pixel * 2 + 1] << 8,
    _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not an indexed compatibility storage format."),
  };

  private static void _WriteIndex(byte[] data, PixelFormat format, int pixel, int index) {
    switch (format) {
      case PixelFormat.Indexed1:
        if (index != 0)
          data[pixel >> 3] |= (byte)(0x80 >> (pixel & 7));
        return;

      case PixelFormat.Indexed4:
        if ((pixel & 1) == 0)
          data[pixel >> 1] |= (byte)(index << 4);
        else
          data[pixel >> 1] |= (byte)index;
        return;

      case PixelFormat.Indexed8:
        data[pixel] = (byte)index;
        return;

      case PixelFormat.Indexed16:
        data[pixel * 2] = (byte)index;
        data[pixel * 2 + 1] = (byte)(index >> 8);
        return;

      default:
        throw new ArgumentOutOfRangeException(nameof(format), format, "Not an indexed compatibility storage format.");
    }
  }
}
