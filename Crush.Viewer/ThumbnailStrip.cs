using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Crush.Viewer;

/// <summary>Virtualized thumbnail strip — fixed number of PictureBox controls recycled during scroll,
/// backed by a sparse image cache with lazy preloading.</summary>
internal sealed class ThumbnailStrip : Panel {

  private const int _THUMB_SIZE = 64;
  private const int _CELL_SIZE = ThumbnailStrip._THUMB_SIZE + 6;
  private const int _PRELOAD_AHEAD = 16;

  private readonly HScrollBar _scrollBar;
  private readonly List<PictureBox> _slots = [];
  private readonly Dictionary<int, Bitmap> _cache = new();

  private int _totalCount;
  private int _scrollOffset;
  private int _selectedIndex;
  private Func<int, CancellationToken, Task<Bitmap?>>? _loader;
  private CancellationTokenSource? _loadCts;

  /// <summary>Fired when the user clicks a thumbnail.</summary>
  internal event Action<int>? IndexSelected;

  internal ThumbnailStrip() {
    this.Height = ThumbnailStrip._CELL_SIZE + 20; // cells + scrollbar
    this.BackColor = Color.FromArgb(32, 32, 32);
    this.Visible = false;
    this.Dock = DockStyle.Bottom;

    this._scrollBar = new() { Dock = DockStyle.Bottom, Minimum = 0, SmallChange = 1, LargeChange = 4 };
    this._scrollBar.Scroll += (_, _) => {
      this._scrollOffset = this._scrollBar.Value;
      this._Refresh(); };
    this.Controls.Add(this._scrollBar);
  }

  /// <summary>Resets the strip for a new multi-image file.</summary>
  internal void SetSource(int totalCount, Func<int, CancellationToken, Task<Bitmap?>> loader) {
    this._loadCts?.Cancel();
    this._loadCts?.Dispose();
    this._loadCts = new();

    this._ClearCache();
    this._totalCount = totalCount;
    this._scrollOffset = 0;
    this._selectedIndex = 0;
    this._loader = loader;

    this.Visible = totalCount >= 2;
    if (!this.Visible) return;

    this._EnsureSlots();
    this._UpdateScrollBar();
    this._Refresh();
    this._PreloadAround(0);
  }

  /// <summary>Clears all state (single-image file loaded).</summary>
  internal void Clear() {
    this._loadCts?.Cancel();
    this._loadCts?.Dispose();
    this._loadCts = null;
    this._ClearCache();
    this._totalCount = 0;
    this.Visible = false;
  }

  /// <summary>Selects a thumbnail by frame index, scrolls it into view.</summary>
  internal void Select(int index) {
    if (index < 0 || index >= this._totalCount) return;

    this._selectedIndex = index;

    // Scroll so the selected index is visible
    var visibleCount = this._VisibleSlotCount();
    if (index < this._scrollOffset)
      this._scrollOffset = index;
    else if (index >= this._scrollOffset + visibleCount)
      this._scrollOffset = index - visibleCount + 1;

    this._scrollOffset = Math.Clamp(this._scrollOffset, 0, Math.Max(0, this._totalCount - visibleCount));
    this._scrollBar.Value = this._scrollOffset;
    this._Refresh();
    this._PreloadAround(index);
  }

  protected override void OnResize(EventArgs e) {
    base.OnResize(e);
    if (this._totalCount < 2) return;

    this._EnsureSlots();
    this._UpdateScrollBar();
    this._Refresh();
  }

  private int _VisibleSlotCount() => Math.Max(1, (this.ClientSize.Width - 4) / ThumbnailStrip._CELL_SIZE);

  private void _EnsureSlots() {
    var needed = this._VisibleSlotCount();
    while (this._slots.Count < needed) {
      var pb = new PictureBox {
        SizeMode = PictureBoxSizeMode.CenterImage,
        Width = ThumbnailStrip._CELL_SIZE,
        Height = ThumbnailStrip._CELL_SIZE,
        BackColor = Color.FromArgb(48, 48, 48),
        Cursor = Cursors.Hand,
        BorderStyle = BorderStyle.FixedSingle,
      };
      pb.Click += this._OnSlotClick;
      this._slots.Add(pb);
      this.Controls.Add(pb);
    }

    // Hide excess slots
    for (var i = 0; i < this._slots.Count; ++i)
      this._slots[i].Visible = i < needed;
  }

  private void _UpdateScrollBar() {
    var visible = this._VisibleSlotCount();
    this._scrollBar.Maximum = Math.Max(0, this._totalCount - 1);
    this._scrollBar.LargeChange = Math.Max(1, visible);
    this._scrollBar.Enabled = this._totalCount > visible;
  }

  private void _Refresh() {
    var visible = this._VisibleSlotCount();
    for (var i = 0; i < visible && i < this._slots.Count; ++i) {
      var frameIndex = this._scrollOffset + i;
      var pb = this._slots[i];
      pb.Location = new(2 + i * ThumbnailStrip._CELL_SIZE, 2);
      pb.Tag = frameIndex;

      if (frameIndex < this._totalCount) {
        pb.Visible = true;
        pb.BorderStyle = frameIndex == this._selectedIndex ? BorderStyle.Fixed3D : BorderStyle.FixedSingle;
        pb.Image = this._cache.GetValueOrDefault(frameIndex);
      } else {
        pb.Visible = false;
      }
    }
  }

  private void _OnSlotClick(object? sender, EventArgs e) {
    if (sender is PictureBox pb && pb.Tag is int index && index < this._totalCount)
      this.IndexSelected?.Invoke(index);
  }

  private void _PreloadAround(int centerIndex) {
    if (this._loader == null || this._loadCts == null) return;
    var ct = this._loadCts.Token;
    var visible = this._VisibleSlotCount();
    var start = Math.Max(0, centerIndex - visible);
    var end = Math.Min(this._totalCount, centerIndex + visible + ThumbnailStrip._PRELOAD_AHEAD);

    _ = Task.Run(async () => {
      for (var i = start; i < end; ++i) {
        if (ct.IsCancellationRequested) return;
        if (this._cache.ContainsKey(i)) continue;

        var thumb = await this._loader(i, ct);
        if (ct.IsCancellationRequested || thumb == null) continue;

        this._cache[i] = thumb;

        // Update visible slot on UI thread if this frame is currently visible
        var slotIndex = i - this._scrollOffset;
        if (slotIndex >= 0 && slotIndex < this._slots.Count) {
          try {
            this.Invoke(() => {
              if (!ct.IsCancellationRequested && slotIndex < this._slots.Count && this._slots[slotIndex].Tag is int tag && tag == i)
                this._slots[slotIndex].Image = thumb;
            });
          } catch (ObjectDisposedException) { return; }
          catch (InvalidOperationException) { return; }
        }
      }
    }, ct);
  }

  private void _ClearCache() {
    foreach (var bmp in this._cache.Values)
      bmp.Dispose();
    this._cache.Clear();

    foreach (var slot in this._slots)
      slot.Image = null;
  }

  protected override void Dispose(bool disposing) {
    if (disposing) {
      this._loadCts?.Cancel();
      this._loadCts?.Dispose();
      this._ClearCache();
    }
    base.Dispose(disposing);
  }
}
