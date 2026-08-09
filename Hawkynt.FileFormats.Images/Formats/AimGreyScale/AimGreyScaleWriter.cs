using System;

namespace FileFormat.AimGreyScale;

/// <summary>Writes an AIM grey scale picture, and the companion that says how big it is.</summary>
/// <remarks>
/// The picture file is the samples and nothing else, so the whole of the format's identity is in the
/// <c>.hd</c> beside it. What that file needs is small and was found by handing companions to the
/// loader until it stopped ignoring them: <c>AA</c> at offset four, the width and the height as
/// sixteen-bit big-endian numbers at 0x16 and 0x18, and twenty-six bytes for the header to reach
/// them. Everything else in it is read by nobody, here or there, and is written as zero.
/// <para/>
/// The two numbers must multiply out to the exact length of the picture or the companion is passed
/// over as belonging to some other file — which is the failure this writer is arranged to avoid, the
/// companion being built from the same file the bytes were built from rather than from the picture a
/// second time.
/// </remarks>
public static class AimGreyScaleWriter {

  /// <summary>Where the companion's two identifying characters stand.</summary>
  private const int _MARK_AT = 4;

  /// <summary>Where the size stands in the companion.</summary>
  private const int _WIDTH_AT = 0x16;

  private const int _HEIGHT_AT = 0x18;

  public static byte[] ToBytes(AimGreyScaleFile file) {
    var pixels = file.PixelData ?? [];
    var length = file.Width * file.Height;
    if (pixels.Length == length)
      return pixels[..];

    var result = new byte[length];
    pixels.AsSpan(0, Math.Min(pixels.Length, length)).CopyTo(result);

    return result;
  }

  /// <summary>Builds the companion stating the size, which is the only thing that states it.</summary>
  public static byte[] CompanionBytes(AimGreyScaleFile file) {
    var companion = new byte[AimGreyScaleFile.CompanionSize];
    companion[_MARK_AT] = (byte)AimGreyScaleFile.CompanionMark[0];
    companion[_MARK_AT + 1] = (byte)AimGreyScaleFile.CompanionMark[1];
    companion[_WIDTH_AT] = (byte)(file.Width >> 8);
    companion[_WIDTH_AT + 1] = (byte)file.Width;
    companion[_HEIGHT_AT] = (byte)(file.Height >> 8);
    companion[_HEIGHT_AT + 1] = (byte)file.Height;

    return companion;
  }
}
