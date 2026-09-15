using System;
using System.IO;

namespace FileFormat.MsxView;

/// <summary>Reads MSX View image files from bytes, streams, or file paths.</summary>
public static class MsxViewReader {

  public static MsxViewFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MSX View file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MsxViewFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static MsxViewFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != MsxViewFile.ExpectedFileSize)
      throw new InvalidDataException($"MSX View file must be exactly {MsxViewFile.ExpectedFileSize} bytes, got {data.Length}.");

    var pixels = new byte[MsxViewFile.ExpectedFileSize];
    data.Slice(0, MsxViewFile.ExpectedFileSize).CopyTo(pixels);

    return new() { PixelData = pixels };
    }

  public static MsxViewFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != MsxViewFile.ExpectedFileSize)
      throw new InvalidDataException($"MSX View file must be exactly {MsxViewFile.ExpectedFileSize} bytes, got {data.Length}.");

    var pixels = new byte[MsxViewFile.ExpectedFileSize];
    data.AsSpan(0, MsxViewFile.ExpectedFileSize).CopyTo(pixels);

    return new() { PixelData = pixels };
  }
}
