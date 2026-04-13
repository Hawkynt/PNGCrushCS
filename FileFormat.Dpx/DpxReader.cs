using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Dpx;

/// <summary>Reads DPX files from bytes, streams, or file paths.</summary>
public static class DpxReader {

  private const int _MINIMUM_HEADER_SIZE = 2048;
  private const int _IMAGE_INFO_OFFSET = 768;

  public static DpxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("DPX file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DpxFile FromStream(Stream stream) {
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

  public static DpxFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < _MINIMUM_HEADER_SIZE)
      throw new InvalidDataException("Data too small for a valid DPX file.");

    var span = data;

    var rawMagic = BinaryPrimitives.ReadInt32BigEndian(span);
    bool isBigEndian;
    if (rawMagic == DpxHeader.MagicBigEndian)
      isBigEndian = true;
    else if (rawMagic == DpxHeader.MagicLittleEndian)
      isBigEndian = false;
    else
      throw new InvalidDataException("Invalid DPX magic number.");

    var header = DpxHeader.ReadFrom(span);

    var dataOffset = header.ImageDataOffset;
    if (dataOffset < _MINIMUM_HEADER_SIZE)
      dataOffset = _MINIMUM_HEADER_SIZE;

    // Image information header at offset 768
    var imageInfo = span[_IMAGE_INFO_OFFSET..];
    var orientation = isBigEndian
      ? BinaryPrimitives.ReadInt16BigEndian(imageInfo)
      : BinaryPrimitives.ReadInt16LittleEndian(imageInfo);

    var numElements = isBigEndian
      ? BinaryPrimitives.ReadInt16BigEndian(imageInfo[2..])
      : BinaryPrimitives.ReadInt16LittleEndian(imageInfo[2..]);

    var width = isBigEndian
      ? BinaryPrimitives.ReadInt32BigEndian(imageInfo[4..])
      : BinaryPrimitives.ReadInt32LittleEndian(imageInfo[4..]);

    var height = isBigEndian
      ? BinaryPrimitives.ReadInt32BigEndian(imageInfo[8..])
      : BinaryPrimitives.ReadInt32LittleEndian(imageInfo[8..]);

    // First image element descriptor starts at offset 780 (768 + 12)
    // Element descriptor byte at offset 780 + 16 = 796
    // Bits per element byte at offset 780 + 20 = 800
    // Packing short at offset 780 + 21 = 801...
    // Actually per SMPTE 268M, first element structure is at offset 780:
    //   DataSign(4), RefLowData(4), RefLowQuantity(4), RefHighData(4), RefHighQuantity(4)
    //   Descriptor(1) at offset 800, Transfer(1) at 801, Colorimetry(1) at 802, BitSize(1) at 803
    //   Packing(2) at 804, Encoding(2) at 806, DataOffset(4) at 808, EolPadding(4) at 812, EoImagePadding(4) at 816
    //   Description(32) at 820
    var elementBase = _IMAGE_INFO_OFFSET + 12; // offset 780
    byte descriptor = span[elementBase + 20]; // offset 800
    byte transfer = span[elementBase + 21]; // offset 801
    byte bitsPerElement = span[elementBase + 23]; // offset 803

    var packing = isBigEndian
      ? BinaryPrimitives.ReadInt16BigEndian(span[(elementBase + 24)..]) // offset 804
      : BinaryPrimitives.ReadInt16LittleEndian(span[(elementBase + 24)..]);

    var pixelDataLength = data.Length - dataOffset;
    var pixelData = new byte[pixelDataLength > 0 ? pixelDataLength : 0];
    if (pixelDataLength > 0)
      data.Slice(dataOffset, pixelDataLength).CopyTo(pixelData.AsSpan(0));

    return new DpxFile {
      Width = width,
      Height = height,
      BitsPerElement = bitsPerElement,
      Descriptor = (DpxDescriptor)descriptor,
      Packing = (DpxPacking)packing,
      Transfer = (DpxTransfer)transfer,
      IsBigEndian = isBigEndian,
      ImageDataOffset = dataOffset,
      PixelData = pixelData
    };
    }

  public static DpxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
