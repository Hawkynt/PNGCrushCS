using System;
using System.Text;
using FileFormat.Core;

namespace FileFormat.CrackArt.Tests;

/// <summary>
/// The run-length coding CrackArt actually uses.
/// </summary>
/// <remarks>
/// What was implemented was PackBits, which this is not, so every packed picture came out as noise
/// while reporting no trouble. A packed CrackArt picture names its own escape byte and then runs
/// plainly: a byte that is not the escape stands for itself, and the escape introduces a count and
/// a value.
/// <para/>
/// The count is stored one less than it means. Reading it as written leaves every run a byte short
/// and the picture drifts sideways from the first run onwards — which is what made the sample decode
/// correctly for its first 7578 bytes and wrongly thereafter.
/// <para/>
/// The stream stops when there is nothing more to say and the rest of the screen stays blank.
/// Checked against RECOIL: all three samples come back identical.
/// </remarks>
[TestFixture]
public sealed class CrackArtCodecTests {

  private static byte[] _Stream(byte escape, params byte[] rest) {
    var data = new byte[4 + rest.Length];
    data[0] = escape;
    data[3] = 1;
    rest.CopyTo(data, 4);
    return data;
  }

  [Test]
  [Category("Unit")]
  public void Decompress_TakesAByteThatIsNotTheEscapeForItself() {
    var screen = CrackArtCompressor.Decompress(_Stream(0x03, 0x11, 0x22, 0x33), 8);

    Assert.That(screen[..4], Is.EqualTo(new byte[] { 0x11, 0x22, 0x33, 0x00 }));
  }

  [Test]
  [Category("Unit")]
  public void Decompress_ReadsARunAsOneMoreThanTheCountSays() {
    // The count byte is 16 and the run is seventeen bytes long.
    var screen = CrackArtCompressor.Decompress(_Stream(0x03, 0x03, 0x10, 0x00, 0xAA), 32);

    Assert.Multiple(() => {
      for (var i = 0; i < 17; ++i)
        Assert.That(screen[i], Is.Zero, $"byte {i} belongs to the run");

      Assert.That(screen[17], Is.EqualTo(0xAA), "the byte after the run is the next literal");
    });
  }

  [Test]
  [Category("Unit")]
  public void Decompress_LeavesTheRestOfTheScreenBlankWhenTheStreamStops() {
    var screen = CrackArtCompressor.Decompress(_Stream(0x03, 0x11), 100);

    Assert.Multiple(() => {
      Assert.That(screen, Has.Length.EqualTo(100));
      for (var i = 1; i < 100; ++i)
        Assert.That(screen[i], Is.Zero, $"byte {i}");
    });
  }

  [Test]
  [Category("Unit")]
  public void Decompress_TakesAStreamShorterThanItsOwnPreamble()
    => Assert.That(CrackArtCompressor.Decompress([0x03], 16), Has.Length.EqualTo(16));

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsAScreenOfLongRuns() {
    var original = new byte[32000];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i / 700);

    var packed = CrackArtCompressor.Compress(original);

    Assert.Multiple(() => {
      Assert.That(CrackArtCompressor.Decompress(packed, 32000), Is.EqualTo(original));
      Assert.That(packed, Has.Length.LessThan(32000), "runs are what the coding is for");
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsAScreenThatCannotBeShortened() {
    var original = new byte[32000];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 37 % 251);

    Assert.That(CrackArtCompressor.Decompress(CrackArtCompressor.Compress(original), 32000), Is.EqualTo(original));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsAScreenContainingWhateverByteBecameTheEscape() {
    // Every value appears, so the escape it picks is bound to occur in the data as well.
    var original = new byte[32000];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)i;

    Assert.That(CrackArtCompressor.Decompress(CrackArtCompressor.Compress(original), 32000), Is.EqualTo(original));
  }
}
