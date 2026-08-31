using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Stad;

namespace FileFormat.Stad.Tests;

[TestFixture]
public sealed class StadReaderTests {

  private const byte Id = 0x01;
  private const byte Pack = 0xFF;
  private const byte Special = 0x02;

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => StadReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pac"));
    Assert.Throws<FileNotFoundException>(() => StadReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => StadReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => StadReader.FromBytes(new byte[2]));

  [Test]
  [Category("Unit")]
  public void FromBytes_RawUncompressedScreen_IsNotMisidentifiedAsStad()
    => Assert.Throws<InvalidDataException>(() => StadReader.FromBytes(new byte[32_000]));

  [Test]
  [Category("Unit")]
  public void FromBytes_ReadsBothEscapesAndTheSevenByteHeader() {
    var compressed = _Header(StadPacking.Horizontal);
    compressed.Add(Id); compressed.Add(0x03);       // four bytes of 0xFF
    compressed.Add(0xAA);                           // one literal
    compressed.Add(Special); compressed.Add(0xBB); compressed.Add(0x01); // two bytes of 0xBB
    _AppendPackRuns(compressed, 32_000 - 7);

    var result = StadReader.FromBytes(compressed.ToArray());

    Assert.That(result.RawData[..7], Is.EqualTo(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xAA, 0xBB, 0xBB }));
    Assert.That(result.Packing, Is.EqualTo(StadPacking.Horizontal));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PutsAPm86ScreenBackIntoRows() {
    var compressed = _Header(StadPacking.Vertical);
    compressed.Add(0xAA);
    compressed.Add(0xBB);
    _AppendPackRuns(compressed, 32_000 - 2);

    var result = StadReader.FromBytes(compressed.ToArray());

    Assert.Multiple(() => {
      Assert.That(result.RawData[0], Is.EqualTo(0xAA));
      Assert.That(result.RawData[80], Is.EqualTo(0xBB));
      Assert.That(result.RawData[1], Is.EqualTo(0xFF));
      Assert.That(result.Packing, Is.EqualTo(StadPacking.Vertical));
    });
  }

  [Test]
  public void FromBytes_TruncatedPackets_AreRejected() {
    var id = _Header(StadPacking.Horizontal); id.Add(Id);
    var special = _Header(StadPacking.Horizontal); special.Add(Special); special.Add(0xAA);
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => StadReader.FromBytes(id.ToArray()));
      Assert.Throws<InvalidDataException>(() => StadReader.FromBytes(special.ToArray()));
    });
  }

  [Test]
  public void FromBytes_UnderflowOverrunAndTrailingData_AreRejected() {
    var underflow = _Header(StadPacking.Horizontal); underflow.Add(0xAA);

    var overrun = _Header(StadPacking.Horizontal);
    _AppendPackRuns(overrun, 31_900);
    overrun.Add(Id); overrun.Add(0xFF);

    var trailing = _Header(StadPacking.Horizontal);
    _AppendPackRuns(trailing, 32_000);
    trailing.Add(0xAA);

    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => StadReader.FromBytes(underflow.ToArray()));
      Assert.Throws<InvalidDataException>(() => StadReader.FromBytes(overrun.ToArray()));
      Assert.Throws<InvalidDataException>(() => StadReader.FromBytes(trailing.ToArray()));
    });
  }

  [Test]
  public void FromBytes_EqualEscapeBytes_AreRejected()
    => Assert.Throws<InvalidDataException>(() => StadReader.FromBytes([(byte)'p', (byte)'M', (byte)'8', (byte)'5', 1, 0, 1]));

  private static List<byte> _Header(StadPacking packing) => [
    (byte)'p', (byte)'M', (byte)'8', packing == StadPacking.Horizontal ? (byte)'5' : (byte)'6', Id, Pack, Special,
  ];

  private static void _AppendPackRuns(List<byte> bytes, int count) {
    while (count > 0) {
      var chunk = Math.Min(256, count);
      bytes.Add(Id);
      bytes.Add((byte)(chunk - 1));
      count -= chunk;
    }
  }
}

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_WriteThenRead_PreservesData() {
    var rawData = new byte[32_000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new StadFile { RawData = rawData };
    var restored = StadReader.FromBytes(StadWriter.ToBytes(original));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ParsedPm86_PreservesPackingAndHeaderParameters() {
    var rawData = new byte[32_000];
    for (var i = 0; i < rawData.Length; i += 80)
      rawData[i] = 0xFF;

    var authored = new StadFile { RawData = rawData, Packing = StadPacking.Vertical };
    var first = StadWriter.ToBytes(authored);
    var parsed = StadReader.FromBytes(first);
    var second = StadWriter.ToBytes(parsed);

    Assert.Multiple(() => {
      Assert.That(second[..7], Is.EqualTo(first[..7]));
      Assert.That(second[3], Is.EqualTo((byte)'6'));
      Assert.That(StadReader.FromBytes(second).RawData, Is.EqualTo(rawData));
    });
  }

  [Test]
  public void Writer_RejectsWrongScreenLength()
    => Assert.Throws<ArgumentException>(() => StadWriter.ToBytes(new StadFile { RawData = [0] }));
}
