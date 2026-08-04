using System;

namespace FileFormat.Blazing;

/// <summary>Assembles Blazing Paddles file bytes from a BlazingFile.</summary>
/// <remarks>
/// Two shapes are saved under these names and the reader already takes both, but this wrote only
/// the high-resolution one — so a multicolour picture read in came back out as a file of the wrong
/// length with its colour memory dropped. Both samples in the corpus are multicolour, and RECOIL
/// accepts nothing else at these extensions.
/// </remarks>
public static class BlazingWriter {

  public static byte[] ToBytes(BlazingFile file) {
    ArgumentNullException.ThrowIfNull(file.BitmapData);

    return file.ColorData != null ? _Multicolor(file) : _Hires(file);
  }

  /// <summary>
  /// The multicolour form, whose three sections each take a whole number of kilobytes.
  /// </summary>
  /// <remarks>
  /// The bitmap occupies 8192 of the 8000 it uses and the two colour sections a kilobyte each of
  /// their thousand; packing them tight instead puts the screen 194 bytes early, which is a picture
  /// of the right shape drawn in the wrong colours.
  /// </remarks>
  private static byte[] _Multicolor(BlazingFile file) {
    var result = new byte[BlazingFile.MulticolorFileSize];

    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);

    file.BitmapData.AsSpan(0, BlazingFile.BitmapDataSize).CopyTo(result.AsSpan(BlazingFile.LoadAddressSize));
    file.ScreenData.AsSpan(0, BlazingFile.ScreenDataSize).CopyTo(result.AsSpan(BlazingFile.MulticolorScreenOffset));
    file.ColorData.AsSpan(0, BlazingFile.ScreenDataSize).CopyTo(result.AsSpan(BlazingFile.MulticolorColorOffset));

    return result;
  }

  private static byte[] _Hires(BlazingFile file) {
    var result = new byte[BlazingFile.ExpectedFileSize];
    var offset = 0;

    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += BlazingFile.LoadAddressSize;

    file.BitmapData.AsSpan(0, BlazingFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += BlazingFile.BitmapDataSize;

    file.ScreenData.AsSpan(0, BlazingFile.ScreenDataSize).CopyTo(result.AsSpan(offset));
    // The remaining seven bytes are padding, which is already nought.

    return result;
  }
}
