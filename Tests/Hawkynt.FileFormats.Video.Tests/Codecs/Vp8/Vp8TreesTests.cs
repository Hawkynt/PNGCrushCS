using System;
using System.Collections.Generic;
using System.Linq;

namespace FileFormat.Codecs.Vp8.Tests;

/// <summary>
/// The coding trees of RFC 6386, checked as trees and against the codes the standard prints.
/// </summary>
/// <remarks>
/// A tree is a transcription, and a transcription can go wrong in three ways that a decoded picture
/// only sometimes reveals: a leaf can be missing, a leaf can appear twice, and a leaf can carry the
/// wrong value. The first two are visible in the shape alone — every value of the alphabet has to
/// appear exactly once and every interior node has to be reachable — and the shape is what the first
/// tests here check.
/// <para/>
/// The third is not visible in the shape at all, and is what the code strings are for. RFC 6386
/// prints the bits of these values as comments beside the trees; walking the tree with those bits
/// and asking what it arrives at compares two independent statements of the same thing.
/// </remarks>
[TestFixture]
public sealed class Vp8TreesTests {

  /// <summary>Each tree by name, with the size of the alphabet it codes.</summary>
  /// <remarks>
  /// Named rather than passed as spans, because a span cannot be a test case argument and the trees
  /// are internal to the codec while a test method NUnit can call has to be public.
  /// </remarks>
  private static IEnumerable<TestCaseData> _Trees() {
    yield return new TestCaseData("LumaMode", 5);
    yield return new TestCaseData("KeyFrameLumaMode", 5);
    yield return new TestCaseData("ChromaMode", 4);
    yield return new TestCaseData("SubblockMode", 10);
    yield return new TestCaseData("Segment", 4);
    yield return new TestCaseData("Token", 12);
    yield return new TestCaseData("SmallMotionVector", 8);
    yield return new TestCaseData("MotionVectorReference", 5);
    yield return new TestCaseData("SplitPartition", 4);
    yield return new TestCaseData("SubblockMotionVectorReference", 4);
  }

  private static sbyte[] _Tree(string name) => name switch {
    "LumaMode" => Vp8Trees.LumaMode.ToArray(),
    "KeyFrameLumaMode" => Vp8Trees.KeyFrameLumaMode.ToArray(),
    "ChromaMode" => Vp8Trees.ChromaMode.ToArray(),
    "SubblockMode" => Vp8Trees.SubblockMode.ToArray(),
    "Segment" => Vp8Trees.Segment.ToArray(),
    "Token" => Vp8Trees.Token.ToArray(),
    "SmallMotionVector" => Vp8Trees.SmallMotionVector.ToArray(),
    "MotionVectorReference" => Vp8Trees.MotionVectorReference.ToArray(),
    "SplitPartition" => Vp8Trees.SplitPartition.ToArray(),
    "SubblockMotionVectorReference" => Vp8Trees.SubblockMotionVectorReference.ToArray(),
    _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No such tree."),
  };

  [TestCaseSource(nameof(_Trees))]
  [Category("Unit")]
  public void ATreeHasTwoEntriesPerInteriorNode(string name, int alphabetSize)
    // A binary tree over n values has n-1 interior nodes and so 2(n-1) entries.
    => Assert.That(_Tree(name).Length, Is.EqualTo(2 * (alphabetSize - 1)));

  [TestCaseSource(nameof(_Trees))]
  [Category("Unit")]
  public void EveryValueOfTheAlphabetIsReachedExactlyOnce(string name, int alphabetSize) {
    var tree = _Tree(name);
    var leaves = new List<int>();
    var visited = new HashSet<int>();
    _Walk(tree, 0, leaves, visited);

    // The values are offset for the trees whose alphabets continue another's numbering, so what is
    // checked is that the leaves are a run of consecutive values with no gap and no repeat.
    Assert.That(leaves.Count, Is.EqualTo(alphabetSize), "leaf count");
    Assert.That(leaves.Distinct().Count(), Is.EqualTo(alphabetSize), "no value appears twice");
    Assert.That(leaves.Max() - leaves.Min(), Is.EqualTo(alphabetSize - 1), "no value is missing");
    Assert.That(visited.Count, Is.EqualTo(alphabetSize - 1), "every interior node is reachable");
  }

  private static void _Walk(sbyte[] tree, int node, List<int> leaves, HashSet<int> visited) {
    Assert.That(visited.Add(node), Is.True, $"node {node} is reached twice");
    Assert.That(node % 2, Is.Zero, $"node {node} is not the start of a pair");

    for (var branch = 0; branch < 2; ++branch) {
      var next = tree[node + branch];
      if (next > 0)
        _Walk(tree, next, leaves, visited);
      else
        leaves.Add(-next);
    }
  }

  /// <summary>The codes RFC 6386 prints beside its trees, and the values it says they reach.</summary>
  private static IEnumerable<TestCaseData> _Codes() {
    // Section 8.2, the two luma trees and the chroma tree.
    yield return new TestCaseData("KeyFrameLumaMode", "0", Vp8Mode.SUBBLOCK_PREDICTION);
    yield return new TestCaseData("KeyFrameLumaMode", "100", Vp8Mode.DC_PREDICTION);
    yield return new TestCaseData("KeyFrameLumaMode", "101", Vp8Mode.VERTICAL_PREDICTION);
    yield return new TestCaseData("KeyFrameLumaMode", "110", Vp8Mode.HORIZONTAL_PREDICTION);
    yield return new TestCaseData("KeyFrameLumaMode", "111", Vp8Mode.TRUE_MOTION_PREDICTION);
    yield return new TestCaseData("LumaMode", "0", Vp8Mode.DC_PREDICTION);
    yield return new TestCaseData("LumaMode", "100", Vp8Mode.VERTICAL_PREDICTION);
    yield return new TestCaseData("LumaMode", "101", Vp8Mode.HORIZONTAL_PREDICTION);
    yield return new TestCaseData("LumaMode", "110", Vp8Mode.TRUE_MOTION_PREDICTION);
    yield return new TestCaseData("LumaMode", "111", Vp8Mode.SUBBLOCK_PREDICTION);
    yield return new TestCaseData("ChromaMode", "0", Vp8Mode.DC_PREDICTION);
    yield return new TestCaseData("ChromaMode", "10", Vp8Mode.VERTICAL_PREDICTION);
    yield return new TestCaseData("ChromaMode", "110", Vp8Mode.HORIZONTAL_PREDICTION);
    yield return new TestCaseData("ChromaMode", "111", Vp8Mode.TRUE_MOTION_PREDICTION);

    // Section 11.2, the subblock modes.
    yield return new TestCaseData("SubblockMode", "0", Vp8Mode.B_DC_PREDICTION);
    yield return new TestCaseData("SubblockMode", "10", Vp8Mode.B_TRUE_MOTION_PREDICTION);
    yield return new TestCaseData("SubblockMode", "110", Vp8Mode.B_VERTICAL_PREDICTION);
    yield return new TestCaseData("SubblockMode", "11100", Vp8Mode.B_HORIZONTAL_PREDICTION);
    yield return new TestCaseData("SubblockMode", "111010", Vp8Mode.B_RIGHT_DOWN_PREDICTION);
    yield return new TestCaseData("SubblockMode", "111011", Vp8Mode.B_VERTICAL_RIGHT_PREDICTION);
    // The last four codes are one bit shorter than the comments beside the tree in RFC 6386 make
    // them. The comments are wrong and the array they annotate is right: the array is what libvpx
    // codes with, and a decoder built to the comments misreads every macroblock predicted a subblock
    // at a time. These are the codes the array gives.
    yield return new TestCaseData("SubblockMode", "11110", Vp8Mode.B_LEFT_DOWN_PREDICTION);
    yield return new TestCaseData("SubblockMode", "111110", Vp8Mode.B_VERTICAL_LEFT_PREDICTION);
    yield return new TestCaseData("SubblockMode", "1111110", Vp8Mode.B_HORIZONTAL_DOWN_PREDICTION);
    yield return new TestCaseData("SubblockMode", "1111111", Vp8Mode.B_HORIZONTAL_UP_PREDICTION);

    // Section 13.2, the residue tokens.
    yield return new TestCaseData("Token", "0", Vp8Token.END_OF_BLOCK);
    yield return new TestCaseData("Token", "10", Vp8Token.ZERO);
    yield return new TestCaseData("Token", "110", Vp8Token.ONE);
    yield return new TestCaseData("Token", "11100", Vp8Token.TWO);
    yield return new TestCaseData("Token", "111010", Vp8Token.THREE);
    yield return new TestCaseData("Token", "111011", Vp8Token.FOUR);
    yield return new TestCaseData("Token", "111100", Vp8Token.CATEGORY_1);
    yield return new TestCaseData("Token", "111101", Vp8Token.CATEGORY_2);
    yield return new TestCaseData("Token", "1111100", Vp8Token.CATEGORY_3);
    yield return new TestCaseData("Token", "1111101", Vp8Token.CATEGORY_4);
    yield return new TestCaseData("Token", "1111110", Vp8Token.CATEGORY_5);
    yield return new TestCaseData("Token", "1111111", Vp8Token.CATEGORY_6);

    // Section 16.2, how a macroblock's motion vector is arrived at.
    yield return new TestCaseData("MotionVectorReference", "0", Vp8Mode.ZERO_MV);
    yield return new TestCaseData("MotionVectorReference", "10", Vp8Mode.NEAREST_MV);
    yield return new TestCaseData("MotionVectorReference", "110", Vp8Mode.NEAR_MV);
    yield return new TestCaseData("MotionVectorReference", "1110", Vp8Mode.NEW_MV);
    yield return new TestCaseData("MotionVectorReference", "1111", Vp8Mode.SPLIT_MV);

    // Section 16.4, how a split macroblock is divided and how each subset gets its vector.
    yield return new TestCaseData("SplitPartition", "0", Vp8Split.SIXTEENTHS);
    yield return new TestCaseData("SplitPartition", "10", Vp8Split.QUARTERS);
    yield return new TestCaseData("SplitPartition", "110", Vp8Split.TOP_BOTTOM);
    yield return new TestCaseData("SplitPartition", "111", Vp8Split.LEFT_RIGHT);
    yield return new TestCaseData("SubblockMotionVectorReference", "0", Vp8Mode.LEFT_4X4);
    yield return new TestCaseData("SubblockMotionVectorReference", "10", Vp8Mode.ABOVE_4X4);
    yield return new TestCaseData("SubblockMotionVectorReference", "110", Vp8Mode.ZERO_4X4);
    yield return new TestCaseData("SubblockMotionVectorReference", "111", Vp8Mode.NEW_4X4);

    // Section 17.1, the small motion vector magnitudes.
    yield return new TestCaseData("SmallMotionVector", "000", 0);
    yield return new TestCaseData("SmallMotionVector", "001", 1);
    yield return new TestCaseData("SmallMotionVector", "010", 2);
    yield return new TestCaseData("SmallMotionVector", "011", 3);
    yield return new TestCaseData("SmallMotionVector", "100", 4);
    yield return new TestCaseData("SmallMotionVector", "101", 5);
    yield return new TestCaseData("SmallMotionVector", "110", 6);
    yield return new TestCaseData("SmallMotionVector", "111", 7);

    // Section 10, the segment identifiers.
    yield return new TestCaseData("Segment", "00", 0);
    yield return new TestCaseData("Segment", "01", 1);
    yield return new TestCaseData("Segment", "10", 2);
    yield return new TestCaseData("Segment", "11", 3);
  }

  [TestCaseSource(nameof(_Codes))]
  [Category("Unit")]
  public void TheCodesPrintedInTheStandardReachTheValuesItNamesForThem(string name, string code, int value) {
    var tree = _Tree(name);
    var node = 0;
    foreach (var character in code) {
      Assert.That(node, Is.GreaterThanOrEqualTo(0), $"the code '{code}' runs past a leaf");
      node = tree[node + (character == '1' ? 1 : 0)];
    }

    Assert.That(node, Is.LessThanOrEqualTo(0), $"the code '{code}' stops short of a leaf");
    Assert.That(-node, Is.EqualTo(value));
  }

  [Test]
  [Category("Unit")]
  public void TheInterModesContinueTheIntraNumbering() {
    // RFC 6386 sections 16.2 and 16.4 set the inter modes to start where the intra ones stop, so one
    // field can hold either. The decoder relies on that when it asks a neighbour what mode it used.
    Assert.That(Vp8Mode.NEAREST_MV, Is.EqualTo(Vp8Mode.INTRA_MODE_COUNT));
    Assert.That(Vp8Mode.LEFT_4X4, Is.EqualTo(Vp8Mode.SUBBLOCK_MODE_COUNT));
  }

  [Test]
  [Category("Unit")]
  public void TheSplitPartitioningsCoverEverySubblockOnce() {
    // Four ways of dividing sixteen subblocks (RFC 6386, 16.4). Every subblock belongs to exactly one
    // subset, and the subsets are numbered from zero without a gap — the decoder finds the first
    // subblock of each in turn and would run off the end of the macroblock on a gap.
    for (var partitioning = 0; partitioning < 4; ++partitioning) {
      var membership = Vp8Split.Membership.Slice(partitioning * 16, 16).ToArray();
      var subsets = Vp8Split.SubsetCount[partitioning];

      Assert.That(membership.Length, Is.EqualTo(16));
      Assert.That(membership.Select(m => (int)m).Distinct().Order(), Is.EqualTo(Enumerable.Range(0, subsets)),
        $"partitioning {partitioning}");
      foreach (var subset in Enumerable.Range(0, subsets))
        Assert.That(membership.Count(m => m == subset), Is.EqualTo(16 / subsets),
          $"partitioning {partitioning}, subset {subset}");
    }
  }

  [Test]
  [Category("Unit")]
  public void TheCoefficientContextsCoverTheTwentyFiveBlocksOfAMacroblock() {
    // Nine contexts in each direction: four luma, two each for U and V, and one for Y2 (RFC 6386,
    // 13.3). The luma indices have to run down the rows on the left and across the columns above,
    // which is what makes the context a neighbour relationship rather than a counter.
    Assert.That(Vp8Trees.LeftContextIndex.Length, Is.EqualTo(25));
    Assert.That(Vp8Trees.AboveContextIndex.Length, Is.EqualTo(25));

    for (var block = 0; block < 16; ++block) {
      Assert.That(Vp8Trees.LeftContextIndex[block], Is.EqualTo(block >> 2), $"luma block {block}, left");
      Assert.That(Vp8Trees.AboveContextIndex[block], Is.EqualTo(block & 3), $"luma block {block}, above");
    }

    Assert.That(Vp8Trees.LeftContextIndex[24], Is.EqualTo(8));
    Assert.That(Vp8Trees.AboveContextIndex[24], Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void TheZigZagVisitsEveryPositionOfABlockOnce() {
    Assert.That(Vp8Trees.ZigZag.Length, Is.EqualTo(16));
    Assert.That(Vp8Trees.ZigZag.ToArray().Select(z => (int)z).Order(), Is.EqualTo(Enumerable.Range(0, 16)));
    Assert.That(Vp8Trees.ZigZag[0], Is.Zero, "the scan starts at the first coefficient");
  }
}
