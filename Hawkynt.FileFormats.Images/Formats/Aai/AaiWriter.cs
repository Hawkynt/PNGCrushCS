using System;

namespace FileFormat.Aai;

/// <summary>Assembles AAI (Dune HD) file bytes from pixel data.</summary>
public static class AaiWriter {

  public static byte[] ToBytes(AaiFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var expectedPixelBytes = file.Width * file.Height * 4;
    var result = new byte[AaiHeader.StructSize + expectedPixelBytes];

    new AaiHeader((uint)file.Width, (uint)file.Height).WriteTo(result);
    file.PixelData.AsSpan(0, Math.Min(expectedPixelBytes, file.PixelData.Length)).CopyTo(result.AsSpan(AaiHeader.StructSize));

    return result;
  }
}
