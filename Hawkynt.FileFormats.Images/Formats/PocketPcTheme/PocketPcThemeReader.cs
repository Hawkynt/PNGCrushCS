using System;
using System.IO;
using FileFormat.Core;
using FileFormat.EmbeddedPicture;

namespace FileFormat.PocketPcTheme;

/// <summary>Scans a Pocket PC theme cabinet for a picture stored in it whole.</summary>
public static class PocketPcThemeReader {

  public static PocketPcThemeFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Pocket PC theme not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PocketPcThemeFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }

    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromSpan(memory.ToArray());
  }

  public static PocketPcThemeFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static PocketPcThemeFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < PocketPcThemeFile.Signature.Length)
      throw new InvalidDataException("Data too small to be a Pocket PC theme.");

    if (!data[..PocketPcThemeFile.Signature.Length].SequenceEqual(PocketPcThemeFile.Signature))
      throw new InvalidDataException("Not a Pocket PC theme: it is not a Microsoft cabinet.");

    var found = _FindFirstPicture(data);
    if (found < 0)
      throw new InvalidDataException("A Pocket PC theme stores no GIF, PNG or JFIF this can reach without unpacking the cabinet.");

    var decoded = PixelConverter.Convert(EmbeddedPictureReader.Decode(data[found..]), PixelFormat.Rgb24);

    return new() { Width = decoded.Width, Height = decoded.Height, PixelData = decoded.PixelData };
  }

  /// <summary>Where the first picture stored whole begins, or -1 when there is none.</summary>
  private static int _FindFirstPicture(ReadOnlySpan<byte> data) {
    for (var at = PocketPcThemeFile.ScanStart; at + 4 <= data.Length; ++at) {
      var window = data.Slice(at, 4);
      if (window.SequenceEqual(PocketPcThemeFile.GifSignature)
          || window.SequenceEqual(PocketPcThemeFile.PngSignature)
          || window.SequenceEqual(PocketPcThemeFile.JfifSignature))
        return at;
    }

    return -1;
  }
}
