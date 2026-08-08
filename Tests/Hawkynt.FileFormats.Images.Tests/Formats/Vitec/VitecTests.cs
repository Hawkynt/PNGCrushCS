using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.Vitec;

namespace FileFormat.Vitec.Tests;

[TestFixture]
public sealed class VitecTests {

  private const int _FirstHeader = 120;
  private const int _SecondHeader = 144;

  /// <summary>A file laid out the way the sample is: two headers, each counting its own length.</summary>
  private static byte[] _File(int width, int height, int samples, int? statedData = null, int extra = 0) {
    var pixels = width * height * samples;
    var data = new byte[4 + _FirstHeader + _SecondHeader + pixels + extra];

    VitecFile.Magic.CopyTo(data);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), _FirstHeader);
    VitecFile.Name.CopyTo(data.AsSpan(VitecFile.NameOffset));

    var secondAt = 4 + _FirstHeader;
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(secondAt), _SecondHeader);

    var fields = secondAt + 4;
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(fields), (uint)(statedData ?? _SecondHeader + pixels));
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(fields + 32), (uint)width);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(fields + 36), (uint)height);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(fields + 52), (uint)samples);

    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => VitecReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongMagic_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => VitecReader.FromBytes(new byte[512]));

  [Test]
  [Category("Unit")]
  public void FromBytes_MissingTheNameString_ThrowsInvalidDataException() {
    var data = _File(8, 4, 1);
    data[VitecFile.NameOffset] = (byte)'X';

    Assert.Throws<InvalidDataException>(() => VitecReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TheHeadersAndSamplesMustBeTheWholeFile()
    => Assert.Throws<InvalidDataException>(() => VitecReader.FromBytes(_File(8, 4, 1, extra: 1)));

  [Test]
  [Category("Unit")]
  public void FromBytes_TheStatedDataSizeMustAgreeWithTheStatedShape()
    => Assert.Throws<InvalidDataException>(() => VitecReader.FromBytes(_File(8, 4, 1, statedData: 99)));

  [Test]
  [Category("Unit")]
  public void FromBytes_AnUnsupportedSampleCount_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => VitecReader.FromBytes(_File(8, 4, 2)));

  [Test]
  [Category("Unit")]
  public void FromBytes_OneSampleIsGrey() {
    var decoded = VitecFile.ToRawImage(VitecReader.FromBytes(_File(16, 9, 1)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(16));
      Assert.That(decoded.Height, Is.EqualTo(9));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Gray8));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ThreeSamplesAreColour() {
    var decoded = VitecFile.ToRawImage(VitecReader.FromBytes(_File(16, 9, 3)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(16));
      Assert.That(decoded.Height, Is.EqualTo(9));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(decoded.PixelData, Has.Length.EqualTo(16 * 9 * 3));
    });
  }
}
