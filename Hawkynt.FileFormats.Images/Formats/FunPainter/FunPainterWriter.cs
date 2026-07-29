using System;

namespace FileFormat.FunPainter;

/// <summary>Assembles Fun Painter II (.fp2/.fun) file bytes from a FunPainterFile.</summary>
public static class FunPainterWriter {

  public static byte[] ToBytes(FunPainterFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[FunPainterFile.LoadAddressSize + file.RawData.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result.AsSpan(FunPainterFile.LoadAddressSize));

    return result;
  }
}
