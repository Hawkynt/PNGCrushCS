using System;

namespace FileFormat.Pagefox;

/// <summary>Assembles Pagefox hires file bytes from a PagefoxFile.</summary>
public static class PagefoxWriter {

  public static byte[] ToBytes(PagefoxFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[PagefoxFile.ExpectedFileSize];
    var copyLen = Math.Min(file.RawData.Length, PagefoxFile.ExpectedFileSize);
    file.RawData.AsSpan(0, copyLen).CopyTo(result.AsSpan(0));

    return result;
  }
}
