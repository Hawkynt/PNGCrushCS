using System;

namespace FileFormat.Avs;

/// <summary>Assembles AVS file bytes from pixel data.</summary>
public static class AvsWriter {

  public static byte[] ToBytes(AvsFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var expectedPixelBytes = width * height * 4;
    var result = new byte[AvsHeader.StructSize + expectedPixelBytes];

    new AvsHeader((uint)width, (uint)height).WriteTo(result);

    pixelData.AsSpan(0, Math.Min(expectedPixelBytes, pixelData.Length)).CopyTo(result.AsSpan(AvsHeader.StructSize));

    return result;
  }
}
