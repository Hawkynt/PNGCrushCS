using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;
using FileFormat.UleadImageLibrary;
using Hawkynt.FileFormats.Images;

namespace FileFormat.UleadImageLibrary.Tests;

[TestFixture]
public sealed class UleadImageLibraryTests {

  private static byte[] _Jpeg(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i)
      pixels[i * 3] = pixels[i * 3 + 1] = pixels[i * 3 + 2] = (byte)(i * 3);

    return JpegWriter.ToBytes(JpegFile.FromRawImage(new RawImage {
      Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels,
    }));
  }

  /// <summary>Builds one of these the way the samples are built: a header stating the count, then a
  /// record per item stating the length of its picture, then the padding these end with.</summary>
  private static byte[] _Library(int items, int width, int height, int trailingZeros = 16) {
    var jpeg = _Jpeg(width, height);
    var body = new List<byte>();

    for (var i = 0; i < items; ++i) {
      var record = new byte[UleadImageLibraryFile.RecordHeaderSize];
      BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(UleadImageLibraryFile.RecordTypeAt), UleadImageLibraryFile.RecordType);
      BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(UleadImageLibraryFile.WidthAt), width);
      BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(UleadImageLibraryFile.HeightAt), height);
      BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(UleadImageLibraryFile.PlaneCountAt), 3);
      BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(UleadImageLibraryFile.JpegLengthAt), jpeg.Length);
      body.AddRange(record);
      body.AddRange(jpeg);
      body.AddRange(new byte[UleadImageLibraryFile.MetadataSize]);
    }

    var first = UleadImageLibraryFile.FirstRecordBase + 4 * items;
    var result = new byte[first + body.Count + trailingZeros];
    UleadImageLibraryFile.Magic.CopyTo(result);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(UleadImageLibraryFile.ItemCountAt), items);
    body.CopyTo(result, first);

    return result;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => UleadImageLibraryReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongMagic_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => UleadImageLibraryReader.FromBytes(new byte[UleadImageLibraryFile.FirstRecordBase + 64]));

  [Test]
  [Category("Integration")]
  public void FromBytes_EveryItemTheCountStatesIsRead() {
    var file = UleadImageLibraryReader.FromBytes(_Library(3, 32, 32));

    Assert.Multiple(() => {
      Assert.That(UleadImageLibraryFile.ImageCount(file), Is.EqualTo(3));
      Assert.That(UleadImageLibraryFile.ToRawImage(file, 2).Width, Is.EqualTo(32));
    });
  }

  /// <summary>The chain is computed, so a first record anywhere but where the count puts it is a
  /// misparse rather than something to search past.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_AStatedCountThatMovesTheFirstRecord_ThrowsInvalidDataException() {
    var data = _Library(2, 16, 16);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(UleadImageLibraryFile.ItemCountAt), 3);

    Assert.Throws<InvalidDataException>(() => UleadImageLibraryReader.FromBytes(data));
  }

  /// <summary>The stated length must land exactly on the picture's own end marker.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_AJpegLengthThatMissesTheEndMarker_ThrowsInvalidDataException() {
    var data = _Library(1, 16, 16);
    var record = UleadImageLibraryFile.FirstRecordBase + 4;
    var stated = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(record + UleadImageLibraryFile.JpegLengthAt));
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(record + UleadImageLibraryFile.JpegLengthAt), stated - 2);

    Assert.Throws<InvalidDataException>(() => UleadImageLibraryReader.FromBytes(data));
  }

  /// <summary>And the size the record states must be the size the picture states of itself.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ARecordDisagreeingWithItsPicture_ThrowsInvalidDataException() {
    var data = _Library(1, 16, 16);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(UleadImageLibraryFile.FirstRecordBase + 4 + UleadImageLibraryFile.WidthAt), 24);

    Assert.Throws<InvalidDataException>(() => UleadImageLibraryReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ATailThatIsNotPadding_ThrowsInvalidDataException() {
    var data = _Library(1, 16, 16);
    data[^1] = 0x42;

    Assert.Throws<InvalidDataException>(() => UleadImageLibraryReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void DetectFromBytes_IsFoundByWhatItOpensWith()
    => Assert.That(FormatRegistry.DetectFromBytes(_Library(1, 16, 16)), Is.EqualTo(ImageFormat.UleadImageLibrary));
}
