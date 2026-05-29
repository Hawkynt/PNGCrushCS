using System;
using System.IO;

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

    if (data.Length != AtariPictureFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Atari Picture data size: expected exactly {AtariPictureFile.ExpectedFileSize} bytes, got {data.Length}.");

    var pixelData = new byte[AtariPictureFile.ExpectedFileSize];
    data.Slice(0, AtariPictureFile.ExpectedFileSize).CopyTo(pixelData);

    return new AtariPictureFile { PixelData = pixelData };
    }

  public static AtariPictureFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != AtariPictureFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Atari Picture data size: expected exactly {AtariPictureFile.ExpectedFileSize} bytes, got {data.Length}.");

    var pixelData = new byte[AtariPictureFile.ExpectedFileSize];
    data.AsSpan(0, AtariPictureFile.ExpectedFileSize).CopyTo(pixelData);

    return new AtariPictureFile { PixelData = pixelData };
  }
}
