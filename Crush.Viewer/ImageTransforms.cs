using FileFormat.Core;

namespace Crush.Viewer;

/// <summary>Whole-picture colour operations the viewer offers that the transform library does not.</summary>
/// <remarks>
/// Both work on a BGRA32 copy rather than in place: <see cref="RawImage.ToBgra32"/> hands back the
/// source's own buffer when the picture already is BGRA32, and writing through that would edit the
/// picture the caller still holds — which is what an undo would have to give back.
/// </remarks>
internal static class ImageTransforms {

  /// <summary>Flattens a picture to greys, keeping whatever alpha it had.</summary>
  /// <remarks>
  /// BT.601 luma with the integer weights 77/150/29 over 256, which is the form every viewer and
  /// converter in this suite uses; the three add to exactly 256 so a white pixel stays white.
  /// </remarks>
  internal static RawImage Grayscale(RawImage source) {
    var data = (byte[])source.ToBgra32().Clone();
    for (var i = 0; i < data.Length; i += 4) {
      var luma = (byte)((data[i + 2] * 77 + data[i + 1] * 150 + data[i] * 29) >> 8);
      data[i] = data[i + 1] = data[i + 2] = luma;
    }

    return _Like(source, data);
  }

  /// <summary>Inverts the colour channels, leaving alpha alone.</summary>
  /// <remarks>
  /// Inverting alpha as well would turn an opaque picture transparent, which is not what anybody
  /// means by "invert" — the operation is about colour, and the mask stays as it was.
  /// </remarks>
  internal static RawImage Invert(RawImage source) {
    var data = (byte[])source.ToBgra32().Clone();
    for (var i = 0; i < data.Length; i += 4) {
      data[i] = (byte)(255 - data[i]);
      data[i + 1] = (byte)(255 - data[i + 1]);
      data[i + 2] = (byte)(255 - data[i + 2]);
    }

    return _Like(source, data);
  }

  private static RawImage _Like(RawImage source, byte[] bgra) => new() {
    Width = source.Width,
    Height = source.Height,
    Format = PixelFormat.Bgra32,
    PixelData = bgra,
    Metadata = source.Metadata,
    ColorInfo = source.ColorInfo,
  };
}
