using System;
using System.IO;
using FileFormat.Core;
using Hawkynt.ColorProcessing.Adapter;

namespace Optimizer.Image;

/// <summary>Reading a file into a picture, and reducing one to a palette.</summary>
/// <remarks>
/// This used to be the bridge to the platform's bitmap type as well. It is not any more: nothing in
/// this project needs one, and the half that did has moved to the viewer, which is the only thing
/// that has to put pixels on a screen.
/// </remarks>
internal static class BitmapConverter {

  /// <summary>Loads an image file as a <see cref="RawImage"/>. Null if the format has no reader.</summary>
  internal static RawImage? LoadRawImage(FileInfo file, ImageFormat format)
    => FormatRegistry.GetEntry(format)?.LoadRawImage(file);

  /// <summary>Loads an image from bytes as a <see cref="RawImage"/>. Null if the format has no reader.</summary>
  internal static RawImage? LoadRawImage(byte[] data, ImageFormat format)
    => FormatRegistry.GetEntry(format)?.LoadRawImageFromBytes(data);

  /// <summary>Reduces a picture to a palette, without a bitmap anywhere in the middle.</summary>
  /// <remarks>
  /// This used to go out to a bitmap and back purely to reach the colour library, whose only public
  /// entry point took one. It no longer does: the library's quantizers and ditherers are driven
  /// directly over the picture, so the round trip through a platform type — and the unpacking of
  /// one, two and four bit indexed rows that came with it — is gone.
  /// <para/>
  /// The names are still the old ones, which the dispatch translates.
  /// </remarks>
  internal static RawImage QuantizeRawImage(
    RawImage source,
    int maxColors,
    string quantizerName = "Median Cut",
    string dithererName = "ErrorDiffusion_FloydSteinberg",
    bool isHighQuality = false,
    System.Collections.Generic.Dictionary<string, object?>? quantizerParams = null,
    System.Collections.Generic.Dictionary<string, object?>? dithererParams = null
  ) {
    ArgumentNullException.ThrowIfNull(source);

    return ColorReductionDispatch.ReduceByName(source, quantizerName, dithererName, maxColors).Image;
  }
}
