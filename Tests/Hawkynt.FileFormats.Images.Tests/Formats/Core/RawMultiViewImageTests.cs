using System;
using System.Collections.Generic;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Core.Tests;

[TestFixture]
public sealed class RawMultiViewImageTests {

  [Test]
  public void StereoPairPreservesRolesNativeIndicesAndGeometry() {
    var left = _Image(64, 48, PixelFormat.Rgb24, 1);
    var right = _Image(64, 48, PixelFormat.Rgb24, 2);

    var pair = new RawMultiViewImage([
      new(right, Index: 1, RawImageViewRole.Right, QualityRank: 2),
      new(left, Index: 0, RawImageViewRole.Left, QualityRank: 1),
    ]);

    Assert.Multiple(() => {
      Assert.That(pair.Width, Is.EqualTo(64));
      Assert.That(pair.Height, Is.EqualTo(48));
      Assert.That(pair.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(pair.Views.Select(static view => view.Index), Is.EqualTo(new[] { 0, 1 }));
      Assert.That(pair.GetView(0).Image, Is.SameAs(left));
      Assert.That(pair.GetView(1).Image, Is.SameAs(right));
      Assert.That(pair.WithRole(RawImageViewRole.Left).Single().Index, Is.EqualTo(0));
      Assert.That(pair.WithRole(RawImageViewRole.Right).Single().Index, Is.EqualTo(1));
    });
  }

  [Test]
  public void MulticamAllowsSeveralAuxiliaryViews() {
    var views = new RawMultiViewImage([
      new(_Image(16, 16, PixelFormat.Gray8, 1), 0, RawImageViewRole.Center),
      new(_Image(16, 16, PixelFormat.Gray8, 2), 1, RawImageViewRole.Auxiliary),
      new(_Image(16, 16, PixelFormat.Gray8, 3), 2, RawImageViewRole.Auxiliary),
    ]);

    Assert.That(views.WithRole(RawImageViewRole.Auxiliary).Count(), Is.EqualTo(2));
  }

  [Test]
  public void RequiresAtLeastTwoViews() {
    Assert.That(
      () => new RawMultiViewImage([new(_Image(8, 8, PixelFormat.Gray8, 1), 0)]),
      Throws.ArgumentException.With.Message.Contains("at least two"));
  }

  [Test]
  public void RejectsDuplicateOrNegativeNativeViewIdentifiers() {
    var image = _Image(8, 8, PixelFormat.Gray8, 1);

    Assert.Multiple(() => {
      Assert.That(
        () => new RawMultiViewImage([new(image, 3), new(image, 3)]),
        Throws.ArgumentException.With.Message.Contains("identifier"));
      Assert.That(
        () => new RawMultiViewImage([new(image, -1), new(image, 0)]),
        Throws.ArgumentException.With.Message.Contains("non-negative"));
    });
  }

  [Test]
  public void RejectsGeometryOrPixelFormatMismatch() {
    Assert.Multiple(() => {
      Assert.That(
        () => new RawMultiViewImage([
          new(_Image(64, 48, PixelFormat.Rgb24, 1), 0),
          new(_Image(32, 48, PixelFormat.Rgb24, 2), 1),
        ]),
        Throws.ArgumentException.With.Message.Contains("all views must match"));
      Assert.That(
        () => new RawMultiViewImage([
          new(_Image(64, 48, PixelFormat.Rgb24, 1), 0),
          new(_Image(64, 48, PixelFormat.Bgr24, 2), 1),
        ]),
        Throws.ArgumentException.With.Message.Contains("all views must match"));
    });
  }

  [Test]
  public void RejectsDuplicateNamedSpatialRolesButNotUnspecifiedOnes() {
    var first = _Image(8, 8, PixelFormat.Gray8, 1);
    var second = _Image(8, 8, PixelFormat.Gray8, 2);

    Assert.That(
      () => new RawMultiViewImage([
        new(first, 0, RawImageViewRole.Left),
        new(second, 1, RawImageViewRole.Left),
      ]),
      Throws.ArgumentException.With.Message.Contains("two Left"));

    Assert.DoesNotThrow(() => new RawMultiViewImage([new(first, 0), new(second, 1)]));
  }

  [Test]
  public void QualityRankIsOptionalButOneBasedWhenPresent() {
    var first = _Image(8, 8, PixelFormat.Gray8, 1);
    var second = _Image(8, 8, PixelFormat.Gray8, 2);

    Assert.Multiple(() => {
      Assert.DoesNotThrow(() => new RawMultiViewImage([new(first, 0), new(second, 1, QualityRank: 1)]));
      Assert.That(
        () => new RawMultiViewImage([new(first, 0), new(second, 1, QualityRank: 0)]),
        Throws.ArgumentException.With.Message.Contains("one-based"));
    });
  }

  [Test]
  public void NativeIndexIsNotArrayPosition() {
    var view2 = _Image(4, 4, PixelFormat.Gray8, 2);
    var view9 = _Image(4, 4, PixelFormat.Gray8, 9);
    var set = new RawMultiViewImage([new(view9, 9), new(view2, 2)]);

    Assert.Multiple(() => {
      Assert.That(set.Views[0].Index, Is.EqualTo(2));
      Assert.That(set.Views[1].Index, Is.EqualTo(9));
      Assert.That(set.GetView(9).Image, Is.SameAs(view9));
    });
  }

  [Test]
  public void RejectsANullViewAndANullRaster() {
    var image = _Image(8, 8, PixelFormat.Gray8, 1);

    Assert.Multiple(() => {
      Assert.That(
        () => new RawMultiViewImage([new(image, 0), null!]),
        Throws.ArgumentException.With.Message.Contains("null view"));
      Assert.That(
        () => new RawMultiViewImage([new(image, 0), new(null!, 1)]),
        Throws.ArgumentException.With.Message.Contains("null raster"));
    });
  }

  /// <summary>
  /// The view list is the whole point of the type, so handing out something a caller can append to
  /// would let a stereo pair grow a third eye after its invariants were checked.
  /// </summary>
  [Test]
  public void TheViewCollectionIsGenuinelyReadOnly() {
    var first = _Image(8, 8, PixelFormat.Gray8, 1);
    var set = new RawMultiViewImage([new(first, 0), new(_Image(8, 8, PixelFormat.Gray8, 2), 1)]);

    Assert.That(set.Views, Is.Not.AssignableTo<RawImageView[]>());
    Assert.That(
      () => ((IList<RawImageView>)set.Views).Add(new(first, 7)),
      Throws.TypeOf<NotSupportedException>());
  }

  [Test]
  public void AskingForAViewIdentifierOrRoleThatIsNotThereIsRefused() {
    var set = new RawMultiViewImage([
      new(_Image(8, 8, PixelFormat.Gray8, 1), 0),
      new(_Image(8, 8, PixelFormat.Gray8, 2), 1),
    ]);

    Assert.Multiple(() => {
      Assert.That(() => set.GetView(4), Throws.TypeOf<ArgumentOutOfRangeException>());
      Assert.That(() => set.GetView(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
      Assert.That(() => set.WithRole((RawImageViewRole)99), Throws.TypeOf<ArgumentOutOfRangeException>());
    });
  }

  private static RawImage _Image(int width, int height, PixelFormat format, byte fill) {
    var bytes = checked((int)((long)width * height * RawImage.BitsPerPixel(format) / 8));
    return new() {
      Width = width,
      Height = height,
      Format = format,
      PixelData = Enumerable.Repeat(fill, bytes).ToArray(),
    };
  }
}
