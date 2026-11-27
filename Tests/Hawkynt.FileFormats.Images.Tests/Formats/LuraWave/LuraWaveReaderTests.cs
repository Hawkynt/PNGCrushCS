using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace FileFormat.LuraWave.Tests;

[TestFixture]
public sealed class LuraWaveReaderTests {

  private static byte[] _Stripes() => LuraWaveFixtures.Bytes("mode06-stripes.lwf");

  /// <summary>Offset of the body of the first chunk with the given tag.</summary>
  private static int _ChunkBody(byte[] data, string tag) {
    var position = 4;
    while (position + 4 <= data.Length) {
      var length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(position + 2));
      if (data[position] == tag[0] && data[position + 1] == tag[1])
        return position + 4;
      position += length;
    }

    throw new InvalidOperationException($"No '{tag}' chunk.");
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => LuraWaveReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => LuraWaveReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".lwf"));
    Assert.Throws<FileNotFoundException>(() => LuraWaveReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ReadsTheSameAsFromBytes() {
    using var stream = new MemoryStream(_Stripes());
    Assert.That(LuraWaveReader.FromStream(stream).PixelData, Is.EqualTo(LuraWaveReader.FromBytes(_Stripes()).PixelData));
  }

  [Test]
  [Category("Unit")]
  public void HeaderIsReported() {
    var header = LuraWaveReader.FromBytes(_Stripes()).Header!;
    Assert.Multiple(() => {
      Assert.That(header.Mode, Is.EqualTo(LuraWaveColourMode.ReversibleColour));
      Assert.That(header.Width, Is.EqualTo(45));
      Assert.That(header.Height, Is.EqualTo(37));
      Assert.That(header.CodedWidth, Is.EqualTo(48));
      Assert.That(header.StripeHeight, Is.EqualTo(16));
      Assert.That(header.StripeCount, Is.EqualTo(3));
      Assert.That(header.Levels, Is.EqualTo(4));
      Assert.That(header.IsEmbedded, Is.False);
      Assert.That(header.StripeSizes, Has.Length.EqualTo(3));
      Assert.That(header.DataOffset % 4, Is.Zero);
    });
  }

  [Test]
  [Category("Unit")]
  public void InterestRegionIsReported() {
    var header = LuraWaveReader.FromBytes(LuraWaveFixtures.Bytes("mode06-region.lwf")).Header!;
    Assert.Multiple(() => {
      Assert.That(header.Regions, Is.EqualTo(new[] { new LuraWaveRegion(5, 6, 25, 20) }));
      Assert.That(header.RegionShift, Is.EqualTo(3));
    });
  }

  [Test]
  [Category("Unit")]
  public void PasswordLimitIsReported()
    => Assert.That(LuraWaveReader.FromBytes(LuraWaveFixtures.Bytes("mode06-stripes-password.lwf")).Header!.PublicQuality, Is.EqualTo(300));

  [TestCase("mode01-tail.lwf", 8, TestName = "Grey")]
  [TestCase("mode17-widegrey-12bit.lwf", 16, TestName = "Wide grey")]
  [TestCase("mode06-stripes.lwf", 24, TestName = "Colour in stripes")]
  [Category("Unit")]
  public void ReadImageInfo_AgreesWithAFullDecode(string fixture, int bits) {
    var data = LuraWaveFixtures.Bytes(fixture);
    var header = LuraWaveHeader.Parse(data);
    var info = LuraWaveFile.ReadImageInfo(data.AsSpan(0, header.DataOffset));
    var image = LuraWaveFile.ToRawImage(LuraWaveReader.FromBytes(data));
    Assert.That(info, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(info!.Value.Width, Is.EqualTo(image.Width));
      Assert.That(info.Value.Height, Is.EqualTo(image.Height));
      Assert.That(info.Value.BitsPerPixel, Is.EqualTo(bits));
      Assert.That(info.Value.FrameCount, Is.EqualTo(1));
      Assert.That(image.PixelData.Length * 8, Is.EqualTo(image.Width * image.Height * bits));
    });
  }

  [Test]
  [Category("Unit")]
  public void ReadImageInfo_NotLuraWave_ReturnsNull() {
    Assert.Multiple(() => {
      Assert.That(LuraWaveFile.ReadImageInfo([0x89, 0x50, 0x4E, 0x47]), Is.Null);
      Assert.That(LuraWaveFile.ReadImageInfo(_Stripes().AsSpan(0, 20)), Is.Null);
    });
  }

  [Test]
  [Category("Unit")]
  public void IsDetectedBySignature() => Assert.That(FormatRegistry.DetectFromBytes(_Stripes()), Is.EqualTo(ImageFormat.LuraWave));

  [TestCase(ImageFormat.Png)]
  [TestCase(ImageFormat.Bmp)]
  [TestCase(ImageFormat.Jpeg)]
  [Category("Unit")]
  public void OtherFormatsRenamedToLwfAreRefused(ImageFormat other) {
    var picture = new RawImage { Width = 8, Height = 8, Format = PixelFormat.Rgb24, PixelData = new byte[8 * 8 * 3] };
    var bytes = FormatRegistry.Write(picture, other)!;
    Assert.Multiple(() => {
      Assert.That(FormatRegistry.DetectFromBytes(bytes), Is.Not.EqualTo(ImageFormat.LuraWave));
      Assert.That(LuraWaveFile.ReadImageInfo(bytes), Is.Null);
      Assert.Throws<InvalidDataException>(() => LuraWaveReader.FromBytes(bytes));
    });
  }

  [Test]
  [Category("Unit")]
  public void WrongVersion_IsRefused() {
    var data = _Stripes();
    data[2] = 1;
    Assert.That(() => LuraWaveReader.FromBytes(data), Throws.InstanceOf<InvalidDataException>().With.Message.Contains("version"));
  }

  [TestCase(3, TestName = "Cut inside the header")]
  [TestCase(40, TestName = "Cut before the data chunk")]
  [Category("Unit")]
  public void TruncatedHeader_IsRefused(int length)
    => Assert.Throws<InvalidDataException>(() => LuraWaveReader.FromBytes(_Stripes()[..length]));

  [Test]
  [Category("Unit")]
  public void TruncatedData_IsRefused() {
    var data = _Stripes();
    Assert.That(() => LuraWaveReader.FromBytes(data[..(data.Length - 100)]), Throws.InstanceOf<InvalidDataException>().With.Message.Contains("truncated"));
  }

  [Test]
  [Category("Unit")]
  public void MissingStripeTerminator_IsRefused() {
    // Without the stripe table the size check cannot fire; the missing terminator before stripe 2 still does.
    var data = _Stripes();
    var stripes = _ChunkBody(data, "ST");
    data[stripes - 4] = (byte)'X';
    var firstStripe = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(stripes));
    var header = LuraWaveHeader.Parse(data);
    Assert.Throws<InvalidDataException>(() => LuraWaveReader.FromBytes(data[..(header.DataOffset + firstStripe)]));
  }

  [Test]
  [Category("Unit")]
  public void UnknownColourMode_IsRefused() {
    var data = _Stripes();
    data[_ChunkBody(data, "GE") + 8] = 3;
    Assert.That(() => LuraWaveReader.FromBytes(data), Throws.InstanceOf<InvalidDataException>().With.Message.Contains("colour mode"));
  }

  [Test]
  [Category("Unit")]
  public void OversizedDimensions_AreRefusedBeforeAllocating() {
    var data = _Stripes();
    var geometry = _ChunkBody(data, "GE");
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(geometry), 0x7FFF0000);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(geometry + 4), 0x7FFF0000);
    Assert.Throws<InvalidDataException>(() => LuraWaveReader.FromBytes(data));
  }

  [TestCase(1, TestName = "One level")]
  [TestCase(16, TestName = "Sixteen levels")]
  [Category("Unit")]
  public void UnsupportedLevelCount_IsRefused(int levels) {
    var data = _Stripes();
    data[_ChunkBody(data, "CB") + 4] = (byte)levels;
    Assert.Throws<InvalidDataException>(() => LuraWaveReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void StripesOfAnotherHeightThanTheLevelBlock_AreRefused() {
    var data = _Stripes();
    data[_ChunkBody(data, "CB") + 4] = 3;
    Assert.That(() => LuraWaveReader.FromBytes(data), Throws.InstanceOf<InvalidDataException>().With.Message.Contains("stripe"));
  }

  [Test]
  [Category("Unit")]
  public void ChunkOverrunningTheFile_IsRefused() {
    var data = _Stripes();
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(_ChunkBody(data, "GE") - 2), 0xFFF0);
    Assert.Throws<InvalidDataException>(() => LuraWaveReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void MoreThanOneInterestRegion_IsRefused() {
    var header = LuraWaveHeader.Parse(LuraWaveFixtures.Bytes("mode06-region.lwf")) with {
      Regions = [new(0, 0, 4, 4), new(8, 8, 4, 4)],
    };
    var data = header.Serialize([0xFF, 0xFF, 0xFF, 0xFF]);
    Assert.That(() => LuraWaveHeader.Parse(data), Throws.InstanceOf<InvalidDataException>().With.Message.Contains("region"));
  }

  [TestCase("mode06-stripes.lwf", TestName = "Striped")]
  [TestCase("mode08-polynomial.lwf", TestName = "Embedded")]
  [Category("Unit")]
  public void CorruptedData_DecodesOrIsRefused_NeverCrashes(string fixture) {
    // The coding carries no checksum: damaged bits change the picture, or lose a stripe terminator, but must never
    // throw anything else or overrun.
    var data = LuraWaveFixtures.Bytes(fixture);
    var header = LuraWaveHeader.Parse(data);
    var random = new Random(42);
    for (var i = header.DataOffset; i < data.Length; i += 7)
      data[i] = (byte)random.Next(256);

    try {
      var image = LuraWaveReader.FromBytes(data);
      Assert.That(image.PixelData, Has.Length.EqualTo(45 * 37 * 3));
    } catch (InvalidDataException) {
      Assert.Pass("Refused.");
    }
  }
}
