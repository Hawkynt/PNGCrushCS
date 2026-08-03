using System;

namespace FileFormat.RunPaint;

/// <summary>Assembles Run Paint (.rpm) file bytes from a RunPaintFile.</summary>
public static class RunPaintWriter {

  public static byte[] ToBytes(RunPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var payload = new byte[RunPaintFile.UncompressedPayloadSize];
    var offset = 0;

    file.BitmapData.AsSpan(0, RunPaintFile.BitmapDataSize).CopyTo(payload.AsSpan(offset));
    offset += RunPaintFile.BitmapDataSize;

    file.ScreenRam.AsSpan(0, RunPaintFile.ScreenRamSize).CopyTo(payload.AsSpan(offset));
    offset += RunPaintFile.ScreenRamSize;

    file.ColorRam.AsSpan(0, RunPaintFile.ColorRamSize).CopyTo(payload.AsSpan(offset));
    offset += RunPaintFile.ColorRamSize;

    payload[offset] = file.BackgroundColor;

    // Written as it stands rather than run-length coded. The one real sample is a plain screen after
    // the load address, which is what the reader now expects of anything long enough to be one; a
    // coded payload that happened to come out no shorter would be read as a plain one and mangled.
    var result = new byte[RunPaintFile.LoadAddressSize + payload.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    payload.AsSpan(0, payload.Length).CopyTo(result.AsSpan(RunPaintFile.LoadAddressSize));

    return result;
  }
}
