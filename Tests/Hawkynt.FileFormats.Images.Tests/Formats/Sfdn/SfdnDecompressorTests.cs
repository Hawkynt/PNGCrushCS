using System;
using FileFormat.Core;

namespace FileFormat.Core.Tests.Sfdn;

[TestFixture]
public sealed class SfdnDecompressorTests {

  /// <summary>Builds a stream whose nibbles step down by <paramref name="step"/> each time.</summary>
  private static byte[] _Stream(int unpackedLength, byte start, byte step) {
    var data = new byte[SfdnDecompressor.DataOffset + (unpackedLength >> 1) + 16];
    SfdnDecompressor.Magic.CopyTo(data);
    data[4] = (byte)unpackedLength;
    data[5] = (byte)(unpackedLength >> 8);
    data[SfdnDecompressor.TableOffset] = step;
    data[SfdnDecompressor.DataOffset] = (byte)(start << 4);

    return data;
  }

  [Test]
  public void RecognisesItsOwnHeader() {
    Assert.Multiple(() => {
      Assert.That(SfdnDecompressor.IsSfdn(_Stream(16, 5, 1)), Is.True);
      Assert.That(SfdnDecompressor.IsSfdn("S102"u8.ToArray()), Is.False);
      Assert.That(SfdnDecompressor.IsSfdn([]), Is.False);
    });
  }

  [Test]
  public void EachNibbleIsTheDistanceFromTheLast() {
    var unpacked = SfdnDecompressor.TryUnpack(_Stream(4, 5, 1), 4);

    // Starting at 5 and stepping down one: 5,4 then 3,2 then 1,0 then 15,14 — the wrap included.
    Assert.That(unpacked, Is.EqualTo(new byte[] { 0x54, 0x32, 0x10, 0xFE }));
  }

  [Test]
  public void AZeroDistanceRepeatsTheNibble() {
    var unpacked = SfdnDecompressor.TryUnpack(_Stream(3, 7, 0), 3);

    Assert.That(unpacked, Is.EqualTo(new byte[] { 0x77, 0x77, 0x77 }));
  }

  [Test]
  public void RejectsALengthTheHeaderDisagreesWith() {
    Assert.That(SfdnDecompressor.TryUnpack(_Stream(16, 5, 1), 32), Is.Null);
  }

  [Test]
  public void RejectsDataTooShortToHoldThePicture() {
    var data = _Stream(4096, 5, 1)[..64];

    Assert.That(SfdnDecompressor.TryUnpack(data, 4096), Is.Null);
  }

  [Test]
  public void RejectsSomethingThatIsNotPackedAtAll() {
    Assert.That(SfdnDecompressor.TryUnpack(new byte[1024], 512), Is.Null);
  }

  [Test]
  public void ReportsTheLengthItsHeaderClaims() {
    Assert.Multiple(() => {
      Assert.That(SfdnDecompressor.UnpackedLength(_Stream(7680, 0, 0)), Is.EqualTo(7680));
      Assert.That(SfdnDecompressor.UnpackedLength(new byte[64]), Is.EqualTo(-1));
    });
  }
}
