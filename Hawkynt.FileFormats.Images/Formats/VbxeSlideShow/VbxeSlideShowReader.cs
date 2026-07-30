using System;
using System.IO;

namespace FileFormat.VbxeSlideShow;

/// <summary>Reads SlideShow for VBXE pictures from bytes, streams, or file paths.</summary>
public static class VbxeSlideShowReader {

  public static VbxeSlideShowFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static VbxeSlideShowFile FromStream(Stream stream) {
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

  public static VbxeSlideShowFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != VbxeSlideShowFile.FileSize)
      throw new InvalidDataException(
        $"A SlideShow for VBXE picture is {VbxeSlideShowFile.FileSize} bytes, got {data.Length}.");

    var pixels = data[..VbxeSlideShowFile.PixelDataSize].ToArray();

    // Three planes, one per channel, each holding every colour's value for that channel.
    var palette = new byte[VbxeSlideShowFile.ColorCount * 3];
    for (var i = 0; i < VbxeSlideShowFile.ColorCount; ++i)
    for (var channel = 0; channel < 3; ++channel)
      palette[i * 3 + channel] = data[VbxeSlideShowFile.PaletteOffset + channel * VbxeSlideShowFile.ColorCount + i];

    return new() { PixelData = pixels, Palette = palette };
  }

  public static VbxeSlideShowFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
