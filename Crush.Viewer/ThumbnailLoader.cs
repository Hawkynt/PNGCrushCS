using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hawkynt.FileFormats.Images;

namespace Crush.Viewer;

/// <summary>Decodes a folder's pictures down to icon size without holding up the window.</summary>
/// <remarks>
/// Results are handed back strictly in listing order. The toolkit's <c>ImageList</c> only appends —
/// there is no way to replace entry <c>n</c> once it is in — so the only way for an item's image
/// index to be its position in the folder is to add exactly one entry per file, in order, including
/// a blank one for a file that would not decode.
/// </remarks>
internal sealed class ThumbnailLoader : IDisposable {

  private readonly int _edge;
  private readonly Action<int, int[]> _onDecoded;
  private readonly Action<Action> _toUiThread;
  private CancellationTokenSource? _cancellation;

  /// <param name="edge">Side length in pixels of the square each thumbnail is fitted into.</param>
  /// <param name="onDecoded">Called on the UI thread with the file's position and its pixels.</param>
  /// <param name="toUiThread">Marshals a callback onto the thread that owns the window.</param>
  internal ThumbnailLoader(int edge, Action<int, int[]> onDecoded, Action<Action> toUiThread) {
    this._edge = edge;
    this._onDecoded = onDecoded;
    this._toUiThread = toUiThread;
  }

  /// <summary>Abandons the folder being decoded and starts on a new one.</summary>
  internal void Start(IReadOnlyList<FileInfo> files) {
    this.Cancel();
    if (files.Count == 0)
      return;

    var cancellation = new CancellationTokenSource();
    this._cancellation = cancellation;
    var token = cancellation.Token;
    var snapshot = new FileInfo[files.Count];
    for (var i = 0; i < files.Count; ++i)
      snapshot[i] = files[i];

    _ = Task.Run(() => this._Decode(snapshot, token), token);
  }

  /// <summary>Abandons whatever folder is being decoded.</summary>
  internal void Cancel() {
    var previous = Interlocked.Exchange(ref this._cancellation, null);
    if (previous == null)
      return;

    try {
      previous.Cancel();
    } catch (ObjectDisposedException) {
      return;
    }

    previous.Dispose();
  }

  private void _Decode(IReadOnlyList<FileInfo> files, CancellationToken token) {
    var blank = new int[this._edge * this._edge];

    for (var i = 0; i < files.Count; ++i) {
      if (token.IsCancellationRequested)
        return;

      int[] pixels;
      try {
        var raw = FormatRegistry.Read(files[i]);
        pixels = raw == null ? blank : ImageBridge.ToThumbnailArgb(raw, this._edge);
      } catch {
        pixels = blank;
      }

      if (token.IsCancellationRequested)
        return;

      var index = i;
      var decoded = pixels;
      try {
        this._toUiThread(() => {
          if (!token.IsCancellationRequested)
            this._onDecoded(index, decoded);
        });
      } catch (Exception) {
        // The window went away between the decode and the hand-off. Nothing is left to show it to.
        return;
      }
    }
  }

  public void Dispose() => this.Cancel();
}
