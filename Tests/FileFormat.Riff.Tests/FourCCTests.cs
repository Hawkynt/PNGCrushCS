using System;
using FileFormat.Riff;

namespace FileFormat.Riff.Tests;

[TestFixture]
public sealed class FourCCTests {

  [Test]
  [Category("Unit")]
  public void Constructor_FromString_StoresBytes() {
    FourCC cc = "RIFF";
    Assert.That(cc.A, Is.EqualTo((byte)'R'));
    Assert.That(cc.D, Is.EqualTo((byte)'F'));
  }

  [Test]
  [Category("Unit")]
  public void Constructor_WrongLength_ThrowsArgumentException() {
    Assert.Throws<ArgumentException>(() => { FourCC _ = new FourCC("AB"); });
  }

  [Test]
  [Category("Unit")]
  public void ToString_ReturnsOriginalString() {
    FourCC cc = "WAVE";
    Assert.That(cc.ToString(), Is.EqualTo("WAVE"));
  }

  [Test]
  [Category("Unit")]
  public void ImplicitConversion_StringToFourCC() {
    FourCC cc = "LIST";
    Assert.That((string)cc, Is.EqualTo("LIST"));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_WriteTo_RoundTrip() {
    var bytes = new byte[] { 0x41, 0x42, 0x43, 0x44 };
    var cc = FourCC.ReadFrom(bytes);

    var output = new byte[4];
    cc.WriteTo(output);

    Assert.That(output, Is.EqualTo(bytes));
  }
}
