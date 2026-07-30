using System;
using FileFormat.Hdr;

namespace FileFormat.Hdr.Tests;

[TestFixture]
public sealed class RgbeCodecTests {

  [Test]
  [Category("Unit")]
  public void EncodePixel_Black_ReturnsZeroExponent() {
    var (r, g, b, e) = RgbeCodec.EncodePixel(0f, 0f, 0f);

    Assert.That(e, Is.EqualTo(0));
    Assert.That(r, Is.EqualTo(0));
    Assert.That(g, Is.EqualTo(0));
    Assert.That(b, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void DecodePixel_ZeroExponent_ReturnsBlack() {
    var (r, g, b) = RgbeCodec.DecodePixel(100, 200, 50, 0);

    Assert.That(r, Is.EqualTo(0f));
    Assert.That(g, Is.EqualTo(0f));
    Assert.That(b, Is.EqualTo(0f));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_UnitValues_PreservedWithinTolerance() {
    var originalR = 1.0f;
    var originalG = 0.5f;
    var originalB = 0.25f;

    var (er, eg, eb, ee) = RgbeCodec.EncodePixel(originalR, originalG, originalB);
    var (dr, dg, db) = RgbeCodec.DecodePixel(er, eg, eb, ee);

    Assert.That(dr, Is.EqualTo(originalR).Within(originalR * 0.02f));
    Assert.That(dg, Is.EqualTo(originalG).Within(originalG * 0.02f));
    Assert.That(db, Is.EqualTo(originalB).Within(originalB * 0.02f));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_BrightPixel_PreservedWithinTolerance() {
    var originalR = 100.0f;
    var originalG = 200.0f;
    var originalB = 50.0f;

    var (er, eg, eb, ee) = RgbeCodec.EncodePixel(originalR, originalG, originalB);
    var (dr, dg, db) = RgbeCodec.DecodePixel(er, eg, eb, ee);

    Assert.That(dr, Is.EqualTo(originalR).Within(originalR * 0.02f));
    Assert.That(dg, Is.EqualTo(originalG).Within(originalG * 0.02f));
    Assert.That(db, Is.EqualTo(originalB).Within(originalB * 0.02f));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_SmallValues_PreservedWithinTolerance() {
    var originalR = 0.001f;
    var originalG = 0.002f;
    var originalB = 0.003f;

    var (er, eg, eb, ee) = RgbeCodec.EncodePixel(originalR, originalG, originalB);
    var (dr, dg, db) = RgbeCodec.DecodePixel(er, eg, eb, ee);

    Assert.That(dr, Is.EqualTo(originalR).Within(originalR * 0.02f));
    Assert.That(dg, Is.EqualTo(originalG).Within(originalG * 0.02f));
    Assert.That(db, Is.EqualTo(originalB).Within(originalB * 0.02f));
  }

  [Test]
  [Category("Unit")]
  public void EncodePixel_SingleChannel_OtherChannelsSmall() {
    var (r, g, b, e) = RgbeCodec.EncodePixel(1.0f, 0.0f, 0.0f);

    Assert.That(e, Is.GreaterThan(0));
    Assert.That(r, Is.GreaterThan(0));
    Assert.That(g, Is.EqualTo(0));
    Assert.That(b, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_EqualChannels_AllMatch() {
    var value = 0.75f;

    var (er, eg, eb, ee) = RgbeCodec.EncodePixel(value, value, value);
    var (dr, dg, db) = RgbeCodec.DecodePixel(er, eg, eb, ee);

    Assert.That(dr, Is.EqualTo(dg).Within(0.001f));
    Assert.That(dg, Is.EqualTo(db).Within(0.001f));
  }
}
