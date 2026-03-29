using System;

namespace FileFormat.XFliEditor;

/// <summary>Assembles X-FLI Editor (.xfl) file bytes from an XFliEditorFile.</summary>
public static class XFliEditorWriter {

  public static byte[] ToBytes(XFliEditorFile file) {
    ArgumentNullException.ThrowIfNull(file);

    // LoadAddress(2) + Bitmap(8000) + 8*Screen(8000) + Color(1000) + BackgroundColor(1) + TrailingData
    var totalSize = XFliEditorFile.LoadAddressSize + XFliEditorFile.MinPayloadSize + 1 + file.TrailingData.Length;
    var result = new byte[totalSize];
    var offset = 0;

    // Load address (2 bytes, little-endian)
    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += XFliEditorFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    file.BitmapData.AsSpan(0, XFliEditorFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += XFliEditorFile.BitmapDataSize;

    // 8 screen banks (8 x 1000 bytes)
    for (var i = 0; i < XFliEditorFile.ScreenBankCount; ++i) {
      file.ScreenBanks[i].AsSpan(0, XFliEditorFile.ScreenBankSize).CopyTo(result.AsSpan(offset));
      offset += XFliEditorFile.ScreenBankSize;
    }

    // Color RAM (1000 bytes)
    file.ColorData.AsSpan(0, XFliEditorFile.ColorDataSize).CopyTo(result.AsSpan(offset));
    offset += XFliEditorFile.ColorDataSize;

    // Background color (1 byte)
    result[offset] = file.BackgroundColor;
    ++offset;

    // Trailing data
    if (file.TrailingData.Length > 0)
      file.TrailingData.AsSpan().CopyTo(result.AsSpan(offset));

    return result;
  }
}
