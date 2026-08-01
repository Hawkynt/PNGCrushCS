using System;
using System.IO;

namespace FileFormat.AtariFalconXga;

/// <summary>Reads Atari Falcon XGA 16-bit true color files from bytes, streams, or file paths.</summary>
public static class AtariFalconXgaReader {

  public static AtariFalconXgaFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Atari Falcon XGA file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariFalconXgaFile FromStream(Stream stream) {
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

  public static AtariFalconXgaFile FromSpan(ReadOnlySpan<byte> data) {
    var (width, height) = AtariFalconXgaFile.SizeOf(data.Length);
    var pixelData = data[..(width * height * 2)].ToArray();

    return new() {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }

  public static AtariFalconXgaFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
