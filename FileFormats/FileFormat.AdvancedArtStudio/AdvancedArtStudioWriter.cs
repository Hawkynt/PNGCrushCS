using System;

namespace FileFormat.AdvancedArtStudio;

/// <summary>Assembles Advanced Art Studio (.ocp) file bytes from an AdvancedArtStudioFile.</summary>
public static class AdvancedArtStudioWriter {

  public static byte[] ToBytes(AdvancedArtStudioFile file) =>
    file.IsHiRes ? _WriteHiRes(file) : _WriteMulticolor(file);

  private static byte[] _WriteMulticolor(AdvancedArtStudioFile file) {
    var result = new byte[AdvancedArtStudioFile.MulticolorFileSize];
    var offset = 0;
    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += AdvancedArtStudioFile.LoadAddressSize;

    file.BitmapData.AsSpan(0, AdvancedArtStudioFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += AdvancedArtStudioFile.BitmapDataSize;

    file.ScreenRam.AsSpan(0, AdvancedArtStudioFile.ScreenRamSize).CopyTo(result.AsSpan(offset));
    offset += AdvancedArtStudioFile.ScreenRamSize;

    file.ColorRam.AsSpan(0, AdvancedArtStudioFile.ColorRamSize).CopyTo(result.AsSpan(offset));
    offset += AdvancedArtStudioFile.ColorRamSize;

    result[offset] = file.BackgroundColor;
    result[offset + 1] = file.BorderColor;
    return result;
  }

  private static byte[] _WriteHiRes(AdvancedArtStudioFile file) {
    var result = new byte[AdvancedArtStudioFile.HiResFileSize];
    var offset = 0;
    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += AdvancedArtStudioFile.LoadAddressSize;

    file.BitmapData.AsSpan(0, AdvancedArtStudioFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += AdvancedArtStudioFile.BitmapDataSize;

    file.ScreenRam.AsSpan(0, AdvancedArtStudioFile.ScreenRamSize).CopyTo(result.AsSpan(offset));
    offset += AdvancedArtStudioFile.ScreenRamSize;

    // 7-byte trailing region: store the border colour in the last byte.
    result[^1] = file.BorderColor;
    return result;
  }
}
