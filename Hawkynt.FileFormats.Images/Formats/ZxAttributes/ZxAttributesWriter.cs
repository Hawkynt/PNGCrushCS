using System;

namespace FileFormat.ZxAttributes;

/// <summary>Assembles ZX Spectrum attribute-only (.atr) file bytes.</summary>
public static class ZxAttributesWriter {

  public static byte[] ToBytes(ZxAttributesFile file) {
    var result = new byte[ZxAttributesFile.FileSize];

    var attributes = file.AttributeData ?? [];
    attributes.AsSpan(0, Math.Min(attributes.Length, ZxAttributesFile.FileSize)).CopyTo(result);

    return result;
  }
}
