using System;

namespace FileFormat.MicroIllustrator;

/// <summary>Assembles Commodore 64 Micro Illustrator file bytes from a MicroIllustratorFile.</summary>
public static class MicroIllustratorWriter {

  public static byte[] ToBytes(MicroIllustratorFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[MicroIllustratorFile.ExpectedFileSize];
    var offset = 0;

    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += MicroIllustratorFile.LoadAddressSize;

    file.BitmapData.AsSpan(0, MicroIllustratorFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += MicroIllustratorFile.BitmapDataSize;

    file.VideoMatrix.AsSpan(0, MicroIllustratorFile.VideoMatrixSize).CopyTo(result.AsSpan(offset));
    offset += MicroIllustratorFile.VideoMatrixSize;

    file.ColorRam.AsSpan(0, MicroIllustratorFile.ColorRamSize).CopyTo(result.AsSpan(offset));
    offset += MicroIllustratorFile.ColorRamSize;

    result[offset] = file.BackgroundColor;

    return result;
  }
}
