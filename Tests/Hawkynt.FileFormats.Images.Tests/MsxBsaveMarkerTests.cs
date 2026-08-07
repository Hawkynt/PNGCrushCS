using System;
using Hawkynt.FileFormats.Images;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>
/// The byte every MSX BSAVE file opens with is not any one format's signature.
/// </summary>
/// <remarks>
/// 0xFE opens every BSAVE file the MSX writes, whichever screen mode it holds, so it says what the
/// container is and nothing about which format the file is. Nine of them declared it as their magic
/// — Screens 2, 3, 4, 5, 6, 8 and 10, plus GraphSaurus — and the registry consults magic before
/// extension, so whichever it reached first took every MSX picture there was.
/// <para/>
/// A Screen 5 picture, 256 by 212, was opened as a Screen 6 one and drawn 512 by 424. Across the
/// sample corpus that accounted for eleven pictures at double their size in each direction.
/// <para/>
/// The extension is what tells these apart and it is what decides now.
/// </remarks>
[TestFixture]
public sealed class MsxBsaveMarkerTests {

  [Test]
  [Category("Unit")]
  public void TheBsaveMarkerAloneIdentifiesNoFormat() {
    // A lone 0xFE, and then nothing that says which screen mode follows.
    var data = new byte[64];
    data[0] = 0xFE;

    Assert.That(FormatRegistry.DetectFromBytes(data), Is.EqualTo(ImageFormat.Unknown),
      "the container marker must not pick a screen mode on its own");
  }

  [Test]
  [Category("Unit")]
  public void TheExtensionStillNamesEachScreenMode() {
    Assert.Multiple(() => {
      foreach (var (extension, expected) in new[] {
        (".sc2", "MsxScreen2"), (".sc5", "MsxScreen5"),
        (".sc8", "MsxScreen8"), (".sr6", "GraphSaurus6"),
        (".sr7", "GraphSaurus7"),
      }) {
        var candidates = FormatRegistry.DetectCandidatesFromExtension(extension);
        Assert.That(candidates.Count, Is.GreaterThan(0), extension);
        Assert.That(candidates.ToString(), Is.Not.Null);
        Assert.That(
          System.Linq.Enumerable.Any(candidates, c => c.ToString() == expected),
          Is.True, $"{extension} should still reach {expected}");
      }
    });
  }

  [Test]
  [Category("Unit")]
  public void ADifferentMarkerIsStillItsOwnFormat() {
    // 0xFD belongs to one format alone, so it remains a usable signature.
    var data = new byte[64];
    data[0] = 0xFD;

    Assert.That(FormatRegistry.DetectFromBytes(data), Is.Not.EqualTo(ImageFormat.Unknown));
  }
}
