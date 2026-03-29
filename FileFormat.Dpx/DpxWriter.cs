using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Dpx;

/// <summary>Assembles DPX file bytes from pixel data.</summary>
public static class DpxWriter {

  private const int _TOTAL_HEADER_SIZE = 2048;
  private const int _IMAGE_INFO_OFFSET = 768;

  public static byte[] ToBytes(DpxFile file) => Assemble(
    file.PixelData,
    file.Width,
    file.Height,
    file.BitsPerElement,
    file.Descriptor,
    file.Packing,
    file.Transfer,
    file.IsBigEndian
  );

  internal static byte[] Assemble(
    byte[] pixelData,
    int width,
    int height,
    int bitsPerElement,
    DpxDescriptor descriptor,
    DpxPacking packing,
    DpxTransfer transfer,
    bool isBigEndian
  ) {
    var dataOffset = _TOTAL_HEADER_SIZE;
    var fileSize = dataOffset + pixelData.Length;
    var result = new byte[fileSize];
    var span = result.AsSpan();

    var magic = isBigEndian ? DpxHeader.MagicBigEndian : DpxHeader.MagicLittleEndian;

    var header = new DpxHeader(
      magic,
      dataOffset,
      "V2.0",
      fileSize,
      1,
      _IMAGE_INFO_OFFSET + 640, // generic header = file info (768) + image info (640) = 1408
      0
    );

    header.WriteTo(span);

    // Image information header at offset 768
    var imageInfo = span[_IMAGE_INFO_OFFSET..];
    if (isBigEndian) {
      BinaryPrimitives.WriteInt16BigEndian(imageInfo, 0); // orientation: left-to-right, top-to-bottom
      BinaryPrimitives.WriteInt16BigEndian(imageInfo[2..], 1); // number of elements
      BinaryPrimitives.WriteInt32BigEndian(imageInfo[4..], width);
      BinaryPrimitives.WriteInt32BigEndian(imageInfo[8..], height);
    } else {
      BinaryPrimitives.WriteInt16LittleEndian(imageInfo, 0);
      BinaryPrimitives.WriteInt16LittleEndian(imageInfo[2..], 1);
      BinaryPrimitives.WriteInt32LittleEndian(imageInfo[4..], width);
      BinaryPrimitives.WriteInt32LittleEndian(imageInfo[8..], height);
    }

    // First image element descriptor at offset 780 (768 + 12)
    var elementBase = _IMAGE_INFO_OFFSET + 12;
    span[elementBase + 20] = (byte)descriptor; // offset 800
    span[elementBase + 21] = (byte)transfer; // offset 801
    span[elementBase + 23] = (byte)bitsPerElement; // offset 803

    if (isBigEndian)
      BinaryPrimitives.WriteInt16BigEndian(span[(elementBase + 24)..], (short)packing); // offset 804
    else
      BinaryPrimitives.WriteInt16LittleEndian(span[(elementBase + 24)..], (short)packing);

    pixelData.AsSpan(0, pixelData.Length).CopyTo(result.AsSpan(dataOffset));

    return result;
  }
}
