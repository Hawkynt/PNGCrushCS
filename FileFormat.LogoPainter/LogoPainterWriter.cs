using System;

namespace FileFormat.LogoPainter;

/// <summary>Assembles Logo Painter 3 (.lp3) file bytes from a LogoPainterFile.</summary>
public static class LogoPainterWriter {

  public static byte[] ToBytes(LogoPainterFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[LogoPainterFile.LoadAddressSize + file.RawData.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result.AsSpan(LogoPainterFile.LoadAddressSize));

    return result;
  }
}
