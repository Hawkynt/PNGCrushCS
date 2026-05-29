using System;

namespace FileFormat.Vips;

/// <summary>Assembles VIPS native image file bytes from pixel data.</summary>
public static class VipsWriter {

  /// <summary>VIPS image type: B_W (grayscale).</summary>
  private const int _TYPE_B_W = 1;

  /// <summary>VIPS image type: RGB.</summary>
  private const int _TYPE_RGB = 22;

  public static byte[] ToBytes(VipsFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return _Assemble(file.PixelData, file.Width, file.Height, file.Bands);
  }

  private static byte[] _Assemble(byte[] pixelData, int width, int height, int bands) {
    var expectedPixelBytes = width * height * bands;
    var result = new byte[VipsHeader.StructSize + expectedPixelBytes];

    var header = new VipsHeader(
      Magic: VipsHeader.MagicValue,
      Width: width,
      Height: height,
      Bands: bands,
      Unused1: 0,
      BandFormat: (int)VipsBandFormat.UChar,
      Coding: 0,
      Type: bands == 1 ? _TYPE_B_W : _TYPE_RGB,
      XRes: 1.0f,
      YRes: 1.0f,
      XOffset: 0,
      YOffset: 0,
      Length: bands,
      Compression: 0,
      Level: 0,
      BBits: 8,
      Unused2: 0
    );
    header.WriteTo(result);

    var copyLen = Math.Min(expectedPixelBytes, pixelData.Length);
    pixelData.AsSpan(0, copyLen).CopyTo(result.AsSpan(VipsHeader.StructSize));

    return result;
  }
}
