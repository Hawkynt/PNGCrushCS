using System;
using FileFormat.JpegXl.Codec;
using NUnit.Framework;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// What a frame that states nothing is asking for.
/// </summary>
/// <remarks>
/// A frame header may be one bit long, meaning every field takes its default —
/// and that includes the nested bundles, of which the loop filter is one. Its
/// own defaults are the smoothing filter on and two passes of the
/// edge-preserving one. Reading the short header as "no filtering stated" and
/// therefore "no filtering" turns the frame that asks for the most into the
/// frame that asks for none, and a picture assembled without the smoothing
/// filter is nine times further from libjxl than one with it.
/// </remarks>
[TestFixture]
internal sealed class JxlFrameDefaultsTests {

  /// <summary>A header of a single set bit: everything default.</summary>
  [Test]
  public void AFrameThatStatesNothingStillAsksForBothFilters() {
    var reader = new JxlBitReader([0x01], 0);

    var frame = JxlSpecFrameHeader.Decode(reader);

    Assert.Multiple(() => {
      Assert.That(frame.AllDefault, Is.True);
      Assert.That(reader.BitsRead, Is.EqualTo(1), "a frame that states nothing spends one bit saying so");
      Assert.That(frame.GaborishParameters, Is.Not.Null, "the smoothing filter");
      Assert.That(frame.GaborishParameters!.Enabled, Is.True);
      Assert.That(frame.EpfParameters, Is.Not.Null, "and the edge-preserving one");
      Assert.That(frame.EpfParameters!.Iters, Is.EqualTo(2), "two passes of it");
    });
  }

  /// <summary>And so does a frame whose header is stated but whose loop filter
  /// is not.</summary>
  [Test]
  public void AStatedFrameWithAnUnstatedLoopFilterAsksForBothToo() {
    // all_default = 0, then the fields of a plain VarDCT frame, ending with the
    // loop filter's own all_default = 1. Taken from a file cjxl wrote.
    var reader = new JxlBitReader([0x00, 0x13, 0x88, 0x02, 0x00], 0);

    var frame = JxlSpecFrameHeader.Decode(reader);

    Assert.Multiple(() => {
      Assert.That(frame.AllDefault, Is.False);
      Assert.That(frame.Encoding, Is.EqualTo(JxlFrameEncoding.VarDct));
      Assert.That(frame.GaborishParameters?.Enabled, Is.True);
      Assert.That(frame.EpfParameters?.Iters, Is.EqualTo(1), "this one states one pass rather than taking the default");
    });
  }
}
