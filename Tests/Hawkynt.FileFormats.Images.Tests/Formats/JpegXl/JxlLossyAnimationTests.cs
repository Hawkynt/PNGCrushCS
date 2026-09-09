using System;
using System.Collections.Generic;
using FileFormat.JpegXl.Codec;
using NUnit.Framework;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Animations whose frames are lossy, against what libjxl decodes them to.
/// </summary>
/// <remarks>
/// A lossy frame is coded in XYB, and XYB is not a space two frames can be put
/// together in: libjxl's blending stage refuses a background that is still in it
/// by name. Its pipeline runs the inverse opsin transform and then the transfer
/// curve <em>before</em> it blends (<c>lib/jxl/dec_cache.cc</c>: the XYB stage,
/// then out of linear, then blending), so a lossy frame and a modular one meet
/// in the same space — the colour the picture is stated in.
///
/// <para>These files were written by <c>cjxl</c> 0.12.0 at its own defaults,
/// which is to say lossy, and <c>djxl</c>'s decode of every moment is kept
/// beside each. The comparison adds libjxl's output dither to this decode
/// first: without it the measurement is of the pattern rather than of the
/// decoder, which <see cref="LibjxlLossyParityTests"/> spells out.</para>
/// </remarks>
[TestFixture]
public sealed class JxlLossyAnimationTests {

  /// <param name="name">
  /// <c>cjxl_lossy_animation</c> is a still picture with something moving over
  /// it, so every frame after the first covers only the part that changed and
  /// has to be drawn over the one kept aside. <c>cjxl_lossy_animation_alpha</c>
  /// carries an alpha plane that changes from moment to moment, which a lossy
  /// frame keeps in its global modular stream beside the coefficients.
  /// <c>cjxl_lossy_animation_groups</c> is wider than a group, so its first
  /// frame is coded in two and the later ones land across the seam.
  /// <c>cjxl_lossy_animation_colour</c> is the same arrangement in colour,
  /// which is what makes the colour transform part of what is measured rather
  /// than something a grey picture happens not to exercise.
  /// </param>
  [TestCase("cjxl_lossy_animation", 4, 3)]
  [TestCase("cjxl_lossy_animation_alpha", 4, 4)]
  [TestCase("cjxl_lossy_animation_groups", 3, 3)]
  [TestCase("cjxl_lossy_animation_colour", 4, 3)]
  public void ALossyAnimationIsEveryMomentLibjxlShowsIt(string name, int moments, int parts) {
    var file = JpegXlReader.FromBytes(TestHelper.Fixture(name + ".jxl"));

    Assert.Multiple(() => {
      Assert.That(JpegXlFile.ImageCount(file), Is.EqualTo(moments));
      Assert.That(file.Frames, Has.Length.EqualTo(moments));
      Assert.That(file.ComponentCount, Is.EqualTo(parts));
    });

    // The pattern is added before the samples are rounded, so the comparison
    // needs the picture as it stands in float rather than the raster a caller
    // gets — adding less than half a level to a whole one changes nothing.
    Assert.That(JpegXlReader.TryReadSpecImage(TestHelper.Fixture(name + ".jxl"), out _, out var raw), Is.True);
    Assert.That(raw, Is.InstanceOf<JxlComposedImage>());
    var composed = (JxlComposedImage)raw!;
    Assert.That(composed.Frames, Has.Length.EqualTo(moments));

    var dither = TestHelper.Dither();
    for (var moment = 0; moment < moments; ++moment) {
      var reference = _ReadPam(TestHelper.Fixture(_Reference(name, moment)));
      Assert.Multiple(() => {
        Assert.That(file.Width, Is.EqualTo(reference.Width));
        Assert.That(file.Height, Is.EqualTo(reference.Height));
      });

      var planes = composed.Frames[moment];
      var worst = 0;
      var differing = 0;
      for (var y = 0; y < reference.Height; ++y)
      for (var x = 0; x < reference.Width; ++x)
      for (var c = 0; c < reference.Channels; ++c) {
        var at = y * reference.Width + x;
        // A grey picture comes back in colour, so libjxl's one channel is this
        // decode's first; the alpha, wherever it sits in the reference, is the
        // plane the picture says carries it.
        var grey = reference.Channels <= 2;
        var isAlpha = c >= (grey ? 1 : 3);
        var plane = isAlpha ? composed.AlphaPlane : c;
        var value = planes[plane][at] * 255.0f + TestHelper.DitherAt(dither, x, y, isAlpha ? 3 : c);
        var got = (byte)Math.Clamp((int)(Math.Clamp(value, 0.0f, 255.0f) + 0.5f), 0, 255);

        var want = reference.Samples[at * reference.Channels + c];
        if (got == want)
          continue;
        ++differing;
        worst = Math.Max(worst, Math.Abs(got - want));
      }

      var samples = reference.Width * reference.Height * reference.Channels;
      // One sample in 2,000, which is what sits near enough a rounding boundary
      // for two independent float pipelines to disagree about it.
      var allowed = Math.Max(1, samples / 2000);
      Assert.Multiple(() => {
        Assert.That(worst, Is.LessThanOrEqualTo(1),
          $"{name} moment {moment}: a sample is out by {worst} levels, which is more than rounding.");
        Assert.That(differing, Is.LessThanOrEqualTo(allowed),
          $"{name} moment {moment}: {differing} of {samples} samples differ from libjxl, and rounding accounts for at most {allowed}.");
      });
    }
  }

  /// <summary>
  /// The still picture such a file stands for is its first moment, not its
  /// frames drawn on top of one another.
  /// </summary>
  [TestCase("cjxl_lossy_animation")]
  [TestCase("cjxl_lossy_animation_alpha")]
  [TestCase("cjxl_lossy_animation_groups")]
  [TestCase("cjxl_lossy_animation_colour")]
  public void TheStillPictureIsTheFirstMoment(string name) {
    var file = JpegXlReader.FromBytes(TestHelper.Fixture(name + ".jxl"));
    Assert.That(file.PixelData, Is.EqualTo(file.Frames[0]));
  }

  /// <summary>
  /// A frame that covers only part of the picture leaves the rest of it as it
  /// was, so the moments really are being composed rather than each read on its
  /// own.
  /// </summary>
  /// <remarks>
  /// Both of these files state their second frame as a small rectangle over the
  /// first. Reading that frame alone would give a picture that is the rectangle
  /// and nothing else; what a viewer shows is the earlier picture with the
  /// rectangle drawn into it, and outside the rectangle the two moments have to
  /// agree exactly.
  /// </remarks>
  [TestCase("cjxl_lossy_animation", 96, 72)]
  [TestCase("cjxl_lossy_animation_groups", 260, 60)]
  [TestCase("cjxl_lossy_animation_colour", 96, 72)]
  public void APartialFrameKeepsWhatWasUnderIt(string name, int width, int height) {
    var file = JpegXlReader.FromBytes(TestHelper.Fixture(name + ".jxl"));
    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(width));
      Assert.That(file.Height, Is.EqualTo(height));
    });

    var first = file.Frames[0];
    var second = file.Frames[1];
    var same = 0;
    var apart = 0;
    for (var i = 0; i < first.Length; ++i)
      if (first[i] == second[i])
        ++same;
      else
        ++apart;

    Assert.Multiple(() => {
      // Most of the picture did not move.
      Assert.That(same, Is.GreaterThan(first.Length / 2),
        "the second moment shares none of the first, so nothing was composed");
      // And some of it did, or the file states a frame that changes nothing.
      Assert.That(apart, Is.GreaterThan(0));
    });
  }

  private static string _Reference(string name, int moment)
    => moment == 0 ? name + ".pam" : $"{name}.frame{moment}.pam";

  /// <summary>
  /// A PAM as <c>djxl</c> writes one: a header of keyword lines, then the
  /// samples, one byte each. It is kept rather than a <c>.ppm</c> because a
  /// grey picture is a third the size that way and because it can hold the
  /// alpha plane, which is what makes an animation's every moment affordable.
  /// </summary>
  private static (int Width, int Height, int Channels, byte[] Samples) _ReadPam(byte[] pam) {
    var at = 0;
    string Line() {
      var start = at;
      while (at < pam.Length && pam[at] != (byte)'\n')
        ++at;
      var line = System.Text.Encoding.ASCII.GetString(pam, start, at - start);
      ++at;
      return line;
    }

    Assert.That(Line(), Is.EqualTo("P7"));
    var fields = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var guard = 0; guard < 32; ++guard) {
      var line = Line();
      if (line == "ENDHDR")
        break;
      var space = line.IndexOf(' ');
      Assert.That(space, Is.GreaterThan(0), $"unreadable PAM header line '{line}'");
      fields[line[..space]] = line[(space + 1)..];
    }

    var width = int.Parse(fields["WIDTH"]);
    var height = int.Parse(fields["HEIGHT"]);
    var channels = int.Parse(fields["DEPTH"]);
    Assert.That(fields["MAXVAL"], Is.EqualTo("255"));

    var samples = new byte[width * height * channels];
    Array.Copy(pam, at, samples, 0, samples.Length);
    return (width, height, channels, samples);
  }
}
