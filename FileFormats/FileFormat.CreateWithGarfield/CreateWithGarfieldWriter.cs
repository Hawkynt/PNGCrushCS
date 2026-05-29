using System;

namespace FileFormat.CreateWithGarfield;

/// <summary>Assembles Commodore 64 Create with Garfield hires file bytes from a CreateWithGarfieldFile.</summary>
public static class CreateWithGarfieldWriter {

  public static byte[] ToBytes(CreateWithGarfieldFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[CreateWithGarfieldFile.ExpectedFileSize];
    var offset = 0;

    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += CreateWithGarfieldFile.LoadAddressSize;

    file.BitmapData.AsSpan(0, Math.Min(file.BitmapData.Length, CreateWithGarfieldFile.BitmapDataSize)).CopyTo(result.AsSpan(offset));
    offset += CreateWithGarfieldFile.BitmapDataSize;

    file.ScreenRam.AsSpan(0, Math.Min(file.ScreenRam.Length, CreateWithGarfieldFile.ScreenRamSize)).CopyTo(result.AsSpan(offset));
    offset += CreateWithGarfieldFile.ScreenRamSize;

    result[offset] = file.BorderColor;

    return result;
  }
}
