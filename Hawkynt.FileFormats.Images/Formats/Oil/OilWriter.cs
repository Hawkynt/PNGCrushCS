using System;
using System.Buffers.Binary;
using System.Text;

namespace FileFormat.Oil;

/// <summary>Writes the documented OIL v1 single-image layout using uncompressed BGRA pixels.</summary>
public static class OilWriter {

  public static byte[] ToBytes(OilFile file) {
    if (file.Width <= 0 || file.Height <= 0)
      throw new ArgumentException("OIL dimensions must be positive.", nameof(file));

    var channels = file.Format switch {
      FileFormat.Core.PixelFormat.Gray8 => 1,
      FileFormat.Core.PixelFormat.Rgb24 => 3,
      FileFormat.Core.PixelFormat.Rgba32 => 4,
      _ => throw new ArgumentException($"OIL writer expects Gray8, Rgb24 or Rgba32, got {file.Format}.", nameof(file)),
    };
    var type = channels switch {
      1 => OilFile.TypeLuminance,
      3 => OilFile.TypeBgr,
      _ => OilFile.TypeBgra,
    };
    var expected = checked(file.Width * file.Height * channels);
    if (file.PixelData == null || file.PixelData.Length < expected)
      throw new ArgumentException($"OIL needs {expected} pixel bytes.", nameof(file));

    var directoryOffset = OilFile.HeaderSize;
    var imageOffset = checked(directoryOffset + OilFile.DirectoryEntrySize);
    var imageLength = checked(OilFile.ImageHeaderSize + expected);
    var output = new byte[checked(imageOffset + imageLength)];

    OilFile.Signature.CopyTo(output);
    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(4, 4), OilFile.MagicNumber);
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(8, 2), OilFile.SupportedVersion);
    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(10, 4), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(14, 4), checked((uint)directoryOffset));
    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(18, 4), 0); // no animation list
    Encoding.ASCII.GetBytes(OilFile.HeadString).CopyTo(output, 22);
    output[22 + OilFile.HeadStringLength - 1] = 0;

    Encoding.ASCII.GetBytes("image").CopyTo(output, directoryOffset);
    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(directoryOffset + 255, 4), checked((uint)imageOffset));
    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(directoryOffset + 259, 4), checked((uint)imageLength));

    var image = output.AsSpan(imageOffset, imageLength);
    BinaryPrimitives.WriteUInt32LittleEndian(image, checked((uint)file.Width));
    BinaryPrimitives.WriteUInt32LittleEndian(image[4..], checked((uint)file.Height));
    BinaryPrimitives.WriteUInt32LittleEndian(image[8..], 1); // one depth slice
    image[12] = (byte)channels;
    image[13] = 1; // one byte per channel
    image[14] = type;
    image[15] = OilFile.CompressionNone;
    image[16] = 0;
    BinaryPrimitives.WriteUInt32LittleEndian(image[17..], 0); // duration
    BinaryPrimitives.WriteUInt32LittleEndian(image[21..], checked((uint)expected));

    var stored = image[OilFile.ImageHeaderSize..];
    var stride = file.Width * channels;
    for (var y = 0; y < file.Height; ++y) {
      var fromRow = y * stride;
      var toRow = (file.Height - 1 - y) * stride;
      if (channels == 1) {
        file.PixelData.AsSpan(fromRow, stride).CopyTo(stored[toRow..]);
        continue;
      }

      for (var x = 0; x < file.Width; ++x) {
        var from = fromRow + x * channels;
        var to = toRow + x * channels;
        stored[to] = file.PixelData[from + 2];
        stored[to + 1] = file.PixelData[from + 1];
        stored[to + 2] = file.PixelData[from];
        if (channels == 4)
          stored[to + 3] = file.PixelData[from + 3];
      }
    }

    return output;
  }
}
