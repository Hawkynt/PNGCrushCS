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
      return FromSpan(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  public static BpgFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < BpgFile.MinHeaderSize)
      throw new InvalidDataException("Data too small for a valid BPG file.");

    if (!data[..4].SequenceEqual(BpgFile.Magic))
      throw new InvalidDataException("Invalid BPG magic bytes (expected 42 50 47 FB).");

    // Byte 4: pixel_format(3) | alpha1_flag(1) | bit_depth_minus_8(4)
    var byte4 = data[4];
    var pixelFormatValue = (byte4 >> 5) & 0x07;
    if (pixelFormatValue > (int)BpgPixelFormat.YCbCr422Mpeg2)
      throw new InvalidDataException($"BPG pixel_format {pixelFormatValue} is reserved.");

    var pixelFormat = (BpgPixelFormat)pixelFormatValue;
    var alpha1Flag = (byte4 & 0x10) != 0;
    var bitDepthMinus8 = byte4 & 0x0f;
    if (bitDepthMinus8 > 6)
      throw new InvalidDataException(
        $"BPG bit_depth_minus_8 is {bitDepthMinus8}; version 0.9.5 permits only component depths from 8 through 14 bits.");

    var bitDepth = bitDepthMinus8 + 8;

    // Byte 5: color_space(4) | extension_present(1) | alpha2_flag(1) | limited_range(1) | animation_flag(1)
    var byte5 = data[5];
    var colorSpaceValue = (byte5 >> 4) & 0x0f;
    if (colorSpaceValue > (int)BpgColorSpace.YCbCrBT2020Ncl)
      throw new InvalidDataException($"BPG color_space {colorSpaceValue} is reserved by version 0.9.5.");

    var colorSpace = (BpgColorSpace)colorSpaceValue;
    if (pixelFormat == BpgPixelFormat.Grayscale && colorSpace != BpgColorSpace.YCbCrBT601)
      throw new InvalidDataException("BPG grayscale pictures require color_space 0.");

    var extensionPresent = (byte5 & 0x08) != 0;
    var alpha2Flag = (byte5 & 0x04) != 0;
    var limitedRange = (byte5 & 0x02) != 0;
    var animationFlag = (byte5 & 0x01) != 0;

    var offset = 6;
    var width = _ReadPositiveSize(data, ref offset, "picture_width");
    var height = _ReadPositiveSize(data, ref offset, "picture_height");
    var pictureDataLength = BpgUe7.ReadUInt32(data, ref offset);

    var extensionData = Array.Empty<byte>();
    if (extensionPresent) {
      var extensionDataLength = BpgUe7.ReadUInt32(data, ref offset);
      extensionData = _TakeDeclaredBytes(data, ref offset, extensionDataLength, "extension_data_length");
    }

    // A stated length of zero means that the picture data runs to EOF.
    var remaining = data.Length - offset;
    var actualPictureLength = pictureDataLength == 0 ? (uint)remaining : pictureDataLength;
    if (actualPictureLength > (uint)remaining)
      throw new InvalidDataException(
        $"BPG picture_data_length declares {actualPictureLength} byte(s), but only {remaining} remain in the file.");
    if (actualPictureLength > int.MaxValue)
      throw new InvalidDataException("The BPG picture payload is too large for this implementation to materialize.");

    var pixelData = data.Slice(offset, (int)actualPictureLength).ToArray();

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

  public static BpgFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static int _ReadPositiveSize(ReadOnlySpan<byte> data, ref int offset, string fieldName) {
    var value = BpgUe7.ReadUInt32(data, ref offset);
    if (value == 0)
      throw new InvalidDataException($"BPG {fieldName} must not be zero.");
    if (value > int.MaxValue)
      throw new InvalidDataException($"BPG {fieldName} {value} exceeds this implementation's image-size model.");
    return (int)value;
  }

  private static byte[] _TakeDeclaredBytes(ReadOnlySpan<byte> data, ref int offset, uint length, string fieldName) {
    var remaining = data.Length - offset;
    if (length > (uint)remaining)
      throw new InvalidDataException(
        $"BPG {fieldName} declares {length} byte(s), but only {remaining} remain in the file.");
    if (length > int.MaxValue)
      throw new InvalidDataException($"BPG {fieldName} is too large for this implementation to materialize.");

    var result = data.Slice(offset, (int)length).ToArray();
    offset += (int)length;
    return result;
  }
}
