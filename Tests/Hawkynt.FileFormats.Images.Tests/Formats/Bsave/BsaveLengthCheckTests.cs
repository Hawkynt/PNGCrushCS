using System;
using System.IO;
using FileFormat.Bsave;

namespace FileFormat.Bsave.Tests;

/// <summary>
/// A BSAVE file states how long its block is, and a real one carries exactly that.
/// </summary>
/// <remarks>
/// The signature is a single byte, 0xFD, which is a loader marker rather than a format's name.
/// Checking only that byte claimed every file that happened to begin with it — among them a VBXE
/// slide show whose first dozen bytes are all 0xFD, which then came back 320 by 200 instead of the
/// 320 by 240 it holds, and the format that could have read it never saw it.
/// <para/>
/// The header also states the block's length, and that is what tells one apart: the real sample says
/// 7836 and carries 7836, while the slide show's bytes read as a length of 65021 against 77561
/// carried.
/// <para/>
/// With the length honoured the slide show matches RECOIL on every pixel.
/// </remarks>
[TestFixture]
public sealed class BsaveLengthCheckTests {

  /// <summary>Builds a header saying the block is the given length, followed by that many bytes.</summary>
  private static byte[] _Build(int stated, int carried) {
    var data = new byte[7 + carried];
    data[0] = 0xFD;
    data[1] = 0x61; data[2] = 0x79;          // segment
    data[5] = (byte)stated; data[6] = (byte)(stated >> 8);
    return data;
  }

  [Test]
  [Category("Unit")]
  public void ABlockThatCarriesWhatItStatesIsRead() {
    var file = BsaveReader.FromBytes(_Build(7836, 7836));

    Assert.That(file.PixelData, Has.Length.EqualTo(7836));
  }

  [Test]
  [Category("Unit")]
  public void ABlockCarryingFarMoreThanItStatesIsRefused() {
    // This is the slide show's shape: a length read out of picture data, and a much larger file.
    Assert.Throws<InvalidDataException>(() => BsaveReader.FromBytes(_Build(65021, 77561)));
  }

  [Test]
  [Category("Unit")]
  public void ABlockCarryingLessThanItStatesIsRefused()
    => Assert.Throws<InvalidDataException>(() => BsaveReader.FromBytes(_Build(7836, 4000)));

  [Test]
  [Category("Unit")]
  public void AShortTrailerIsTolerated() {
    // A few bytes after the block are common enough and do not make the file something else.
    Assert.DoesNotThrow(() => BsaveReader.FromBytes(_Build(7836, 7840)));
  }

  [Test]
  [Category("Unit")]
  public void SomethingThatMerelyBeginsWithTheMarkerIsRefused() {
    var data = new byte[4096];
    Array.Fill(data, (byte)0xFD);

    Assert.Throws<InvalidDataException>(() => BsaveReader.FromBytes(data));
  }
}
