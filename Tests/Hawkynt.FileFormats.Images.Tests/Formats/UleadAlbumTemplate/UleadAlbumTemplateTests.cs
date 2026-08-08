using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.Jpeg;
using FileFormat.UleadAlbumTemplate;
using Hawkynt.FileFormats.Images;

namespace FileFormat.UleadAlbumTemplate.Tests;

[TestFixture]
public sealed class UleadAlbumTemplateTests {

  private const int _HEADER_SIZE = 0x200;

  private static byte[] _Jpeg(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i)
      pixels[i * 3] = pixels[i * 3 + 1] = pixels[i * 3 + 2] = (byte)(i * 7);

    return JpegWriter.ToBytes(JpegFile.FromRawImage(new RawImage {
      Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels,
    }));
  }

  /// <summary>Builds one of these: a header pointing at a directory that runs to the end of the file,
  /// with one entry per record.</summary>
  private static byte[] _Templates(int count, int width, int height) {
    var jpeg = _Jpeg(width, height);
    var records = new List<byte>();
    var offsets = new int[count];

    for (var i = 0; i < count; ++i) {
      offsets[i] = _HEADER_SIZE + records.Count;
      var record = new byte[UleadAlbumTemplateFile.DefaultRecordHeaderSize];
      BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(UleadAlbumTemplateFile.WidthAt), (ushort)width);
      BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(UleadAlbumTemplateFile.HeightAt), (ushort)height);
      BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(UleadAlbumTemplateFile.PlaneCountAt), 3);
      BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(UleadAlbumTemplateFile.JpegLengthAt), jpeg.Length);
      records.AddRange(record);
      records.AddRange(jpeg);
    }

    var directoryOffset = _HEADER_SIZE + records.Count;
    var namesAt = count * UleadAlbumTemplateFile.DirectoryEntrySize;
    var names = new List<byte>();
    var nameOffsets = new int[count];
    for (var i = 0; i < count; ++i) {
      nameOffsets[i] = namesAt + names.Count;
      names.AddRange(Encoding.ASCII.GetBytes($"T{i}.bmp"));
      names.Add(0);
    }

    var directoryLength = namesAt + names.Count;
    var result = new byte[directoryOffset + directoryLength];

    UleadAlbumTemplateFile.Magic.CopyTo(result);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(UleadAlbumTemplateFile.DirectoryOffsetAt), directoryOffset);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(UleadAlbumTemplateFile.DirectoryLengthAt), directoryLength);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(UleadAlbumTemplateFile.RecordHeaderSizeAt), UleadAlbumTemplateFile.DefaultRecordHeaderSize);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(UleadAlbumTemplateFile.EntryCountAt), count);
    records.CopyTo(result, _HEADER_SIZE);

    for (var i = 0; i < count; ++i) {
      var entry = directoryOffset + i * UleadAlbumTemplateFile.DirectoryEntrySize;
      BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(entry), offsets[i]);
      BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(entry + 4), nameOffsets[i]);
    }

    names.CopyTo(result, directoryOffset + namesAt);

    return result;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => UleadAlbumTemplateReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongMagic_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => UleadAlbumTemplateReader.FromBytes(new byte[_HEADER_SIZE]));

  [Test]
  [Category("Integration")]
  public void FromBytes_EveryTemplateTheDirectoryListsIsRead() {
    var file = UleadAlbumTemplateReader.FromBytes(_Templates(3, 32, 24));

    Assert.Multiple(() => {
      Assert.That(UleadAlbumTemplateFile.ImageCount(file), Is.EqualTo(3));
      Assert.That(file.Templates[1].Name, Is.EqualTo("T1.bmp"), "the directory names them");
      Assert.That(UleadAlbumTemplateFile.ToRawImage(file, 2).Width, Is.EqualTo(32));
    });
  }

  /// <summary>The directory runs to the end of the file; one that does not is a header read somewhere
  /// that happens to hold two plausible numbers.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_TheDirectoryMustAccountForTheFile() {
    var data = _Templates(2, 16, 16);
    Array.Resize(ref data, data.Length + 1);

    Assert.Throws<InvalidDataException>(() => UleadAlbumTemplateReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ARecordDisagreeingWithItsPicture_ThrowsInvalidDataException() {
    var data = _Templates(1, 16, 16);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(_HEADER_SIZE + UleadAlbumTemplateFile.WidthAt), 24);

    Assert.Throws<InvalidDataException>(() => UleadAlbumTemplateReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void DetectFromBytes_IsFoundByWhatItOpensWith()
    => Assert.That(FormatRegistry.DetectFromBytes(_Templates(1, 16, 16)), Is.EqualTo(ImageFormat.UleadAlbumTemplate));
}
