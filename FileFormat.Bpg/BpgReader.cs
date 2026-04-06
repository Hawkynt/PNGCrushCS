using System;
using System.IO;

namespace FileFormat.Bpg;

/// <summary>Reads BPG (Better Portable Graphics) files from bytes, streams, or file paths.</summary>
public static class BpgReader {

  public static BpgFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("BPG file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static BpgFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static BpgFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static BpgFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < BpgFile.MinHeaderSize)
      throw new InvalidDataException("Data too small for a valid BPG file.");

    if (data[0] != BpgFile.Magic[0] || data[1] != BpgFile.Magic[1] || data[2] != BpgFile.Magic[2] || data[3] != BpgFile.Magic[3])
      throw new InvalidDataException("Invalid BPG magic bytes (expected 42 50 47 FB).");

    // Byte 4: pixel_format(3) | alpha1_flag(1) | bit_depth_minus_8(4)
    var byte4 = data[4];
    var pixelFormat = (BpgPixelFormat)((byte4 >> 5) & 0x07);
    var alpha1Flag = ((byte4 >> 4) & 0x01) != 0;
    var bitDepthMinus8 = byte4 & 0x0F;
    var bitDepth = bitDepthMinus8 + 8;

    // Byte 5: color_space(4) | extension_present(1) | alpha2_flag(1) | limited_range(1) | animation_flag(1)
    var byte5 = data[5];
    var colorSpace = (BpgColorSpace)((byte5 >> 4) & 0x0F);
    var extensionPresent = ((byte5 >> 3) & 0x01) != 0;
    var alpha2Flag = ((byte5 >> 2) & 0x01) != 0;
    var limitedRange = ((byte5 >> 1) & 0x01) != 0;
    var animationFlag = (byte5 & 0x01) != 0;

    var offset = 6;
    var span = new ReadOnlySpan<byte>(data);

    var width = BpgUe7.Read(span, ref offset);
    var height = BpgUe7.Read(span, ref offset);
    var pictureDataLength = BpgUe7.Read(span, ref offset);

    var extensionData = Array.Empty<byte>();
    if (extensionPresent) {
      var extensionDataLength = BpgUe7.Read(span, ref offset);
      extensionData = new byte[extensionDataLength];
      var available = Math.Min(extensionDataLength, data.Length - offset);
      if (available > 0)
        data.AsSpan(offset, available).CopyTo(extensionData.AsSpan(0));

      offset += extensionDataLength;
    }

    var pixelDataLength = Math.Min(pictureDataLength, data.Length - offset);
    var pixelData = new byte[pixelDataLength > 0 ? pixelDataLength : 0];
    if (pixelDataLength > 0)
      data.AsSpan(offset, pixelDataLength).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      PixelFormat = pixelFormat,
      BitDepth = bitDepth,
      ColorSpace = colorSpace,
      HasAlpha = alpha1Flag,
      HasAlpha2 = alpha2Flag,
      LimitedRange = limitedRange,
      IsAnimation = animationFlag,
      ExtensionPresent = extensionPresent,
      ExtensionData = extensionData,
      PixelData = pixelData,
    };
  }
}
