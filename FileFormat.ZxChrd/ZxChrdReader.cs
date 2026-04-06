using System;
using System.IO;

namespace FileFormat.ZxChrd;

/// <summary>Reads ZX Spectrum character set (.chr) files from bytes, streams, or file paths.</summary>
public static class ZxChrdReader {

  /// <summary>Total file size: 256 characters x 8 bytes each.</summary>
  internal const int FileSize = 2048;

  public static ZxChrdFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ZX Spectrum character set file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ZxChrdFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static ZxChrdFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static ZxChrdFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != FileSize)
      throw new InvalidDataException($"ZX Spectrum character set file must be exactly {FileSize} bytes, got {data.Length}.");

    var charData = new byte[FileSize];
    data.AsSpan(0, FileSize).CopyTo(charData);

    return new ZxChrdFile {
      CharacterData = charData,
    };
  }
}
