using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.IffAcbm;

namespace FileFormat.IffAcbm.Tests;

[TestFixture]
public sealed class IffAcbmReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffAcbmReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffAcbmReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".acbm"));
    Assert.Throws<FileNotFoundException>(() => IffAcbmReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffAcbmReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[8];
    Assert.Throws<InvalidDataException>(() => IffAcbmReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[12];
    bad[0] = (byte)'X'; bad[1] = (byte)'Y'; bad[2] = (byte)'Z'; bad[3] = (byte)'Z';
    Assert.Throws<InvalidDataException>(() => IffAcbmReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidFormType_ThrowsInvalidDataException() {
    var bad = new byte[40];
    bad[0] = (byte)'F'; bad[1] = (byte)'O'; bad[2] = (byte)'R'; bad[3] = (byte)'M';
    bad[4] = 0; bad[5] = 0; bad[6] = 0; bad[7] = 20;
    bad[8] = (byte)'I'; bad[9] = (byte)'L'; bad[10] = (byte)'B'; bad[11] = (byte)'M';
    Assert.Throws<InvalidDataException>(() => IffAcbmReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MissingBmhd_ThrowsInvalidDataException() {
    // FORM + ACBM + an ABIT chunk but no BMHD
    using var ms = new MemoryStream();
    ms.Write(Encoding.ASCII.GetBytes("FORM"));
    _WriteInt32BE(ms, 4 + 8 + 4); // formDataSize: "ACBM" + ABIT chunk (8 header + 4 data)
    ms.Write(Encoding.ASCII.GetBytes("ACBM"));
    ms.Write(Encoding.ASCII.GetBytes("ABIT"));
    _WriteInt32BE(ms, 4);
    ms.Write(new byte[4]);
    Assert.Throws<InvalidDataException>(() => IffAcbmReader.FromBytes(ms.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MissingAbit_ThrowsInvalidDataException() {
    // FORM + ACBM + BMHD but no ABIT
    using var ms = new MemoryStream();
    ms.Write(Encoding.ASCII.GetBytes("FORM"));
    _WriteInt32BE(ms, 4 + 8 + 20); // "ACBM" + BMHD chunk
    ms.Write(Encoding.ASCII.GetBytes("ACBM"));
    ms.Write(Encoding.ASCII.GetBytes("BMHD"));
    _WriteInt32BE(ms, 20);
    var bmhd = new byte[20];
    BinaryPrimitives.WriteUInt16BigEndian(bmhd.AsSpan(0), 8);  // width
    BinaryPrimitives.WriteUInt16BigEndian(bmhd.AsSpan(2), 2);  // height
    bmhd[8] = 1; // numPlanes
    ms.Write(bmhd);
    Assert.Throws<InvalidDataException>(() => IffAcbmReader.FromBytes(ms.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid4Plane_ParsesCorrectly() {
    var data = TestHelper.BuildMinimalAcbm(16, 4, 4);
    var result = IffAcbmReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(16));
      Assert.That(result.Height, Is.EqualTo(4));
      Assert.That(result.NumPlanes, Is.EqualTo(4));
      Assert.That(result.PixelData.Length, Is.EqualTo(16 * 4));
      Assert.That(result.Palette, Is.Not.Null);
      Assert.That(result.Palette.Length, Is.EqualTo(16 * 3));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid1Plane_ParsesCorrectly() {
    var data = TestHelper.BuildMinimalAcbm(8, 2, 1);
    var result = IffAcbmReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(8));
      Assert.That(result.Height, Is.EqualTo(2));
      Assert.That(result.NumPlanes, Is.EqualTo(1));
      Assert.That(result.PixelData.Length, Is.EqualTo(8 * 2));
      Assert.That(result.Palette.Length, Is.EqualTo(2 * 3));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid_ParsesCorrectly() {
    var data = TestHelper.BuildMinimalAcbm(8, 2, 2);
    using var stream = new MemoryStream(data);
    var result = IffAcbmReader.FromStream(stream);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(8));
      Assert.That(result.Height, Is.EqualTo(2));
      Assert.That(result.NumPlanes, Is.EqualTo(2));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelDataPreserved() {
    var original = TestHelper.CreateTestFile(16, 4, 4);
    var data = IffAcbmWriter.ToBytes(original);
    var result = IffAcbmReader.FromBytes(data);

    Assert.That(result.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_BmhdFieldsParsed() {
    var original = new IffAcbmFile {
      Width = 32,
      Height = 16,
      NumPlanes = 4,
      PixelData = new byte[32 * 16],
      Palette = new byte[16 * 3],
      XAspect = 10,
      YAspect = 11,
      PageWidth = 320,
      PageHeight = 200,
      TransparentColor = 5,
    };

    var data = IffAcbmWriter.ToBytes(original);
    var result = IffAcbmReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.XAspect, Is.EqualTo(10));
      Assert.That(result.YAspect, Is.EqualTo(11));
      Assert.That(result.PageWidth, Is.EqualTo(320));
      Assert.That(result.PageHeight, Is.EqualTo(200));
      Assert.That(result.TransparentColor, Is.EqualTo(5));
    });
  }

  private static void _WriteInt32BE(Stream stream, int value) {
    Span<byte> buffer = stackalloc byte[4];
    BinaryPrimitives.WriteInt32BigEndian(buffer, value);
    stream.Write(buffer);
  }
}
