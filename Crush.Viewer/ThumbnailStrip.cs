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
  private const int _CELL_SIZE = _THUMB_SIZE + 6;
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
    Height = _CELL_SIZE + 20; // cells + scrollbar
    BackColor = Color.FromArgb(32, 32, 32);
    Visible = false;
    Dock = DockStyle.Bottom;

    _scrollBar = new HScrollBar { Dock = DockStyle.Bottom, Minimum = 0, SmallChange = 1, LargeChange = 4 };
    _scrollBar.Scroll += (_, _) => { _scrollOffset = _scrollBar.Value; _Refresh(); };
    Controls.Add(_scrollBar);
  }

  /// <summary>Resets the strip for a new multi-image file.</summary>
  internal void SetSource(int totalCount, Func<int, CancellationToken, Task<Bitmap?>> loader) {
    _loadCts?.Cancel();
    _loadCts?.Dispose();
    _loadCts = new CancellationTokenSource();

    _ClearCache();
    _totalCount = totalCount;
    _scrollOffset = 0;
    _selectedIndex = 0;
    _loader = loader;

    Visible = totalCount >= 2;
    if (!Visible) return;

    _EnsureSlots();
    _UpdateScrollBar();
    _Refresh();
    _PreloadAround(0);
  }

  /// <summary>Clears all state (single-image file loaded).</summary>
  internal void Clear() {
    _loadCts?.Cancel();
    _loadCts?.Dispose();
    _loadCts = null;
    _ClearCache();
    _totalCount = 0;
    Visible = false;
  }

  /// <summary>Selects a thumbnail by frame index, scrolls it into view.</summary>
  internal void Select(int index) {
    if (index < 0 || index >= _totalCount) return;
    _selectedIndex = index;

    // Scroll so the selected index is visible
    var visibleCount = _VisibleSlotCount();
    if (index < _scrollOffset)
      _scrollOffset = index;
    else if (index >= _scrollOffset + visibleCount)
      _scrollOffset = index - visibleCount + 1;

    _scrollOffset = Math.Clamp(_scrollOffset, 0, Math.Max(0, _totalCount - visibleCount));
    _scrollBar.Value = _scrollOffset;
    _Refresh();
    _PreloadAround(index);
  }

  protected override void OnResize(EventArgs e) {
    base.OnResize(e);
    if (_totalCount < 2) return;
    _EnsureSlots();
    _UpdateScrollBar();
    _Refresh();
  }

  private int _VisibleSlotCount() => Math.Max(1, (ClientSize.Width - 4) / _CELL_SIZE);

  private void _EnsureSlots() {
    var needed = _VisibleSlotCount();
    while (_slots.Count < needed) {
      var pb = new PictureBox {
        SizeMode = PictureBoxSizeMode.CenterImage,
        Width = _CELL_SIZE,
        Height = _CELL_SIZE,
        BackColor = Color.FromArgb(48, 48, 48),
        Cursor = Cursors.Hand,
        BorderStyle = BorderStyle.FixedSingle,
      };
      pb.Click += _OnSlotClick;
      _slots.Add(pb);
      Controls.Add(pb);
    }

    // Hide excess slots
    for (var i = 0; i < _slots.Count; ++i)
      _slots[i].Visible = i < needed;
  }

  private void _UpdateScrollBar() {
    var visible = _VisibleSlotCount();
    _scrollBar.Maximum = Math.Max(0, _totalCount - 1);
    _scrollBar.LargeChange = Math.Max(1, visible);
    _scrollBar.Enabled = _totalCount > visible;
  }

  private void _Refresh() {
    var visible = _VisibleSlotCount();
    for (var i = 0; i < visible && i < _slots.Count; ++i) {
      var frameIndex = _scrollOffset + i;
      var pb = _slots[i];
      pb.Location = new Point(2 + i * _CELL_SIZE, 2);
      pb.Tag = frameIndex;

      if (frameIndex < _totalCount) {
        pb.Visible = true;
        pb.BorderStyle = frameIndex == _selectedIndex ? BorderStyle.Fixed3D : BorderStyle.FixedSingle;
        pb.Image = _cache.GetValueOrDefault(frameIndex);
      } else {
        pb.Visible = false;
      }
    }
  }

  private void _OnSlotClick(object? sender, EventArgs e) {
    if (sender is PictureBox pb && pb.Tag is int index && index < _totalCount)
      IndexSelected?.Invoke(index);
  }

  private void _PreloadAround(int centerIndex) {
    if (_loader == null || _loadCts == null) return;
    var ct = _loadCts.Token;
    var visible = _VisibleSlotCount();
    var start = Math.Max(0, centerIndex - visible);
    var end = Math.Min(_totalCount, centerIndex + visible + _PRELOAD_AHEAD);

    _ = Task.Run(async () => {
      for (var i = start; i < end; ++i) {
        if (ct.IsCancellationRequested) return;
        if (_cache.ContainsKey(i)) continue;

        var thumb = await _loader(i, ct);
        if (ct.IsCancellationRequested || thumb == null) continue;

        _cache[i] = thumb;

        // Update visible slot on UI thread if this frame is currently visible
        var slotIndex = i - _scrollOffset;
        if (slotIndex >= 0 && slotIndex < _slots.Count) {
          try {
            Invoke(() => {
              if (!ct.IsCancellationRequested && slotIndex < _slots.Count && _slots[slotIndex].Tag is int tag && tag == i)
                _slots[slotIndex].Image = thumb;
            });
          } catch (ObjectDisposedException) { return; }
          catch (InvalidOperationException) { return; }
        }
      }
    }, ct);
  }

  private void _ClearCache() {
    foreach (var bmp in _cache.Values)
      bmp.Dispose();
    _cache.Clear();

    foreach (var slot in _slots)
      slot.Image = null;
  }

  protected override void Dispose(bool disposing) {
    if (disposing) {
      _loadCts?.Cancel();
      _loadCts?.Dispose();
      _ClearCache();
    }
    base.Dispose(disposing);
  }
}
