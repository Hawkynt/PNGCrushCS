using System;

namespace FileFormat.CharSet64;

/// <summary>Assembles C64 character set file bytes from a CharSet64File.</summary>
public static class CharSet64Writer {

  public static byte[] ToBytes(CharSet64File file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[CharSet64File.ExpectedFileSize];
    var copyLen = Math.Min(file.CharData.Length, CharSet64File.ExpectedFileSize);
    file.CharData.AsSpan(0, copyLen).CopyTo(result.AsSpan(0));

    return result;
  }
}
