using Crush.Image.Verbs;

namespace Crush.Image.Tests;

/// <summary>
/// Unit tests for the rewriting that fills in the value a bare on-by-default flag leaves out.
/// </summary>
/// <remarks>
/// The rewriting has to be narrow: it may only add a <c>true</c> after a flag that has no value,
/// on the verb that is actually being run, and it must leave everything else alone — short names
/// mean different things on different verbs, and what follows a <c>--</c> is not ours at all.
/// </remarks>
[TestFixture]
public sealed class VerbArgumentsTests {

  [Test]
  [Category("Unit")]
  public void SupplyImplicitTrue_NoArguments_StaysEmpty() {
    Assert.That(VerbArguments.SupplyImplicitTrue([]), Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void SupplyImplicitTrue_TrailingBareFlag_GainsTrue() {
    Assert.That(
      VerbArguments.SupplyImplicitTrue(["png", "-i", "a", "-o", "b", "--interlace"]),
      Is.EqualTo(new[] { "png", "-i", "a", "-o", "b", "--interlace", "True" }));
  }

  [Test]
  [Category("Unit")]
  public void SupplyImplicitTrue_BareFlagBeforeAnotherOption_GainsTrue() {
    Assert.That(
      VerbArguments.SupplyImplicitTrue(["png", "-a", "-i", "a", "-o", "b"]),
      Is.EqualTo(new[] { "png", "-a", "True", "-i", "a", "-o", "b" }));
  }

  [Test]
  [Category("Unit")]
  public void SupplyImplicitTrue_FlagThatAlreadyHasAValue_IsLeftAlone() {
    Assert.That(
      VerbArguments.SupplyImplicitTrue(["png", "--interlace", "false", "-i", "a", "-o", "b"]),
      Is.EqualTo(new[] { "png", "--interlace", "false", "-i", "a", "-o", "b" }));
  }

  [Test]
  [Category("Unit")]
  public void SupplyImplicitTrue_FlagWrittenWithAnEqualsSign_IsLeftAlone() {
    Assert.That(
      VerbArguments.SupplyImplicitTrue(["png", "--interlace=false", "-i", "a", "-o", "b"]),
      Is.EqualTo(new[] { "png", "--interlace=false", "-i", "a", "-o", "b" }));
  }

  [Test]
  [Category("Unit")]
  public void SupplyImplicitTrue_OffByDefaultSwitch_IsLeftAlone() {
    Assert.That(
      VerbArguments.SupplyImplicitTrue(["png", "--dithering", "-i", "a", "-o", "b"]),
      Is.EqualTo(new[] { "png", "--dithering", "-i", "a", "-o", "b" }));
  }

  [Test]
  [Category("Unit")]
  public void SupplyImplicitTrue_TokensAfterADoubleDash_AreLeftAlone() {
    Assert.That(
      VerbArguments.SupplyImplicitTrue(["png", "-i", "a", "-o", "b", "--", "--interlace"]),
      Is.EqualTo(new[] { "png", "-i", "a", "-o", "b", "--", "--interlace" }));
  }

  /// <summary>
  /// <c>-s</c> is <c>--strategies</c> on gif and <c>--strip-metadata</c> on webp, so the flags to
  /// rewrite can only be read off the verb the command line actually selects.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void SupplyImplicitTrue_ShortNameBelongingToAnotherVerb_IsLeftAlone() {
    Assert.That(
      VerbArguments.SupplyImplicitTrue(["gif", "-s", "Original", "-i", "a", "-o", "b"]),
      Is.EqualTo(new[] { "gif", "-s", "Original", "-i", "a", "-o", "b" }));
  }

  [Test]
  [Category("Unit")]
  public void SupplyImplicitTrue_ShortNameOfTheSelectedVerb_GainsTrue() {
    Assert.That(
      VerbArguments.SupplyImplicitTrue(["webp", "-s", "-i", "a", "-o", "b"]),
      Is.EqualTo(new[] { "webp", "-s", "True", "-i", "a", "-o", "b" }));
  }

  [Test]
  [Category("Unit")]
  public void SupplyImplicitTrue_VerbAlias_IsRecognised() {
    Assert.That(
      VerbArguments.SupplyImplicitTrue(["jpg", "--strip", "-i", "a", "-o", "b"]),
      Is.EqualTo(new[] { "jpg", "--strip", "True", "-i", "a", "-o", "b" }));
  }

  /// <summary>A command line that names no verb runs the default one, and is rewritten for it.</summary>
  [Test]
  [Category("Unit")]
  public void SupplyImplicitTrue_NoVerbNamed_UsesTheDefaultVerb() {
    Assert.That(
      VerbArguments.SupplyImplicitTrue(["--convert", "-i", "a", "-o", "b"]),
      Is.EqualTo(new[] { "--convert", "True", "-i", "a", "-o", "b" }));
  }

  [Test]
  [Category("Unit")]
  public void SupplyImplicitTrue_HelpRequest_IsLeftAlone() {
    Assert.That(VerbArguments.SupplyImplicitTrue(["--help"]), Is.EqualTo(new[] { "--help" }));
    Assert.That(VerbArguments.SupplyImplicitTrue(["png", "--help"]), Is.EqualTo(new[] { "png", "--help" }));
  }
}
