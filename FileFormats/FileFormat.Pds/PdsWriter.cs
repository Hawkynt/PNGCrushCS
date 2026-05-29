using System;
using System.Collections.Generic;

namespace FileFormat.Pds;

/// <summary>Assembles PDS (NASA Planetary Data System) file bytes from pixel data.</summary>
public static class PdsWriter {

  public static byte[] ToBytes(PdsFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(
      file.PixelData,
      file.Width,
      file.Height,
      file.SampleBits,
      file.Bands,
      file.BandStorage,
      file.SampleType,
      file.Labels
    );
  }

  internal static byte[] Assemble(
    byte[] pixelData,
    int width,
    int height,
    int sampleBits,
    int bands,
    PdsBandStorage bandStorage,
    PdsSampleType sampleType,
    Dictionary<string, string>? extraLabels
  ) {
    var bytesPerSample = sampleBits / 8;
    var expectedPixelBytes = width * height * bands * bytesPerSample;

    var headerBytes = PdsHeaderParser.Format(
      width,
      height,
      sampleBits,
      bands,
      bandStorage,
      sampleType,
      expectedPixelBytes,
      extraLabels
    );

    var result = new byte[headerBytes.Length + expectedPixelBytes];
    headerBytes.AsSpan(0, headerBytes.Length).CopyTo(result.AsSpan(0));

    var copyLen = Math.Min(expectedPixelBytes, pixelData.Length);
    if (copyLen > 0)
      pixelData.AsSpan(0, copyLen).CopyTo(result.AsSpan(headerBytes.Length));

    return result;
  }
}
