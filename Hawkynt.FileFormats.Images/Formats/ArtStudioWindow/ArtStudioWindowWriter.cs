using System;

namespace FileFormat.ArtStudioWindow;

/// <summary>Assembles an Art Studio window from an <see cref="ArtStudioWindowFile"/>.</summary>
public static class ArtStudioWindowWriter {

  public static byte[] ToBytes(ArtStudioWindowFile file) {
    var data = file.Data ?? [];
    var result = new byte[data.Length];
    data.AsSpan().CopyTo(result);

    return result;
  }
}
