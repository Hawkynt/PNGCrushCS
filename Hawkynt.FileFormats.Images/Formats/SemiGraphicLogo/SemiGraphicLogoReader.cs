using System;
using System.IO;

namespace FileFormat.SemiGraphicLogo;

/// <summary>Reads Semi-Graphic logos Editor screens from bytes, streams, or file paths.</summary>
public static class SemiGraphicLogoReader {

  public static SemiGraphicLogoFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Screen not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SemiGraphicLogoFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static SemiGraphicLogoFile FromSpan(ReadOnlySpan<byte> data) {
    // Nothing but character codes, so the length is the whole of the identification.
    if (data.Length != SemiGraphicLogoFile.FileSize)
      throw new InvalidDataException($"Not a Semi-Graphic logos screen: {data.Length} bytes.");

    return new() { Characters = data.ToArray() };
  }

  public static SemiGraphicLogoFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
