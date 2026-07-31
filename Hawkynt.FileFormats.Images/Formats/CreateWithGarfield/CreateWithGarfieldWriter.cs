using System;

namespace FileFormat.CreateWithGarfield;

/// <summary>Assembles Commodore 64 Create with Garfield hires file bytes from a CreateWithGarfieldFile.</summary>
public static class CreateWithGarfieldWriter {

  public static byte[] ToBytes(CreateWithGarfieldFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[CreateWithGarfieldFile.ExpectedFileSize];

    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);

    file.BitmapData.AsSpan(0, Math.Min(file.BitmapData.Length, CreateWithGarfieldFile.BitmapDataSize))
      .CopyTo(result.AsSpan(CreateWithGarfieldFile.BitmapOffset));
    file.VideoMatrix.AsSpan(0, Math.Min(file.VideoMatrix.Length, CreateWithGarfieldFile.VideoMatrixSize))
      .CopyTo(result.AsSpan(CreateWithGarfieldFile.VideoMatrixOffset));
    file.ColorRam.AsSpan(0, Math.Min(file.ColorRam.Length, CreateWithGarfieldFile.ColorRamSize))
      .CopyTo(result.AsSpan(CreateWithGarfieldFile.ColorRamOffset));
    result[CreateWithGarfieldFile.BackgroundOffset] = file.BackgroundColor;

    return result;
  }
}
