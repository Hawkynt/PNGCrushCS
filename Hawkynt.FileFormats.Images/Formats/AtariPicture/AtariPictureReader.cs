using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AtariPicture;

/// <summary>Reads Atari Picture generic screen capture files from bytes, streams, or file paths.</summary>
public static class AtariPictureReader {

  public static AtariPictureFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Atari Picture file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariPictureFile FromStream(Stream stream) {
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

  public static AtariPictureFile FromSpan(ReadOnlySpan<byte> data) {
    // An .aps file is these same bytes under the SFDN packer.
    if (SfdnDecompressor.IsSfdn(data)) {
      var unpacked = SfdnDecompressor.TryUnpack(data, AtariPictureFile.PaddedFileSize)
        ?? throw new InvalidDataException("Not an APAC picture: the SFDN data does not unpack to a screen.");

      return FromSpan((ReadOnlySpan<byte>)unpacked);
    }

    // The padded size carries a short trailer the picture does not use.
    if (data.Length != AtariPictureFile.FileSize && data.Length != AtariPictureFile.PaddedFileSize)
      throw new InvalidDataException(
        $"An APAC picture is {AtariPictureFile.FileSize} or {AtariPictureFile.PaddedFileSize} bytes, got {data.Length}.");

    var pixelData = new byte[AtariPictureFile.FileSize];
    data[..AtariPictureFile.FileSize].CopyTo(pixelData);

    return new() { PixelData = pixelData };
  }

  public static AtariPictureFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
