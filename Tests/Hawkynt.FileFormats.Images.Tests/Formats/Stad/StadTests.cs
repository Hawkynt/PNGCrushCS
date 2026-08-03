using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Stad;

namespace FileFormat.Stad.Tests;

[TestFixture]
public sealed class StadReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => StadReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pac"));
    Assert.Throws<FileNotFoundException>(() => StadReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => StadReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => StadReader.FromBytes(new byte[2]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RawUncompressed_Parses() {
    var data = new byte[32000];
    data[0] = 0xAB;
    data[31999] = 0xCD;

    var result = StadReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(640));
    Assert.That(result.Height, Is.EqualTo(400));
    Assert.That(result.RawData.Length, Is.EqualTo(32000));
    Assert.That(result.RawData[0], Is.EqualTo(0xAB));
    Assert.That(result.RawData[31999], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ReadsBothEscapesAndTheSevenByteHeader() {
    // This used to hand-build a PackBits stream and assert what the reader made of it, so it agreed
    // with an encoding STAD does not use. The header is seven bytes: the magic, an escape and the one
    // value it repeats, then a second escape carrying a value of its own. Both counts are one less
    // than the run.
    var compressed = new byte[] {
      (byte)'p', (byte)'M', (byte)'8', (byte)'5',
      0x01, 0xFF, 0x02,
      0x01, 0x03,       // four bytes of 0xFF
      0xAA,             // one taken as it stands
      0x02, 0xBB, 0x01, // two bytes of 0xBB
    };

    var result = StadReader.FromBytes(compressed);

    Assert.That(result.Width, Is.EqualTo(640));
    Assert.That(result.Height, Is.EqualTo(400));
    Assert.That(result.RawData[..7], Is.EqualTo(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xAA, 0xBB, 0xBB }));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PutsAPm86ScreenBackIntoRows() {
    // pM86 stores the screen a byte-column at a time. Marking the first two bytes of the stream puts
    // them at the start of the first two rows once transposed, 80 bytes apart, rather than side by
    // side as pM85 would.
    var compressed = new byte[] {
      (byte)'p', (byte)'M', (byte)'8', (byte)'6',
      0x01, 0x00, 0x02,
      0xAA, 0xBB,
    };

    var result = StadReader.FromBytes(compressed);

    Assert.That(result.RawData[0], Is.EqualTo(0xAA));
    Assert.That(result.RawData[80], Is.EqualTo(0xBB));
    Assert.That(result.RawData[1], Is.EqualTo(0x00));
  }

}

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_WriteThenRead_PreservesData() {
    var rawData = new byte[32000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new StadFile { RawData = rawData };

    var bytes = StadWriter.ToBytes(original);
    var restored = StadReader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new StadFile { RawData = new byte[32000] };

    var bytes = StadWriter.ToBytes(original);
    var restored = StadReader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var rawData = new byte[32000];
    rawData[0] = 0xFF;
    rawData[31999] = 0xAA;
    var original = new StadFile { RawData = rawData };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pac");
    try {
      File.WriteAllBytes(tempPath, StadWriter.ToBytes(original));
      var restored = StadReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.RawData, Is.EqualTo(original.RawData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}

