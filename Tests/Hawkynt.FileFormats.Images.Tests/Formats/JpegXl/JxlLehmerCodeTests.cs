using System;
using System.Linq;
using FileFormat.JpegXl.Codec;
using NUnit.Framework;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Turning a Lehmer code back into the permutation it stands for.
/// </summary>
/// <remarks>
/// The code says, for each position in turn, how many of the values not yet
/// used are smaller than the one that belongs there. That definition is what
/// the cases below are worked out from, by hand and independently of the
/// decoder, so agreeing with them is agreeing with the definition rather than
/// with the implementation.
/// </remarks>
[TestFixture]
internal sealed class JxlLehmerCodeTests {

  /// <summary>Taking the smallest value still unused every time leaves
  /// everything where it started.</summary>
  [Test]
  public void AllZerosIsTheIdentity() {
    var order = JxlLehmerCode.Decode(new int[6], 6);
    Assert.That(order, Is.EqualTo(new[] { 0, 1, 2, 3, 4, 5 }));
  }

  /// <param name="code">Read as: at this position, skip this many of the
  /// values still unused.</param>
  /// <param name="expected">Worked out from that reading by hand.</param>
  [TestCase(new[] { 3, 0, 0, 0 }, new[] { 3, 0, 1, 2 }, TestName = "The largest first, then the rest in order")]
  [TestCase(new[] { 1, 1, 1, 0 }, new[] { 1, 2, 3, 0 }, TestName = "Skipping one each time rotates by one")]
  [TestCase(new[] { 3, 2, 1, 0 }, new[] { 3, 2, 1, 0 }, TestName = "Always the largest left reverses")]
  [TestCase(new[] { 0, 2, 0, 0 }, new[] { 0, 3, 1, 2 }, TestName = "A skip counts only what is still unused")]
  public void ACodeNamesTheValuesItSkipsPast(int[] code, int[] expected) {
    Assert.That(JxlLehmerCode.Decode(code, code.Length), Is.EqualTo(expected));
  }

  /// <summary>Whatever the code, what comes back uses every value once.</summary>
  [Test]
  public void WhatComesBackIsAlwaysAPermutation([Values(1, 2, 5, 64, 128, 1024)] int count) {
    var random = new Random(count * 7919);
    var code = new int[count];
    for (var i = 0; i < count; ++i)
      code[i] = random.Next(count - i);

    var order = JxlLehmerCode.Decode(code, count);

    Assert.Multiple(() => {
      Assert.That(order, Has.Length.EqualTo(count));
      Assert.That(order.Distinct().Count(), Is.EqualTo(count), "no value is used twice");
      Assert.That(order.Min(), Is.EqualTo(0));
      Assert.That(order.Max(), Is.EqualTo(count - 1));
    });
  }

  /// <summary>A permutation reduced to its code by the definition comes back
  /// unchanged.</summary>
  [Test]
  public void APermutationSurvivesBeingReducedToItsCodeAndBack([Values(2, 7, 64, 256)] int count) {
    var random = new Random(count * 104729);
    var permutation = Enumerable.Range(0, count).OrderBy(_ => random.Next()).ToArray();

    // The definition: how many values not yet used are smaller than this one.
    var used = new bool[count];
    var code = new int[count];
    for (var i = 0; i < count; ++i) {
      var smaller = 0;
      for (var v = 0; v < permutation[i]; ++v)
        if (!used[v])
          ++smaller;
      code[i] = smaller;
      used[permutation[i]] = true;
    }

    Assert.That(JxlLehmerCode.Decode(code, count), Is.EqualTo(permutation));
  }

  [Test]
  public void ACodeNamingAValueThatIsAlreadyGoneIsRefused() {
    // At the last position only one value is left, so anything but zero is a lie.
    Assert.Throws<ArgumentException>(() => JxlLehmerCode.Decode([0, 0, 1], 3));
  }
}
