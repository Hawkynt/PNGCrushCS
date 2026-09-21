using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FileFormat.Core;

/// <summary>The camera/view meaning of one image in a simultaneous multi-view picture.</summary>
public enum RawImageViewRole {
  /// <summary>No spatial role is known; <see cref="RawImageView.Index"/> still identifies the view.</summary>
  Unspecified,
  /// <summary>Left eye/camera view of a stereo or wider multi-camera set.</summary>
  Left,
  /// <summary>Right eye/camera view of a stereo or wider multi-camera set.</summary>
  Right,
  /// <summary>Center/reference camera view in a multi-camera set.</summary>
  Center,
  /// <summary>An additional camera/view with no stronger standardized spatial role.</summary>
  Auxiliary,
}

/// <summary>One simultaneous raster view and the metadata that identifies it inside a view set.</summary>
/// <param name="Image">Decoded raster for this view.</param>
/// <param name="Index">Stable zero-based view identifier from the source representation.</param>
/// <param name="Role">Optional spatial meaning such as left or right eye.</param>
/// <param name="QualityRank">Optional one-based preference/quality rank; one is best, larger values are less preferred.</param>
public sealed record RawImageView(
  RawImage Image,
  int Index,
  RawImageViewRole Role = RawImageViewRole.Unspecified,
  int? QualityRank = null);

/// <summary>
/// A set of decoded raster views that represent the same presentation instant from multiple cameras
/// or eyes.
/// </summary>
/// <remarks>
/// This is semantic topology, not another spelling of <c>RawImage[]</c>. All views must have the same
/// dimensions and pixel representation, matching the invariant used by VC-5 layers for stereo images.
/// The explicit <see cref="RawImageView.Index"/> is intentionally distinct from list position so a
/// codec can preserve its native channel/view identifier even when a caller reorders views.
/// <para/>
/// Do <b>not</b> use this type for consecutive temporal frames. A codec such as legacy CineForm's
/// two-frame volumetric/3-D wavelet may code multiple time samples in one compressed group, but after
/// reconstruction those remain temporal frames with separate presentation times. Likewise, HDR
/// exposures and interlaced fields are VC-5 layer applications but are not camera views; they need
/// their own semantic topology rather than being mislabeled as stereo.
/// </remarks>
public sealed class RawMultiViewImage {
  private readonly RawImageView[] _views;
  private readonly ReadOnlyCollection<RawImageView> _readOnlyViews;

  public RawMultiViewImage(IEnumerable<RawImageView> views) {
    ArgumentNullException.ThrowIfNull(views);

    var materialized = views.ToArray();
    if (materialized.Length < 2)
      throw new ArgumentException("A multi-view image needs at least two simultaneous views.", nameof(views));
    if (materialized.Any(static view => view is null))
      throw new ArgumentException("A multi-view image cannot contain a null view.", nameof(views));

    Array.Sort(materialized, static (left, right) => left.Index.CompareTo(right.Index));
    this._views = materialized;
    this._readOnlyViews = Array.AsReadOnly(this._views);

    var first = this._views[0];
    if (first.Image is null)
      throw new ArgumentException("A multi-view image cannot contain a null raster.", nameof(views));
    if (first.Index < 0)
      throw new ArgumentException("View identifiers must be non-negative.", nameof(views));

    this.Width = first.Image.Width;
    this.Height = first.Image.Height;
    this.Format = first.Image.Format;

    var seenIndices = new HashSet<int>();
    var seenSpatialRoles = new HashSet<RawImageViewRole>();
    foreach (var view in this._views) {
      if (view.Image is null)
        throw new ArgumentException("A multi-view image cannot contain a null raster.", nameof(views));
      if (view.Index < 0 || !seenIndices.Add(view.Index))
        throw new ArgumentException($"View identifier {view.Index} is negative or duplicated.", nameof(views));
      if (view.QualityRank is <= 0)
        throw new ArgumentException($"View {view.Index} has quality rank {view.QualityRank}; ranks are one-based when present.", nameof(views));
      if (view.Image.Width != this.Width || view.Image.Height != this.Height || view.Image.Format != this.Format)
        throw new ArgumentException(
          $"View {view.Index} is {view.Image.Width}x{view.Image.Height} {view.Image.Format}; all views must match {this.Width}x{this.Height} {this.Format}.",
          nameof(views));

      if (view.Role is RawImageViewRole.Left or RawImageViewRole.Right or RawImageViewRole.Center)
        if (!seenSpatialRoles.Add(view.Role))
          throw new ArgumentException($"A multi-view image cannot contain two {view.Role} views.", nameof(views));
    }
  }

  /// <summary>All views ordered by their stable <see cref="RawImageView.Index"/>.</summary>
  public IReadOnlyList<RawImageView> Views => this._readOnlyViews;

  public int Width { get; }
  public int Height { get; }
  public PixelFormat Format { get; }

  /// <summary>Gets one view by its native/stable identifier rather than by list position.</summary>
  public RawImageView GetView(int index) {
    if (index < 0)
      throw new ArgumentOutOfRangeException(nameof(index));

    foreach (var view in this._views)
      if (view.Index == index)
        return view;

    throw new ArgumentOutOfRangeException(nameof(index), index, "The multi-view image does not contain that view identifier.");
  }

  /// <summary>Returns every view with the requested semantic role.</summary>
  public IEnumerable<RawImageView> WithRole(RawImageViewRole role) {
    if (!Enum.IsDefined(role))
      throw new ArgumentOutOfRangeException(nameof(role));
    return this._views.Where(view => view.Role == role);
  }
}
