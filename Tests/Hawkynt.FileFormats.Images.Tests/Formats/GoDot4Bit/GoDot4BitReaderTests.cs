using System;
using System.IO;
using FileFormat.GoDot4Bit;

namespace FileFormat.GoDot4Bit.Tests;

/// <summary>
/// What a GoDot file is.
/// </summary>
/// <remarks>
/// These used to describe 16384 raw bytes at a fixed 160 by 200 with no signature and no packing,
/// because that is what the reader expected. No GoDot file is anything like it — they open with
/// "GOD0" or "GOD1", are packed, and are 320 wide or whatever size a clip states.
/// </remarks>
[TestFixture]
public sealed class GoDot4BitReaderTests {

  /// <summary>Builds a whole screen, packed, with every byte the same.</summary>
  private static byte[] _BuildScreen(byte fill) {
    var output = new System.Collections.Generic.List<byte>();
    foreach (var b in GoDot4BitFile.ScreenMagic)
      output.Add(b);

    // 32000 bytes of the same value, in runs of 250 so no count reaches the 256 that means a full run.
    for (var written = 0; written < 32000; written += 250) {
      output.Add(GoDot4BitFile.RunEscape);
      output.Add(250);
      output.Add(fill);
    }

    return output.ToArray();
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GoDot4BitReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GoDot4BitReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".4bt"));
    Assert.Throws<FileNotFoundException>(() => GoDot4BitReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GoDot4BitReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => GoDot4BitReader.FromBytes(new byte[3]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithoutASignature_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => GoDot4BitReader.FromBytes(new byte[16385]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RunningOutOfPixels_ThrowsInvalidDataException() {
    var truncated = _BuildScreen(0x77)[..100];
    Assert.Throws<InvalidDataException>(() => GoDot4BitReader.FromBytes(truncated));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AWholeScreenIs320By200() {
    var result = GoDot4BitReader.FromBytes(_BuildScreen(0x12));

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.IsClip, Is.False);
      Assert.That(result.PixelData.Length, Is.EqualTo(320 * 200 / 2));
      Assert.That(result.PixelData[0], Is.EqualTo(0x12));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AClipStatesItsSizeInCharacterCells() {
    // "GOD1", two bytes of where it was cut from, then 13 cells across and 11 down.
    var output = new System.Collections.Generic.List<byte> { (byte)'G', (byte)'O', (byte)'D', (byte)'1', 1, 1, 13, 11 };
    for (var written = 0; written < 13 * 8 * 11 * 8 / 2; written += 100) {
      output.Add(GoDot4BitFile.RunEscape);
      output.Add(100);
      output.Add(0x34);
    }

    var result = GoDot4BitReader.FromBytes(output.ToArray());

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(104));
      Assert.That(result.Height, Is.EqualTo(88));
      Assert.That(result.IsClip, Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ACountOfNoughtStandsFor256() {
    // Four cells by two is 32 by 16 pixels, which is exactly the 256 bytes one such run fills.
    var output = new System.Collections.Generic.List<byte> { (byte)'G', (byte)'O', (byte)'D', (byte)'1', 0, 0, 4, 2 };
    output.Add(GoDot4BitFile.RunEscape);
    output.Add(0);
    output.Add(0x5A);

    var result = GoDot4BitReader.FromBytes(output.ToArray());

    Assert.Multiple(() => {
      Assert.That(result.PixelData.Length, Is.EqualTo(256));
      Assert.That(result.PixelData[255], Is.EqualTo(0x5A));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidParsesCorrectly() {
    using var ms = new MemoryStream(_BuildScreen(0xAB));
    var result = GoDot4BitReader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllBytesPreserved() {
    var pixelData = new byte[320 * 200 / 2];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 3 % 256);

    var original = new GoDot4BitFile {
      Width = 320,
      Height = 200,
      PixelData = pixelData,
    };

    var restored = GoDot4BitReader.FromBytes(GoDot4BitWriter.ToBytes(original));

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
