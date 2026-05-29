using System;

namespace FileFormat.Degas;

/// <summary>Assembles DEGAS/DEGAS Elite file bytes from a DegasFile.</summary>
public static class DegasWriter {

  private const int _COMPRESSION_FLAG = unchecked((short)0x8000);

  public static byte[] ToBytes(DegasFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var resolutionValue = (short)file.Resolution;
    if (file.IsCompressed)
      resolutionValue = (short)(resolutionValue | _COMPRESSION_FLAG);

    var header = new DegasHeader(resolutionValue, file.Palette);

    byte[] imageData;
    if (file.IsCompressed)
      imageData = PackBitsCompressor.Compress(file.PixelData);
    else
      imageData = file.PixelData;

    var result = new byte[DegasHeader.StructSize + imageData.Length];
    header.WriteTo(result.AsSpan());
    imageData.AsSpan(0, imageData.Length).CopyTo(result.AsSpan(DegasHeader.StructSize));

    return result;
  }
}
