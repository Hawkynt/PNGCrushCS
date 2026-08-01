using System;
using System.Text;

namespace FileFormat.AtariGraphicsStudio;

/// <summary>Assembles an Atari Graphics Studio picture from an <see cref="AtariGraphicsStudioFile"/>.</summary>
public static class AtariGraphicsStudioWriter {

  public static byte[] ToBytes(AtariGraphicsStudioFile file) {
    var data = file.Data ?? [];
    var result = new byte[data.Length];
    data.AsSpan().CopyTo(result);

    if (result.Length >= AtariGraphicsStudioFile.Signature.Length)
      Encoding.ASCII.GetBytes(AtariGraphicsStudioFile.Signature).CopyTo(result, 0);

    return result;
  }
}
