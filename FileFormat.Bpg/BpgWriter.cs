using System;
using System.Collections.Generic;

namespace FileFormat.Bpg;

/// <summary>Assembles BPG (Better Portable Graphics) file bytes from a BpgFile model.</summary>
public static class BpgWriter {

  public static byte[] ToBytes(BpgFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var output = new List<byte>();

    // Magic bytes
    output.AddRange(BpgFile.Magic);

    // Byte 4: pixel_format(3) | alpha1_flag(1) | bit_depth_minus_8(4)
    var bitDepthMinus8 = file.BitDepth - 8;
    var byte4 = (byte)((((int)file.PixelFormat & 0x07) << 5) | ((file.HasAlpha ? 1 : 0) << 4) | (bitDepthMinus8 & 0x0F));
    output.Add(byte4);

    // Byte 5: color_space(4) | extension_present(1) | alpha2_flag(1) | limited_range(1) | animation_flag(1)
    var byte5 = (byte)(
      (((int)file.ColorSpace & 0x0F) << 4) |
      ((file.ExtensionPresent ? 1 : 0) << 3) |
      ((file.HasAlpha2 ? 1 : 0) << 2) |
      ((file.LimitedRange ? 1 : 0) << 1) |
      (file.IsAnimation ? 1 : 0)
    );
    output.Add(byte5);

    // Width and Height as ue7
    BpgUe7.Write(output, file.Width);
    BpgUe7.Write(output, file.Height);

    // Picture data length as ue7
    BpgUe7.Write(output, file.PixelData.Length);

    // Extension data if present
    if (file.ExtensionPresent) {
      BpgUe7.Write(output, file.ExtensionData.Length);
      output.AddRange(file.ExtensionData);
    }

    // Pixel/picture data
    output.AddRange(file.PixelData);

    return output.ToArray();
  }
}
