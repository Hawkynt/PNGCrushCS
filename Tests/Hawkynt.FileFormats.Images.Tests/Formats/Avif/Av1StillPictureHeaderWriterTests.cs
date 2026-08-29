using FileFormat.Avif.Codec;

namespace FileFormat.Avif.Tests;

[TestFixture]
public sealed class Av1StillPictureHeaderWriterTests {

  [TestCase(1, 1)]
  [TestCase(17, 9)]
  [TestCase(320, 240)]
  [TestCase(1920, 1080)]
  [Category("Unit")]
  public void SequenceHeaderObu_ParsesAsReducedProfile0StillPicture(int width, int height) {
    var bytes = Av1StillPictureHeaderWriter.WriteSequenceHeaderObu(width, height);
    var obu = Av1ObuParser.ParseSingleObu(bytes, 0, bytes.Length);
    var sequence = Av1SequenceHeader.Parse(bytes, obu.PayloadOffset, obu.PayloadSize);

    Assert.Multiple(() => {
      Assert.That(obu.Type, Is.EqualTo(Av1ObuType.SequenceHeader));
      Assert.That(obu.HasExtension, Is.False);
      Assert.That(obu.HasSize, Is.True);
      Assert.That(obu.TotalSize, Is.EqualTo(bytes.Length));
      Assert.That(sequence.SeqProfile, Is.EqualTo(0));
      Assert.That(sequence.StillPicture, Is.True);
      Assert.That(sequence.ReducedStillPictureHeader, Is.True);
      Assert.That(sequence.MaxFrameWidth, Is.EqualTo(width));
      Assert.That(sequence.MaxFrameHeight, Is.EqualTo(height));
      Assert.That(sequence.BitDepth, Is.EqualTo(8));
      Assert.That(sequence.MonoChrome, Is.False);
      Assert.That(sequence.SubsamplingX, Is.EqualTo(1));
      Assert.That(sequence.SubsamplingY, Is.EqualTo(1));
      Assert.That(sequence.ColorRange, Is.True);
      Assert.That(sequence.EnableSuperRes, Is.False);
      Assert.That(sequence.EnableCdef, Is.False);
      Assert.That(sequence.EnableRestoration, Is.False);
    });
  }
}
