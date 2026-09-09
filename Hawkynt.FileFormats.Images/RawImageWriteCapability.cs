using System;
using System.IO;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Images;

/// <summary>
/// Runtime view of one compile-time typed raw-image writer contract. The source generator creates
/// these descriptors from closed <see cref="IImageFromRawImage{TSelf,TPixel}"/> implementations, so
/// capability discovery and dispatch require no runtime reflection.
/// </summary>
public sealed record RawImageWriteCapability(
  RawPixelFormatTraits PixelFormat,
  Func<RawImage, byte[]> Encode,
  Action<RawImage, FileInfo>? WriteToFile = null
) {
  /// <summary>
  /// Whether a legacy raw image can be viewed as this exact typed representation without copying or
  /// adapting content. Narrow logical indexed formats are value-validated here as well.
  /// </summary>
  public bool Accepts(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    try {
      RawPixelFormats.ValidateDeclaredRepresentation(image, this.PixelFormat);
      return true;
    } catch (ArgumentException) {
      return false;
    } catch (OverflowException) {
      return false;
    }
  }
}
