using System;
using System.IO;

namespace FileFormat.Core;

/// <summary>
/// Declares one exact raw representation a format can serialize without quantization, dithering,
/// resampling, or other content adaptation. A format may implement this interface multiple times for
/// different <typeparamref name="TPixel"/> descriptors.
/// </summary>
/// <typeparam name="TSelf">In-memory file representation.</typeparam>
/// <typeparam name="TPixel">Exact raw representation accepted by this writer path.</typeparam>
public interface IImageFromRawImage<TSelf, TPixel>
  where TSelf : IImageFromRawImage<TSelf, TPixel>
  where TPixel : IRawPixelFormat<TPixel> {

  /// <summary>Creates the in-memory file representation from an already-supported raw representation.</summary>
  static abstract TSelf FromRawImage(RawImage<TPixel> image);

  /// <summary>Creates the representation for a target extension without changing pixel semantics.</summary>
  static virtual TSelf FromRawImage(RawImage<TPixel> image, string extension) => TSelf.FromRawImage(image);

  /// <summary>Creates the representation for a named target without changing pixel semantics.</summary>
  static virtual TSelf FromRawImage(RawImage<TPixel> image, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    return TSelf.FromRawImage(image, target.Extension);
  }
}
