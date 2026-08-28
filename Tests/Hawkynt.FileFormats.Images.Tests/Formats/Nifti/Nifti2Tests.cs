using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.Nifti.Tests;

[TestFixture]
public sealed class Nifti2Tests {

  [Test]
  [Category("Integration")]
  public void Nifti2_Rgba32_RoundTripsExactly() {
    var data = new byte[13 * 7 * 4];
    for (var i = 0; i < data.Length / 4; ++i) {
      data[i * 4] = (byte)(i * 3 + 1);
      data[i * 4 + 1] = (byte)(i * 7 + 2);
      data[i * 4 + 2] = (byte)(i * 11 + 3);
      data[i * 4 + 3] = (byte)(255 - i * 2);
    }
    var source = new RawImage { Width = 13, Height = 7, Format = PixelFormat.Rgba32, PixelData = data };

    var bytes = FormatIO.Encode<Nifti2File>(source);
    Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes), Is.EqualTo(540));
    Assert.That(bytes.AsSpan(4, 8).SequenceEqual("n+2\0\r\n\x1A\n"u8), Is.True);

    var decoded = FormatIO.Decode<Nifti2File>(bytes);
    Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgba32));
    Assert.That(decoded.PixelData, Is.EqualTo(data));
  }

  [Test]
  [Category("Integration")]
  public void Nifti2Gzip_RoundTripsExactly() {
    var data = new byte[17 * 5];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 19 + 5);
    var source = new RawImage { Width = 17, Height = 5, Format = PixelFormat.Gray8, PixelData = data };

    var bytes = FormatIO.Encode<Nifti2GzipFile>(source);
    Assert.That(bytes[0], Is.EqualTo(0x1F));
    Assert.That(bytes[1], Is.EqualTo(0x8B));

    var decoded = FormatIO.Decode<Nifti2GzipFile>(bytes).EnsureFormat(PixelFormat.Gray8);
    Assert.That(decoded.PixelData, Is.EqualTo(data));
  }

  [Test]
  [Category("Integration")]
  public void Nifti1_BigEndianUInt16_IsNormalizedBeforeDecode() {
    var bytes = new byte[356];
    BinaryPrimitives.WriteInt32BigEndian(bytes, 348);
    BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(40), 2);
    BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(42), 2);
    BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(44), 1);
    BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(70), (short)NiftiDataType.UInt16);
    BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(72), 16);
    BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(108), BitConverter.SingleToInt32Bits(352f));
    BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(112), BitConverter.SingleToInt32Bits(1f));
    "n+1\0"u8.CopyTo(bytes.AsSpan(344));
    // Two big-endian UInt16 voxels: 0x1234, 0xABCD.
    bytes[352] = 0x12; bytes[353] = 0x34;
    bytes[354] = 0xAB; bytes[355] = 0xCD;

    var file = NiftiReader.FromBytes(bytes);
    Assert.That(file.PixelData, Is.EqualTo(new byte[] { 0x34, 0x12, 0xCD, 0xAB }));
    var decoded = NiftiFile.ToRawImage(file);
    Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Gray16));
    Assert.That(decoded.PixelData, Is.EqualTo(new byte[] { 0x12, 0x34, 0xAB, 0xCD }));
  }

  [Test]
  [Category("Integration")]
  public void Nifti2_BigEndianUInt16_IsAccepted() {
    var bytes = new byte[548];
    BinaryPrimitives.WriteInt32BigEndian(bytes, 540);
    "n+2\0\r\n\x1A\n"u8.CopyTo(bytes.AsSpan(4));
    BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(12), (short)NiftiDataType.UInt16);
    BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(14), 16);
    BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(16), 2);
    BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(24), 2);
    BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(32), 1);
    BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(40), 1);
    BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(168), 544);
    BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(176), BitConverter.DoubleToInt64Bits(1.0));
    bytes[544] = 0x00; bytes[545] = 0x01;
    bytes[546] = 0xFE; bytes[547] = 0xDC;

    var decoded = Nifti2File.ToRawImage(Nifti2Reader.FromSpan(bytes));
    Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Gray16));
    Assert.That(decoded.PixelData, Is.EqualTo(new byte[] { 0x00, 0x01, 0xFE, 0xDC }));
  }
}
