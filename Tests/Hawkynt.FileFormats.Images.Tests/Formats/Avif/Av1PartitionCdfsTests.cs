using FileFormat.Avif.Codec;

namespace FileFormat.Avif.Tests;

[TestFixture]
public sealed class Av1PartitionCdfsTests {

  [Test]
  [Category("Unit")]
  public void Defaults_MatchAv1Section94PartitionTables() {
    var cdfs = new Av1PartitionCdfs();

    Assert.Multiple(() => {
      Assert.That(cdfs.GetPartitionCdf(3, 0), Is.EqualTo(new ushort[] { 19132, 25510, 30392, 32768, 0 }));
      Assert.That(cdfs.GetPartitionCdf(4, 0), Is.EqualTo(new ushort[] { 15597, 20929, 24571, 26706, 27664, 28821, 29601, 30571, 31902, 32768, 0 }));
      Assert.That(cdfs.GetPartitionCdf(5, 3), Is.EqualTo(new ushort[] { 1394, 2208, 2796, 28614, 29061, 29466, 29840, 30185, 31899, 32768, 0 }));
      Assert.That(cdfs.GetPartitionCdf(6, 2), Is.EqualTo(new ushort[] { 5945, 7663, 8348, 28683, 29117, 29749, 30064, 30298, 32238, 32768, 0 }));
      Assert.That(cdfs.GetPartitionCdf(7, 0), Is.EqualTo(new ushort[] { 27899, 28219, 28529, 32484, 32539, 32619, 32639, 32768, 0 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void EdgeCdfs_FoldDisallowedPartitionProbabilitiesIntoSplit() {
    var cdfs = new Av1PartitionCdfs();
    var w16 = cdfs.GetPartitionCdf(4, 0);
    var w128 = cdfs.GetPartitionCdf(7, 0);

    Assert.Multiple(() => {
      Assert.That(Av1PartitionCdfs.BuildSplitOrHorizontalCdf(w16, block128: false), Is.EqualTo(new ushort[] { 23417, 32768, 0 }));
      Assert.That(Av1PartitionCdfs.BuildSplitOrVerticalCdf(w16, block128: false), Is.EqualTo(new ushort[] { 21075, 32768, 0 }));
      Assert.That(Av1PartitionCdfs.BuildSplitOrHorizontalCdf(w128, block128: true), Is.EqualTo(new ushort[] { 28299, 32768, 0 }));
      Assert.That(Av1PartitionCdfs.BuildSplitOrVerticalCdf(w128, block128: true), Is.EqualTo(new ushort[] { 28338, 32768, 0 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void Instances_HaveIndependentMutableTileState() {
    var first = new Av1PartitionCdfs();
    var second = new Av1PartitionCdfs();

    first.GetPartitionCdf(4, 2)[0] = 1;

    Assert.That(second.GetPartitionCdf(4, 2)[0], Is.EqualTo(5414));
  }
}
