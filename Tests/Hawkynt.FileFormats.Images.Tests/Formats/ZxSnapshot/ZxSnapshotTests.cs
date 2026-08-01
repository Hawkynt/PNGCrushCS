using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.ZxSnapshot.Tests;

/// <summary>A Spectrum snapshot, whose screen is the first 6912 bytes of its memory.</summary>
[TestFixture]
public sealed class ZxSnapshotTests {

  private static byte[] _Snapshot(int length) {
    var data = new byte[length];

    // Border blue, and a screen of alternating cell rows on a bright white ink.
    data[ZxSnapshotFile.BorderOffset] = 0x0A;
    for (var i = 0; i < 6144; ++i)
      data[ZxSnapshotFile.HeaderSize + i] = (byte)(i / 32 % 2 == 0 ? 0xAA : 0x55);

    for (var i = 0; i < 768; ++i)
      data[ZxSnapshotFile.HeaderSize + 6144 + i] = 0x47;

    return data;
  }

  [TestCase(ZxSnapshotFile.ShortFileSize)]
  [TestCase(ZxSnapshotFile.LongFileSize)]
  [TestCase(ZxSnapshotFile.LongerFileSize)]
  [Category("Unit")]
  public void Read_TakesEveryLengthASnapshotComesIn(int length) {
    var file = ZxSnapshotReader.FromBytes(_Snapshot(length));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(256));
      Assert.That(file.Height, Is.EqualTo(192));
      Assert.That(file.Screen, Has.Length.EqualTo(6912));
    });
  }

  [TestCase(6912)]
  [TestCase(49178)]
  [TestCase(49180)]
  [Category("Unit")]
  public void Read_RefusesALengthNoSnapshotHas(int length)
    => Assert.Throws<InvalidDataException>(() => ZxSnapshotReader.FromBytes(new byte[length]));

  [Test]
  [Category("Unit")]
  public void Read_TakesTheBorderFromTheRegistersRatherThanTheScreen() {
    var file = ZxSnapshotReader.FromBytes(_Snapshot(ZxSnapshotFile.ShortFileSize));
    Assert.That(file.BorderColor, Is.EqualTo(2), "only the low three bits reach the border");
  }

  [Test]
  [Category("Unit")]
  public void Read_TakesTheScreenFromTheStartOfMemoryAndNotOfTheFile() {
    var data = _Snapshot(ZxSnapshotFile.ShortFileSize);
    // A byte inside the register block must not appear in the screen.
    data[0] = 0xFF;

    var file = ZxSnapshotReader.FromBytes(data);
    Assert.That(file.Screen[0], Is.EqualTo(0xAA));
  }

  [Test]
  [Category("Integration")]
  public void Decoded_DrawsTheSameScreenABareOneWould() {
    var data = _Snapshot(ZxSnapshotFile.ShortFileSize);
    var fromSnapshot = ZxSnapshotFile.ToRawImage(ZxSnapshotReader.FromBytes(data));

    var bare = data.AsSpan(ZxSnapshotFile.HeaderSize, ZxSnapshotFile.ScreenSize).ToArray();
    var fromScreen = FileFormat.ZxSpectrum.ZxSpectrumFile.ToRawImage(
      FileFormat.ZxSpectrum.ZxSpectrumReader.FromBytes(bare));

    Assert.That(fromSnapshot.PixelData, Is.EqualTo(fromScreen.PixelData));
  }
}
