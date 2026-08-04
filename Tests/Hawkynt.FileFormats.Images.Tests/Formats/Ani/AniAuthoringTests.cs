using System;
using System.Text;
using FileFormat.Ani;
using FileFormat.Core;

namespace FileFormat.Ani.Tests;

/// <summary>
/// Building an animated cursor from a single picture.
/// </summary>
/// <remarks>
/// Nothing installed here reads an animated cursor — IrfanView turns down the ones in the corpus as
/// readily as ours — so this is checked the only two ways left: the container is walked chunk by
/// chunk and its size field must close on the file, and the frame is pulled back out and handed to
/// the icon reader, which ImageMagick and IrfanView both agree with.
/// </remarks>
[TestFixture]
public class AniAuthoringTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 4];
    for (var i = 0; i < width * height; ++i) {
      pixels[i * 4] = (byte)(i & 0xFF);
      pixels[i * 4 + 1] = 0x30;
      pixels[i * 4 + 2] = 0x90;
      pixels[i * 4 + 3] = i == 0 ? (byte)0 : (byte)255;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Bgra32, PixelData = pixels };
  }

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => AniFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_MakesOneFrameAndOneStep() {
    var ani = AniFile.FromRawImage(_Picture(32, 32));

    Assert.Multiple(() => {
      Assert.That(ani.Frames, Has.Count.EqualTo(1));
      Assert.That(ani.Header.NumFrames, Is.EqualTo(1));
      Assert.That(ani.Header.NumSteps, Is.EqualTo(1));
      Assert.That(ani.Header.Width, Is.EqualTo(32));
      Assert.That(ani.Header.Height, Is.EqualTo(32));
      Assert.That(ani.Header.BitCount, Is.EqualTo(32));
    });
  }

  [Test]
  public void ToBytes_IsARiffFileOfAnimatedCursors() {
    var bytes = AniWriter.ToBytes(AniFile.FromRawImage(_Picture(16, 16)));

    Assert.Multiple(() => {
      Assert.That(Encoding.ASCII.GetString(bytes, 0, 4), Is.EqualTo("RIFF"));
      Assert.That(Encoding.ASCII.GetString(bytes, 8, 4), Is.EqualTo("ACON"));
    });
  }

  [Test]
  public void ToBytes_SizeFieldClosesOnTheFile() {
    var bytes = AniWriter.ToBytes(AniFile.FromRawImage(_Picture(16, 16)));

    Assert.That(BitConverter.ToUInt32(bytes, 4), Is.EqualTo((uint)(bytes.Length - 8)));
  }

  [Test]
  public void ToBytes_PutsTheHeaderBeforeTheFrames() {
    // The lists used to be written first, which puts the frames ahead of the header saying how many
    // there are. Every real animated cursor states its header first and Windows reads it as one.
    var bytes = AniWriter.ToBytes(AniFile.FromRawImage(_Picture(16, 16)));

    Assert.That(Encoding.ASCII.GetString(bytes, 12, 4), Is.EqualTo("anih"));
  }

  [Test]
  public void ToBytes_EveryChunkLiesInsideTheFile() {
    var bytes = AniWriter.ToBytes(AniFile.FromRawImage(_Picture(16, 16)));

    var at = 12;
    var seen = 0;
    while (at + 8 <= bytes.Length) {
      var size = (int)BitConverter.ToUInt32(bytes, at + 4);
      Assert.That(at + 8 + size, Is.LessThanOrEqualTo(bytes.Length), "a chunk runs past the end");
      at += 8 + size + (size & 1);
      ++seen;
    }

    Assert.Multiple(() => {
      Assert.That(at, Is.EqualTo(bytes.Length), "the chunks account for the whole file");
      Assert.That(seen, Is.EqualTo(2), "a header and a list of frames");
    });
  }

  [Test]
  public void RoundTrip_ThroughBytesKeepsEveryPixelAndItsAlpha() {
    var source = _Picture(16, 16);

    var restored = AniFile.ToRawImage(AniReader.FromBytes(AniWriter.ToBytes(AniFile.FromRawImage(source))));

    Assert.That(restored.ToBgra32(), Is.EqualTo(source.PixelData));
  }
}
