using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.IffDpan;

namespace FileFormat.IffDpan.Tests;

[TestFixture]
public sealed class IffDpanReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffDpanReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffDpanReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dpan"));
    Assert.Throws<FileNotFoundException>(() => IffDpanReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffDpanReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[IffDpanFile.MinFileSize - 1];
    Assert.Throws<InvalidDataException>(() => IffDpanReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_FormDpanPseudoContainer_IsRejected() {
    var data = new byte[12];
    "FORM"u8.CopyTo(data);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4, 4), 4);
    "DPAN"u8.CopyTo(data.AsSpan(8, 4));

    Assert.Throws<InvalidDataException>(() => IffDpanReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AnimWithoutIlbm_IsRejected() {
    var data = new byte[12];
    "FORM"u8.CopyTo(data);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4, 4), 4);
    "ANIM"u8.CopyTo(data.AsSpan(8, 4));

    Assert.Throws<InvalidDataException>(() => IffDpanReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WriterOutput_ParsesDpanMetadataAndFrame() {
    var data = _CreateValidData(7, 5);

    var result = IffDpanReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(7));
      Assert.That(result.Height, Is.EqualTo(5));
      Assert.That(result.Version, Is.EqualTo(IffDpanFile.CurrentVersion));
      Assert.That(result.FrameCount, Is.EqualTo(1));
      Assert.That(result.Flags, Is.Zero);
      Assert.That(result.PixelData, Has.Length.EqualTo(7 * 5 * 3));
      Assert.That(result.RawData, Is.EqualTo(data));
      Assert.That(result.RawData, Is.Not.SameAs(data));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_WriterOutput_ParsesCorrectly() {
    var data = _CreateValidData(16, 9);

    using var stream = new MemoryStream(data);
    var result = IffDpanReader.FromStream(stream);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(16));
      Assert.That(result.Height, Is.EqualTo(9));
      Assert.That(result.FrameCount, Is.EqualTo(1));
      Assert.That(result.RawData, Is.EqualTo(data));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TruncatedOuterForm_IsRejected() {
    var data = _CreateValidData(3, 2);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4, 4), checked((uint)data.Length));

    Assert.Throws<InvalidDataException>(() => IffDpanReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongDpanPayloadSize_IsRejected() {
    var data = _CreateValidData(3, 2);
    var dpanOffset = _FindSequence(data, "DPAN"u8);
    Assert.That(dpanOffset, Is.GreaterThan(0));
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(dpanOffset + 4, 4), 7);

    Assert.Throws<InvalidDataException>(() => IffDpanReader.FromBytes(data));
  }

  private static byte[] _CreateValidData(int width, int height) {
    var pixels = new byte[checked(width * height * 3)];
    for (var i = 0; i < pixels.Length; i += 3) {
      pixels[i] = (byte)(i * 17);
      pixels[i + 1] = (byte)(i * 29);
      pixels[i + 2] = (byte)(i * 43);
    }

    var image = new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };

    return FormatIO.Encode<IffDpanFile>(image);
  }

  private static int _FindSequence(ReadOnlySpan<byte> data, ReadOnlySpan<byte> sequence) {
    for (var i = 0; i <= data.Length - sequence.Length; ++i)
      if (data.Slice(i, sequence.Length).SequenceEqual(sequence))
        return i;

    return -1;
  }
}
