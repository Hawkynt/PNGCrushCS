using System;
using System.Text;
using FileFormat.Core;

namespace FileFormat.NokiaNlm;

/// <summary>Assembles Nokia Logo Manager file bytes.</summary>
public static class NokiaNlmWriter {

  public static byte[] ToBytes(NokiaNlmFile file) {
    var packed = BilevelRows.Pack(file.PixelData ?? [], file.Width, file.Height);
    var result = new byte[NokiaNlmFile.HeaderSize + packed.Length];

    Encoding.ASCII.GetBytes(NokiaNlmFile.Signature).CopyTo(result, 0);
    result[NokiaNlmFile.WidthOffset] = (byte)file.Width;
    result[NokiaNlmFile.HeightOffset] = (byte)file.Height;

    packed.CopyTo(result, NokiaNlmFile.HeaderSize);
    return result;
  }
}
