using System;
using System.IO;

namespace FileFormat.AimGreyScale;

/// <summary>Reads an AIM grey scale image, taking its size from the companion beside it.</summary>
public static class AimGreyScaleReader {

  /// <summary>Where the companion's two identifying characters stand.</summary>
  private const int _MARK_AT = 4;

  /// <summary>Where the size stands in the companion, big-endian and sixteen bits each.</summary>
  private const int _WIDTH_AT = 0x16;

  private const int _HEIGHT_AT = 0x18;

  /// <summary>
  /// Reads a picture and the companion stating its size.
  /// </summary>
  /// <remarks>
  /// The companion's name is the picture's with everything from the last dot replaced by
  /// <c>.hd</c> — so <c>scan.ima</c> is described by <c>scan.hd</c>, and a name with two dots in it
  /// loses only the last part. A picture whose companion is missing, malformed, or describes some
  /// other number of pixels falls back on its length exactly as if there were no companion at all.
  /// </remarks>
  public static AimGreyScaleFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    var pixels = File.ReadAllBytes(file.FullName);
    var companion = new FileInfo(Path.ChangeExtension(file.FullName, AimGreyScaleFile.CompanionExtension));
    if (!companion.Exists)
      return FromSpan(pixels);

    byte[] stated;
    try {
      stated = File.ReadAllBytes(companion.FullName);
    } catch (IOException) {
      return FromSpan(pixels);
    }

    return _TrySize(stated, pixels.Length, out var width, out var height)
      ? new() { Width = width, Height = height, PixelData = pixels }
      : FromSpan(pixels);
  }

  public static AimGreyScaleFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  public static AimGreyScaleFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>
  /// Reads a picture with no companion to hand, which leaves exactly one length readable.
  /// </summary>
  /// <remarks>
  /// Bytes alone cannot reach a file beside the file, so this is what the format comes to without a
  /// path: the one size the loader will assume, and a refusal for every other length. Nothing here
  /// guesses a shape from a factorisation — a headerless picture with no companion is 256 by 256 or
  /// it is nothing.
  /// </remarks>
  public static AimGreyScaleFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != AimGreyScaleFile.FallbackLength)
      throw new InvalidDataException(
        $"An AIM picture takes its size from the .hd beside it; without one only {AimGreyScaleFile.FallbackLength} bytes is readable, and this is {data.Length}.");

    return new() {
      Width = AimGreyScaleFile.FallbackExtent,
      Height = AimGreyScaleFile.FallbackExtent,
      PixelData = data.ToArray(),
    };
  }

  /// <summary>Reads a picture whose companion has already been fetched by some other means.</summary>
  public static AimGreyScaleFile FromBytesAndCompanion(byte[] data, byte[]? companion) {
    ArgumentNullException.ThrowIfNull(data);

    return companion != null && _TrySize(companion, data.Length, out var width, out var height)
      ? new() { Width = width, Height = height, PixelData = data }
      : FromSpan(data);
  }

  /// <summary>
  /// Reads the size out of a companion, and says whether it describes this picture.
  /// </summary>
  /// <remarks>
  /// Everything but the mark and the two numbers is read and thrown away, so only the first
  /// twenty-six bytes have to be there. The last test is the one that matters: the two numbers
  /// multiplied have to come to the length of the picture exactly. A companion that says something
  /// else is not this picture's, and is ignored rather than believed.
  /// </remarks>
  private static bool _TrySize(ReadOnlySpan<byte> companion, int length, out int width, out int height) {
    width = 0;
    height = 0;
    if (companion.Length < AimGreyScaleFile.CompanionSize)
      return false;
    if (companion[_MARK_AT] != AimGreyScaleFile.CompanionMark[0] || companion[_MARK_AT + 1] != AimGreyScaleFile.CompanionMark[1])
      return false;

    width = (companion[_WIDTH_AT] << 8) | companion[_WIDTH_AT + 1];
    height = (companion[_HEIGHT_AT] << 8) | companion[_HEIGHT_AT + 1];

    return width > 0 && height > 0 && width * height == length;
  }
}
