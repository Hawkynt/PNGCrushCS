using System;

namespace FileFormat.PrintfoxPagefox;

/// <summary>Assembles Printfox/Pagefox (.bs/.pg) file bytes from a PrintfoxPagefoxFile.</summary>
public static class PrintfoxPagefoxWriter {

  public static byte[] ToBytes(PrintfoxPagefoxFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[file.RawData.Length];
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result);

    return result;
  }
}
