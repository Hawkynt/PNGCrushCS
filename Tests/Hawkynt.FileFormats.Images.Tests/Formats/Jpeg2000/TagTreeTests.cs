using FileFormat.Jpeg2000.Codec;

namespace FileFormat.Jpeg2000.Tests;

[TestFixture]
public sealed class TagTreeTests {

  [Test]
  [Category("Unit")]
  public void EncodeAndDecode_PreservesSharedParentStateAcrossThresholds() {
    var encodedTree = new TagTree(2, 1);
    encodedTree.SetValue(0, 0, 2);
    encodedTree.SetValue(1, 0, 0);

    var writer = new BitWriter();
    Assert.Multiple(() => {
      Assert.That(encodedTree.Encode(0, 0, 1, writer), Is.False);
      Assert.That(encodedTree.Encode(1, 0, 1, writer), Is.True);
      Assert.That(encodedTree.Encode(0, 0, 2, writer), Is.False);
      Assert.That(encodedTree.Encode(0, 0, 3, writer), Is.True);
    });

    var bytes = writer.Flush();
    var reader = new BitReader(bytes, 0, bytes.Length);
    var decodedTree = new TagTree(2, 1);

    Assert.Multiple(() => {
      Assert.That(decodedTree.Decode(0, 0, 1, reader), Is.False);
      Assert.That(decodedTree.Decode(1, 0, 1, reader), Is.True);
      Assert.That(decodedTree.Decode(0, 0, 2, reader), Is.False);
      Assert.That(decodedTree.Decode(0, 0, 3, reader), Is.True);
      Assert.That(decodedTree.GetValue(0, 0), Is.EqualTo(2));
      Assert.That(decodedTree.GetValue(1, 0), Is.EqualTo(0));
    });
  }

  [Test]
  [Category("Unit")]
  public void EncoderBuildsParentMinimumBeforePublishingAnyLeaf() {
    var tree = new TagTree(2, 1);
    tree.SetValue(0, 0, 5);
    tree.SetValue(1, 0, 0);

    var writer = new BitWriter();
    Assert.That(tree.Encode(0, 0, 1, writer), Is.False);

    // The parent minimum is zero because of the sibling. If the encoder builds parents lazily from
    // the first leaf, it writes five zero bits here before it even reaches the leaf.
    var bytes = writer.Flush();
    Assert.That(bytes[0] & 0x80, Is.Not.Zero, "root value zero is published with the first bit");
  }
}
