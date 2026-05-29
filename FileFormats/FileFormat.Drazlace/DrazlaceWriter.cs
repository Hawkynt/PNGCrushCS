using System;

namespace FileFormat.Drazlace;

/// <summary>Assembles Drazlace (.dlp/.drl) file bytes from a DrazlaceFile.</summary>
public static class DrazlaceWriter {

  public static byte[] ToBytes(DrazlaceFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var payload = new byte[DrazlaceFile.UncompressedPayloadSize];
    var offset = 0;

    file.BitmapData1.AsSpan(0, DrazlaceFile.BitmapDataSize).CopyTo(payload.AsSpan(offset));
    offset += DrazlaceFile.BitmapDataSize;

    file.ScreenRam1.AsSpan(0, DrazlaceFile.ScreenRamSize).CopyTo(payload.AsSpan(offset));
    offset += DrazlaceFile.ScreenRamSize;

    file.ColorRam.AsSpan(0, DrazlaceFile.ColorRamSize).CopyTo(payload.AsSpan(offset));
    offset += DrazlaceFile.ColorRamSize;

    payload[offset] = file.BackgroundColor;
    offset += 1;

    file.BitmapData2.AsSpan(0, DrazlaceFile.BitmapDataSize).CopyTo(payload.AsSpan(offset));
    offset += DrazlaceFile.BitmapDataSize;

    file.ScreenRam2.AsSpan(0, DrazlaceFile.ScreenRamSize).CopyTo(payload.AsSpan(offset));

    var compressed = DrazlaceFile.RleEncode(payload);

    var result = new byte[DrazlaceFile.LoadAddressSize + compressed.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    compressed.AsSpan(0, compressed.Length).CopyTo(result.AsSpan(DrazlaceFile.LoadAddressSize));

    return result;
  }
}
