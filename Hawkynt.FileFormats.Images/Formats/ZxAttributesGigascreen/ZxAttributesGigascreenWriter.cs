using System;

namespace FileFormat.ZxAttributesGigascreen;

/// <summary>Assembles ZX Spectrum Attributes Gigascreen (.hlr) file bytes.</summary>
public static class ZxAttributesGigascreenWriter {

  public static byte[] ToBytes(ZxAttributesGigascreenFile file) {
    var result = new byte[ZxAttributesGigascreenFile.FileSize];

    // The loader stub is what identifies the file; the rest of the header stays zero.
    ZxAttributesGigascreenFile.LoaderSignature.CopyTo(result);

    _Copy(file.Dither, result, ZxAttributesGigascreenFile.DitherOffset, 8);
    _Copy(file.FirstAttributes, result, ZxAttributesGigascreenFile.FirstAttributesOffset,
      ZxAttributesGigascreenFile.AttributesSize);
    _Copy(file.SecondAttributes, result, ZxAttributesGigascreenFile.SecondAttributesOffset,
      ZxAttributesGigascreenFile.AttributesSize);

    return result;
  }

  private static void _Copy(byte[]? source, byte[] destination, int offset, int length) {
    var data = source ?? [];
    data.AsSpan(0, Math.Min(data.Length, length)).CopyTo(destination.AsSpan(offset));
  }
}
