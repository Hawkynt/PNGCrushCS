using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Nifti;

namespace FileFormat.Nifti.Tests;

[TestFixture]
public sealed class NiftiReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NiftiReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NiftiReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".nii"));
    Assert.Throws<FileNotFoundException>(() => NiftiReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => NiftiReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidSizeOfHdr_ThrowsInvalidDataException() {
    var data = new byte[352];
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), 999);
    Encoding.ASCII.GetBytes("n+1\0").CopyTo(data.AsSpan(344));
    Assert.Throws<InvalidDataException>(() => NiftiReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[352];
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), 348);
    Encoding.ASCII.GetBytes("BAD\0").CopyTo(data.AsSpan(344));
    Assert.Throws<InvalidDataException>(() => NiftiReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidUInt8() {
    var data = _BuildMinimalNifti(4, 3, 1, NiftiDataType.UInt8, 8);
    var pixelData = new byte[4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    Array.Copy(pixelData, 0, data, 352, pixelData.Length);

    var result = NiftiReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.Depth, Is.EqualTo(1));
    Assert.That(result.Datatype, Is.EqualTo(NiftiDataType.UInt8));
    Assert.That(result.Bitpix, Is.EqualTo(8));
    Assert.That(result.PixelData.Length, Is.EqualTo(pixelData.Length));
    Assert.That(result.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NiftiReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = _BuildMinimalNifti(2, 2, 1, NiftiDataType.UInt8, 8);
    var pixelData = new byte[] { 10, 20, 30, 40 };
    Array.Copy(pixelData, 0, data, 352, pixelData.Length);

    using var ms = new MemoryStream(data);
    var result = NiftiReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.PixelData, Is.EqualTo(pixelData));
  }

  private static byte[] _BuildMinimalNifti(int width, int height, int depth, NiftiDataType datatype, short bitpix) {
    var bytesPerVoxel = bitpix / 8;
    var voxelCount = width * height * Math.Max(depth, 1);
    var pixelDataSize = voxelCount * bytesPerVoxel;
    var data = new byte[352 + pixelDataSize];
    var span = data.AsSpan();

    BinaryPrimitives.WriteInt32LittleEndian(span, 348);

    var ndims = (short)(depth > 1 ? 3 : 2);
    BinaryPrimitives.WriteInt16LittleEndian(span[40..], ndims);
    BinaryPrimitives.WriteInt16LittleEndian(span[42..], (short)width);
    BinaryPrimitives.WriteInt16LittleEndian(span[44..], (short)height);
    if (ndims >= 3)
      BinaryPrimitives.WriteInt16LittleEndian(span[46..], (short)depth);

    BinaryPrimitives.WriteInt16LittleEndian(span[70..], (short)datatype);
    BinaryPrimitives.WriteInt16LittleEndian(span[72..], bitpix);

    BinaryPrimitives.WriteSingleLittleEndian(span[108..], 352f);

    Encoding.ASCII.GetBytes("n+1\0").CopyTo(span[344..]);

    return data;
  }
}
