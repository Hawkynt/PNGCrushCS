using System;

namespace FileFormat.NokiaOperatorLogo;

/// <summary>Assembles Nokia Operator Logo (NOL) file bytes from a NokiaOperatorLogoFile.</summary>
public static class NokiaOperatorLogoWriter {

  public static byte[] ToBytes(NokiaOperatorLogoFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelCount = file.Width * file.Height;
    var result = new byte[NokiaOperatorLogoFile.HeaderSize + pixelCount];

    // Magic: "NOL"
    result[0] = NokiaOperatorLogoFile.Magic[0];
    result[1] = NokiaOperatorLogoFile.Magic[1];
    result[2] = NokiaOperatorLogoFile.Magic[2];

    // Null terminator
    result[3] = 0x00;

    // Unknown (version)
    result[4] = 0x01;
    result[5] = 0x00;

    // MCC as little-endian uint16
    result[6] = (byte)(file.Mcc & 0xFF);
    result[7] = (byte)((file.Mcc >> 8) & 0xFF);

    // MNC
    result[8] = (byte)(file.Mnc & 0xFF);

    // Padding
    result[9] = 0x00;

    // Width
    result[10] = (byte)file.Width;

    // Padding
    result[11] = 0x00;

    // Height
    result[12] = (byte)file.Height;

    // Padding
    result[13] = 0x00;

    // Unknown trailing header bytes
    result[14] = 0x01;
    result[15] = 0x00;
    result[16] = 0x01;
    result[17] = 0x00;
    result[18] = 0x00;
    result[19] = 0x00;

    // Pixel data: convert 1bpp packed to ASCII '0'/'1'
    var bytesPerRow = (file.Width + 7) / 8;
    for (var y = 0; y < file.Height; ++y)
      for (var x = 0; x < file.Width; ++x) {
        var byteIndex = y * bytesPerRow + x / 8;
        var bitIndex = 7 - (x % 8);
        var bit = (file.PixelData[byteIndex] >> bitIndex) & 1;
        result[NokiaOperatorLogoFile.HeaderSize + y * file.Width + x] = bit == 1 ? (byte)'1' : (byte)'0';
      }

    return result;
  }
}
