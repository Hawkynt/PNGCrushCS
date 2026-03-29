using System;

namespace FileFormat.AdvancedArtStudio;

/// <summary>Assembles Advanced Art Studio (.ocp) file bytes from an AdvancedArtStudioFile.</summary>
public static class AdvancedArtStudioWriter {

  public static byte[] ToBytes(AdvancedArtStudioFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[AdvancedArtStudioFile.ExpectedFileSize];
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
    ++offset;

    result[offset] = file.BorderColor;

    return result;
  }
}
