using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;
using Optimizer.Image;
using ImageRegistry = Hawkynt.FileFormats.Images.FormatRegistry;

namespace Crush.Viewer;

/// <summary>The viewer window: a ribbon, a thumbnail browser, a canvas and an information pane.</summary>
internal sealed class ViewerShell : Form {

  private const int _THUMBNAIL_EDGE = 96;
  private const int _STATUS_HEIGHT = 26;
  private const int _RIBBON_HEIGHT = 148;
  private const string _TITLE = "Crush Viewer";

  private readonly IPlatformBackend _backend;
  private readonly FolderModel _folder = new();
  private readonly ThumbnailLoader _thumbnailLoader;

  private readonly Ribbon _ribbon = new() { Dock = DockStyle.Top };
  private readonly StatusStrip _statusBar = new() { Dock = DockStyle.Bottom, Height = _STATUS_HEIGHT };
  private readonly SplitContainer _outer = new() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
  private readonly SplitContainer _inner = new() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
  private readonly ListView _browser = new() { Dock = DockStyle.Fill, View = ListViewView.LargeIcon, MultiSelect = false };
  private readonly ImageList _thumbnails = new(_THUMBNAIL_EDGE);
  private readonly ViewportPanel _viewport = new() { Dock = DockStyle.Fill, MinZoom = 1.0 / 64, MaxZoom = 64 };
  private readonly TextBox _information = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true };
  private readonly Timer _slideshow = new();

  private readonly ToolStripStatusLabel _statusFormat = new("Ready");
  private readonly ToolStripStatusLabel _statusDimensions = new("");
  private readonly ToolStripStatusLabel _statusBytes = new("");
  private readonly ToolStripStatusLabel _statusPosition = new("") { Spring = true };
  private readonly ToolStripStatusLabel _statusZoom = new("100%");

  private readonly RibbonToggleButton _wrapToggle = new("Wrap around", RibbonItemSize.Large);
  private readonly RibbonToggleButton _slideshowToggle = new("Slideshow", RibbonItemSize.Large);
  private readonly RibbonToggleButton _showBrowser = new("Browser", RibbonItemSize.Small) { Checked = true };
  private readonly RibbonToggleButton _showInformation = new("Information", RibbonItemSize.Small) { Checked = true };
  private readonly RibbonToggleButton _showRulers = new("Rulers", RibbonItemSize.Small);
  private readonly List<(RibbonToggleButton Button, DisplayFilter? Filter)> _filterButtons = [];
  private readonly RibbonButton _previousPage = new("Previous page", RibbonItemSize.Large);
  private readonly RibbonButton _nextPage = new("Next page", RibbonItemSize.Large);
  private readonly RibbonButton _extractPages = new("Extract pages…", RibbonItemSize.Large);

  private IImage? _displayed;
  private RawImage? _raw;
  private FileInfo? _file;
  private FormatEntry? _entry;
  private ImageFormat _format = ImageFormat.Unknown;
  private int _page;
  private int _pageCount = 1;
  private double _pixelAspect = 1;
  private DisplayFilter _formatFilter = DisplayFilter.None;
  private DisplayFilter? _filterOverride;
  private string? _notice;
  private bool _suppressBrowserSelection;
  private bool _suppressFilterButtons;

  internal ViewerShell(IPlatformBackend backend) {
    this._backend = backend;
    this.Text = _TITLE;
    this.ClientSize = new(1280, 820);
    this.MinimumSize = new(720, 520);
    this.StartPosition = FormStartPosition.CenterScreen;

    this._thumbnailLoader = new(_THUMBNAIL_EDGE, this._OnThumbnailDecoded, action => this.BeginInvoke(action));
    this._browser.LargeImageList = this._thumbnails;
    this._browser.SelectedIndexChanged += this._OnBrowserSelectionChanged;
    this._viewport.WalkRequested += (_, delta) => this.Walk(delta);
    this._viewport.PageRequested += (_, delta) => this._GoPage(delta);
    this._viewport.ZoomChanged += (_, _) => this._UpdateStatus();
    this._slideshow.Tick += (_, _) => this._OnSlideshowTick();

    this._BuildRibbon();
    this._BuildBody();
    this._BuildStatusBar();

    this.Controls.Add(this._ribbon);
    this.Controls.Add(this._statusBar);
    this.Controls.Add(this._outer);

    this.Load += (_, _) => this._OnLoad();
    this.FormClosing += (_, _) => this._thumbnailLoader.Cancel();
    this.FormClosed += (_, _) => this._ReleaseImages();
    this._UpdateInformation();
    this._UpdateStatus();
  }

  // ============================================================================================
  // Chrome
  // ============================================================================================

  private void _BuildRibbon() {
    this._ribbon.Tabs.Add(this._BuildFileTab());
    this._ribbon.Tabs.Add(this._BuildNavigateTab());
    this._ribbon.Tabs.Add(this._BuildPagesTab());
    this._ribbon.Tabs.Add(this._BuildViewTab());
    this._ribbon.Tabs.Add(this._BuildPictureTab());
    this._ribbon.SelectedIndex = 0;
    this._ribbon.PreferredHeightChanged += (_, _) => this._SizeRibbon();
    this._SizeRibbon();

    this._ribbon.QuickAccessItems.Add(_Button("Open", () => this._OpenPicturePicker()));
    this._ribbon.QuickAccessItems.Add(_Button("Previous", () => this.Walk(-1)));
    this._ribbon.QuickAccessItems.Add(_Button("Next", () => this.Walk(1)));
  }

  private RibbonTab _BuildFileTab() {
    var tab = new RibbonTab("File");

    var open = new RibbonGroup("Open");
    open.Items.Add(_Button("Picture…", this._OpenPicturePicker, RibbonItemSize.Large));
    open.Items.Add(_Button("Folder…", this._OpenFolderPicker, RibbonItemSize.Large));
    tab.Groups.Add(open);

    var save = new RibbonGroup("Save");
    save.Items.Add(_Button("Save as…", this._SaveAs, RibbonItemSize.Large));
    tab.Groups.Add(save);

    var convert = new RibbonGroup("Convert");
    convert.Items.Add(_Button("One file…", this._ConvertCurrent, RibbonItemSize.Large));
    convert.Items.Add(_Button("Whole folder…", this._ConvertFolder, RibbonItemSize.Large));
    tab.Groups.Add(convert);

    return tab;
  }

  private RibbonTab _BuildNavigateTab() {
    var tab = new RibbonTab("Navigate");

    var walk = new RibbonGroup("Folder");
    walk.Items.Add(_Button("First", () => this._OpenIndex(0), RibbonItemSize.Small));
    walk.Items.Add(_Button("Previous", () => this.Walk(-1), RibbonItemSize.Large));
    walk.Items.Add(_Button("Next", () => this.Walk(1), RibbonItemSize.Large));
    walk.Items.Add(_Button("Last", () => this._OpenIndex(this._folder.Files.Count - 1), RibbonItemSize.Small));
    tab.Groups.Add(walk);

    var ends = new RibbonGroup("At the ends");
    this._wrapToggle.ToolTipText = "On: the last picture is followed by the first. Off: walking stops at the ends.";
    this._wrapToggle.CheckedChanged += (_, _) => {
      this._folder.WalkMode = this._wrapToggle.Checked ? WalkMode.Wrap : WalkMode.Stop;
      this._UpdateStatus();
    };
    ends.Items.Add(this._wrapToggle);
    tab.Groups.Add(ends);

    var show = new RibbonGroup("Slideshow");
    this._slideshowToggle.CheckedChanged += (_, _) => this._ToggleSlideshow();
    show.Items.Add(this._slideshowToggle);
    tab.Groups.Add(show);

    return tab;
  }

  private RibbonTab _BuildPagesTab() {
    var tab = new RibbonTab("Pages");

    var page = new RibbonGroup("Page");
    this._previousPage.Click += (_, _) => this._GoPage(-1);
    this._nextPage.Click += (_, _) => this._GoPage(1);
    page.Items.Add(this._previousPage);
    page.Items.Add(this._nextPage);
    tab.Groups.Add(page);

    var files = new RibbonGroup("Files");
    this._extractPages.ToolTipText = "Write every page of this file into its own picture file.";
    this._extractPages.Click += (_, _) => this._ExtractPages();
    files.Items.Add(this._extractPages);

    var assemble = new RibbonButton("Assemble pages…", RibbonItemSize.Large) {
      Enabled = false,
      ToolTipText = PageService.AssemblyUnavailable,
    };
    files.Items.Add(assemble);
    tab.Groups.Add(files);

    this._UpdatePageButtons();
    return tab;
  }

  private RibbonTab _BuildViewTab() {
    var tab = new RibbonTab("View");

    var zoom = new RibbonGroup("Zoom");
    zoom.Items.Add(_Button("Fit", () => this._viewport.FitToWindow(), RibbonItemSize.Large));
    zoom.Items.Add(_Button("Actual size", () => this._viewport.ActualSize(), RibbonItemSize.Large));
    zoom.Items.Add(_Button("Zoom out", () => this._ZoomBy(1 / Math.Sqrt(2)), RibbonItemSize.Small));
    zoom.Items.Add(_Button("Zoom in", () => this._ZoomBy(Math.Sqrt(2)), RibbonItemSize.Small));
    tab.Groups.Add(zoom);

    var panes = new RibbonGroup("Panes");
    this._showBrowser.CheckedChanged += (_, _) => this._outer.Panel1Collapsed = !this._showBrowser.Checked;
    this._showInformation.CheckedChanged += (_, _) => this._inner.Panel2Collapsed = !this._showInformation.Checked;
    this._showRulers.CheckedChanged += (_, _) => this._viewport.ShowRulers = this._showRulers.Checked;
    panes.Items.Add(this._showBrowser);
    panes.Items.Add(this._showInformation);
    panes.Items.Add(this._showRulers);
    tab.Groups.Add(panes);

    var filters = new RibbonGroup("Display filter");
    this._AddFilterButton(filters, "As the format says", null);
    this._AddFilterButton(filters, "Off", DisplayFilter.None);
    this._AddFilterButton(filters, "NTSC composite", DisplayFilter.NtscComposite);
    this._AddFilterButton(filters, "NTSC S-Video", DisplayFilter.NtscSvideo);
    this._AddFilterButton(filters, "PAL", DisplayFilter.Pal);
    this._filterButtons[0].Button.Checked = true;
    tab.Groups.Add(filters);

    return tab;
  }

  private RibbonTab _BuildPictureTab() {
    var tab = new RibbonTab("Picture");

    var rotate = new RibbonGroup("Rotate");
    rotate.Items.Add(_Button("Right", () => this._Apply(i => ImageTransformer.Rotate(i, RotateAngle.CW90)), RibbonItemSize.Small));
    rotate.Items.Add(_Button("Left", () => this._Apply(i => ImageTransformer.Rotate(i, RotateAngle.CW270)), RibbonItemSize.Small));
    rotate.Items.Add(_Button("Half turn", () => this._Apply(i => ImageTransformer.Rotate(i, RotateAngle.CW180)), RibbonItemSize.Small));
    tab.Groups.Add(rotate);

    var flip = new RibbonGroup("Flip");
    flip.Items.Add(_Button("Horizontally", () => this._Apply(i => ImageTransformer.Flip(i, FlipDirection.Horizontal)), RibbonItemSize.Small));
    flip.Items.Add(_Button("Vertically", () => this._Apply(i => ImageTransformer.Flip(i, FlipDirection.Vertical)), RibbonItemSize.Small));
    tab.Groups.Add(flip);

    var adjust = new RibbonGroup("Adjust");
    adjust.Items.Add(_Button("Resize…", this._Resize, RibbonItemSize.Large));
    adjust.Items.Add(_Button("Reduce colours…", this._ReduceColours, RibbonItemSize.Large));
    tab.Groups.Add(adjust);

    return tab;
  }

  private void _AddFilterButton(RibbonGroup group, string text, DisplayFilter? filter) {
    var button = new RibbonToggleButton(text, RibbonItemSize.Small);
    button.CheckedChanged += (_, _) => {
      if (this._suppressFilterButtons)
        return;

      // The toolkit's toggle flips on every click, so clicking the filter that is already on would
      // turn it off and leave the whole group unchecked while that filter was still being applied.
      // Exactly one of them is on at all times, which is what makes the group mean anything.
      if (!button.Checked) {
        this._suppressFilterButtons = true;
        button.Checked = true;
        this._suppressFilterButtons = false;
        return;
      }

      this._suppressFilterButtons = true;
      foreach (var (other, _) in this._filterButtons)
        if (!ReferenceEquals(other, button))
          other.Checked = false;
      this._suppressFilterButtons = false;

      this._filterOverride = filter;
      this._Render();
      this._UpdateInformation();
    };

    this._filterButtons.Add((button, filter));
    group.Items.Add(button);
  }

  private void _BuildBody() {
    this._inner.Panel1.Controls.Add(this._viewport);
    this._inner.Panel2.Controls.Add(this._information);
    this._outer.Panel1.Controls.Add(this._browser);
    this._outer.Panel2.Controls.Add(this._inner);
    this._outer.Panel1MinSize = 160;
    this._outer.Panel2MinSize = 320;
    this._inner.Panel1MinSize = 240;
    this._inner.Panel2MinSize = 180;
  }

  private void _BuildStatusBar() {
    this._statusBar.Items.Add(this._statusFormat);
    this._statusBar.Items.Add(this._statusDimensions);
    this._statusBar.Items.Add(this._statusBytes);
    this._statusBar.Items.Add(this._statusPosition);
    this._statusBar.Items.Add(this._statusZoom);
  }

  private static RibbonButton _Button(string text, Action action, RibbonItemSize size = RibbonItemSize.Small) {
    var button = new RibbonButton(text, size);
    button.Click += (_, _) => action();
    return button;
  }

  private void _OnLoad() {
    this._outer.SplitterDistance = 270;
    this._inner.SplitterDistance = Math.Max(320, this._inner.Width - 330);
    this._SizeRibbon();
  }

  /// <summary>
  /// Gives the ribbon a height its groups fit into.
  /// </summary>
  /// <remarks>
  /// The control reports a preferred height of the tab strip alone until it has been given room to
  /// lay its groups out, so asking it first and believing the answer collapses the ribbon to its
  /// tabs. Starting from a height a group row fits into and only growing to what it then asks for
  /// breaks that circle.
  /// </remarks>
  private void _SizeRibbon()
    => this._ribbon.Height = Math.Max(_RIBBON_HEIGHT, this._ribbon.PreferredHeight);

  // ============================================================================================
  // Opening
  // ============================================================================================

  /// <summary>Opens a file or a folder, whichever the path names.</summary>
  internal void OpenPath(string path) {
    if (Directory.Exists(path))
      this.OpenFolder(new(path), null);
    else if (File.Exists(path))
      this.OpenFile(new(path));
  }

  /// <summary>Lists a folder and shows the given picture, or the first one.</summary>
  internal void OpenFolder(DirectoryInfo folder, FileInfo? select) {
    this._folder.Open(folder, select);
    this._RefreshBrowser();
    if (this._folder.Index >= 0)
      this._Load(this._folder.Files[this._folder.Index]);
    else if (this._folder.Files.Count > 0)
      this._OpenIndex(0);
  }

  /// <summary>Shows one picture and lists the folder it sits in.</summary>
  internal void OpenFile(FileInfo file) {
    var folder = file.Directory;
    if (folder != null && (this._folder.Folder == null
        || !string.Equals(this._folder.Folder.FullName, folder.FullName, StringComparison.OrdinalIgnoreCase))) {
      this._folder.Open(folder, file);
      this._RefreshBrowser();
    } else {
      this._folder.Select(file);
      this._SyncBrowserSelection();
    }

    this._Load(file);
  }

  private void _OpenPicturePicker() {
    var dialog = new OpenFileDialog {
      Title = "Open picture",
      Filter = _ReadableFilter(),
      InitialDirectory = this._folder.Folder?.FullName ?? "",
    };

    if (dialog.ShowDialog() == DialogResult.OK && File.Exists(dialog.FileName))
      this.OpenFile(new(dialog.FileName));
  }

  private void _OpenFolderPicker() {
    var dialog = new FolderBrowserDialog {
      Title = "Open a folder of pictures",
      SelectedPath = this._folder.Folder?.FullName ?? "",
    };

    if (dialog.ShowDialog() == DialogResult.OK && Directory.Exists(dialog.SelectedPath))
      this.OpenFolder(new(dialog.SelectedPath), null);
  }

  private void _OpenIndex(int index) {
    if ((uint)index >= (uint)this._folder.Files.Count)
      return;

    this._folder.SelectIndex(index);
    this._SyncBrowserSelection();
    this._Load(this._folder.Files[index]);
  }

  /// <summary>Moves to the neighbouring picture, honouring the wrap setting.</summary>
  internal void Walk(int delta) {
    var next = this._folder.Step(delta);
    if (next < 0 || next == this._folder.Index)
      return;

    this._OpenIndex(next);
  }

  private void _Load(FileInfo file) {
    try {
      var format = ImageRegistry.DetectFromFile(file);
      var entry = ImageRegistry.GetEntry(format);
      var raw = ImageRegistry.Read(file);
      if (raw == null) {
        this._Note($"{file.Name}: no registered reader could decode it ({format}).");
        return;
      }

      this._file = file;
      this._format = format;
      this._entry = entry;
      this._raw = raw;
      this._page = 0;
      this._pageCount = PageService.PageCount(file, entry);
      this._PickVideoMode();
      this._Render();
      this._viewport.FitToWindow();
      this._UpdatePageButtons();
      this._UpdateInformation();
      this._notice = null;
      this._UpdateStatus();
    } catch (Exception ex) {
      this._Note($"{file.Name}: {ex.Message}");
    }
  }

  private void _GoPage(int delta) {
    if (this._file == null || this._pageCount < 2)
      return;

    var page = Math.Clamp(this._page + delta, 0, this._pageCount - 1);
    if (page == this._page)
      return;

    try {
      var raw = PageService.LoadPage(this._file, this._entry, page);
      if (raw == null) {
        this._Note($"Page {page + 1} could not be decoded.");
        return;
      }

      this._raw = raw;
      this._page = page;
      this._PickVideoMode();
      this._Render();
      this._UpdatePageButtons();
      this._UpdateInformation();
      this._notice = null;
      this._UpdateStatus();
    } catch (Exception ex) {
      this._Note($"Page {page + 1}: {ex.Message}");
    }
  }

  // ============================================================================================
  // Browser
  // ============================================================================================

  private void _RefreshBrowser() {
    this._thumbnailLoader.Cancel();
    this._suppressBrowserSelection = true;
    this._browser.Items.Clear();
    this._thumbnails.Clear();

    foreach (var file in this._folder.Files)
      this._browser.Items.Add(new ListViewItem(file.Name) { ImageIndex = -1, Tag = file });

    this._browser.SelectedIndex = this._folder.Index;
    this._suppressBrowserSelection = false;
    this._thumbnailLoader.Start(this._folder.Files);
  }

  private void _SyncBrowserSelection() {
    this._suppressBrowserSelection = true;
    this._browser.SelectedIndex = this._folder.Index;
    this._suppressBrowserSelection = false;
  }

  private void _OnThumbnailDecoded(int index, int[] pixels) {
    if (index >= this._browser.Items.Count)
      return;

    var slot = this._thumbnails.Add(pixels);
    this._browser.Items[index].ImageIndex = slot;
    this._browser.Invalidate();
  }

  private void _OnBrowserSelectionChanged(object? sender, EventArgs e) {
    if (this._suppressBrowserSelection)
      return;

    var index = this._browser.SelectedIndex;
    if (index < 0 || index == this._folder.Index)
      return;

    this._folder.SelectIndex(index);
    this._Load(this._folder.Files[index]);
  }

  // ============================================================================================
  // Rendering
  // ============================================================================================

  private void _PickVideoMode() {
    this._pixelAspect = 1;
    this._formatFilter = DisplayFilter.None;
    if (this._raw == null || this._entry?.VideoModes is not { Length: > 0 } modes)
      return;

    var mode = modes.FirstOrDefault(m => m.MatchesDimensions(this._raw.Width, this._raw.Height)) ?? modes[0];
    this._pixelAspect = mode.PixelAspectRatio?.Ratio ?? 1;
    this._formatFilter = mode.DisplayFilter;
  }

  private void _Render() {
    if (this._raw == null) {
      this._ReleaseDisplayed();
      return;
    }

    var shown = DisplayFilterPipeline.Apply(this._raw, this._filterOverride ?? this._formatFilter);
    shown = _ApplyPixelAspect(shown, this._pixelAspect);

    var previous = this._displayed;
    this._displayed = ImageBridge.ToImage(this._backend, shown);
    this._viewport.Image = this._displayed;
    previous?.Dispose();
    this._UpdateStatus();
  }

  /// <summary>Widens a picture whose pixels are not square, so it is shown at its real shape.</summary>
  private static RawImage _ApplyPixelAspect(RawImage source, double aspect) {
    if (Math.Abs(aspect - 1) < 0.001)
      return source;

    var width = Math.Max(1, (int)Math.Round(source.Width * aspect));
    return width == source.Width
      ? source
      : ImageTransformer.Resize(source, width, source.Height, ResizeMode.Stretch, InterpolationHint.Bilinear);
  }

  private void _ZoomBy(double factor) {
    var zoom = Math.Clamp(this._viewport.Zoom * factor, this._viewport.MinZoom, this._viewport.MaxZoom);
    this._viewport.ZoomTo(zoom, this._viewport.Width / 2, this._viewport.Height / 2);
    this._UpdateStatus();
  }

  // ============================================================================================
  // Editing
  // ============================================================================================

  private void _Apply(Func<RawImage, RawImage> transform) {
    if (this._raw == null)
      return;

    try {
      this._raw = transform(this._raw);
      this._Render();
      this._UpdateInformation();
      this._UpdateStatus();
    } catch (Exception ex) {
      this._Complain("Picture", ex.Message);
    }
  }

  private void _Resize() {
    if (this._raw == null)
      return;

    var dialog = new ResizeOptionsDialog(this._raw.Width, this._raw.Height, ImageTransformer.GuessInterpolation(this._raw));
    if (dialog.ShowDialog(this) != DialogResult.OK)
      return;

    this._Apply(i => ImageTransformer.Resize(i, dialog.TargetWidth, dialog.TargetHeight, dialog.Mode, dialog.Interpolation));
  }

  private void _ReduceColours() {
    if (this._raw == null)
      return;

    var current = this._raw.IsIndexed ? Math.Clamp(this._raw.PaletteCount, 2, 256) : 256;
    var dialog = new NumberDialog("Reduce colours", "Median Cut with Floyd-Steinberg dithering.", "Colours", 2, 256, current, "Reduce");
    if (dialog.ShowDialog(this) != DialogResult.OK)
      return;

    this._Apply(i => BitmapConverter.QuantizeRawImage(i, dialog.Value));
  }

  // ============================================================================================
  // Writing
  // ============================================================================================

  private void _SaveAs() {
    if (this._raw == null)
      return;

    // One filter listing every writable extension rather than one filter per writer: there are
    // hundreds of writers, and a file chooser with a drop-down that long is not a picker any more.
    // The extension that gets typed chooses the writer, and the Convert tab has the proper list.
    var writable = ImageRegistry.SupportedWriteFormats.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    var patterns = writable.SelectMany(e => e.AllExtensions).Distinct(StringComparer.OrdinalIgnoreCase)
      .OrderBy(e => e, StringComparer.OrdinalIgnoreCase).Select(e => $"*{e}");
    var dialog = new SaveFileDialog {
      Title = "Save picture as",
      Filter = $"Writable formats|{string.Join(";", patterns)}|All files|*.*",
      InitialDirectory = this._folder.Folder?.FullName ?? "",
      FileName = Path.GetFileNameWithoutExtension(this._file?.Name ?? "picture") + (this._entry?.PrimaryExtension ?? ".png"),
    };

    if (dialog.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.FileName))
      return;

    var target = new FileInfo(dialog.FileName);
    var extension = target.Extension;
    var entry = writable.FirstOrDefault(e => e.AllExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
    if (entry == null) {
      this._Complain("Save picture", $"No registered writer claims '{extension}'. Use Convert for a full list of formats.");
      return;
    }

    var result = ConversionService.Write(this._raw, entry, target, this._file);
    if (!result.Succeeded) {
      this._Complain("Save picture", result.Message);
      return;
    }

    if (this._folder.Folder != null && target.Directory != null
        && string.Equals(this._folder.Folder.FullName, target.Directory.FullName, StringComparison.OrdinalIgnoreCase)) {
      this._folder.Refresh();
      this._RefreshBrowser();
    }

    this._UpdateStatus();
  }

  private void _ConvertCurrent() {
    if (this._file == null || this._raw == null) {
      this._Complain("Convert", "Open a picture first.");
      return;
    }

    var dialog = new ConvertOptionsDialog(
      "Convert this picture",
      $"Re-encodes {this._file.Name} through the registry's writers.",
      "Convert",
      this._folder.Folder?.FullName ?? this._file.DirectoryName ?? "",
      this._format);
    if (dialog.ShowDialog(this) != DialogResult.OK)
      return;

    var target = new FileInfo(Path.Combine(
      dialog.Destination.FullName,
      Path.GetFileNameWithoutExtension(this._file.Name) + dialog.Target.PrimaryExtension));
    var result = ConversionService.Convert(this._file, dialog.Target, target, dialog.Overwrite);
    ConvertOptionsDialog.ShowReport(this, "Convert this picture", [result]);
    this._folder.Refresh();
    this._RefreshBrowser();
  }

  private void _ConvertFolder() {
    if (this._folder.Files.Count == 0) {
      this._Complain("Convert", "Open a folder of pictures first.");
      return;
    }

    var dialog = new ConvertOptionsDialog(
      "Convert the whole folder",
      $"Converts all {this._folder.Files.Count} readable picture(s) in {this._folder.Folder?.Name}.",
      "Convert all",
      Path.Combine(this._folder.Folder!.FullName, "converted"),
      this._format);
    if (dialog.ShowDialog(this) != DialogResult.OK)
      return;

    var results = ConversionService.ConvertBatch(this._folder.Files, dialog.Target, dialog.Destination, dialog.Overwrite);
    ConvertOptionsDialog.ShowReport(this, "Convert the whole folder", results);
  }

  private void _ExtractPages() {
    if (this._file == null) {
      this._Complain("Pages", "Open a picture first.");
      return;
    }

    var dialog = new ConvertOptionsDialog(
      "Extract pages",
      $"{this._file.Name} holds {this._pageCount} page(s); each becomes its own file.",
      "Extract",
      Path.Combine(this._folder.Folder?.FullName ?? this._file.DirectoryName ?? ".", "pages"),
      this._format);
    if (dialog.ShowDialog(this) != DialogResult.OK)
      return;

    var results = PageService.ExtractPages(this._file, this._entry, dialog.Target, dialog.Destination, dialog.Overwrite);
    ConvertOptionsDialog.ShowReport(this, "Extract pages", results);
  }

  // ============================================================================================
  // Slideshow
  // ============================================================================================

  private void _ToggleSlideshow() {
    if (!this._slideshowToggle.Checked) {
      this._slideshow.Stop();
      return;
    }

    if (this._folder.Files.Count < 2) {
      this._slideshowToggle.Checked = false;
      return;
    }

    var dialog = new NumberDialog("Slideshow", "Steps through the folder on a timer.", "Seconds per picture", 1, 600, 3, "Start");
    if (dialog.ShowDialog(this) != DialogResult.OK) {
      this._slideshowToggle.Checked = false;
      return;
    }

    this._slideshow.Interval = dialog.Value * 1000;
    this._slideshow.Start();
  }

  private void _OnSlideshowTick() {
    var next = this._folder.Step(1);
    if (next < 0) {
      this._slideshow.Stop();
      this._slideshowToggle.Checked = false;
      return;
    }

    this._OpenIndex(next);
  }

  // ============================================================================================
  // Readouts
  // ============================================================================================

  private void _UpdatePageButtons() {
    var multi = this._pageCount > 1;
    this._previousPage.Enabled = multi && this._page > 0;
    this._nextPage.Enabled = multi && this._page < this._pageCount - 1;
    this._extractPages.Enabled = this._file != null;
  }

  private void _UpdateStatus() {
    this._statusFormat.Text = this._notice ?? this._entry?.Name ?? "Ready";
    this._statusDimensions.Text = this._raw == null ? "" : $"{this._raw.Width} x {this._raw.Height}";
    this._statusBytes.Text = this._file is { Exists: true } file ? _FormatSize(file.Length) : "";
    this._statusZoom.Text = $"{this._viewport.Zoom * 100:0.#}%";

    var walk = this._folder.WalkMode == WalkMode.Wrap ? "wrapping" : "stopping at the ends";
    var position = this._folder.Files.Count == 0
      ? walk
      : $"picture {this._folder.Index + 1}/{this._folder.Files.Count}, {walk}";
    this._statusPosition.Text = this._pageCount > 1 ? $"{position} — page {this._page + 1}/{this._pageCount}" : position;

    this.Text = this._file == null ? _TITLE : $"{this._file.Name} — {_TITLE}";
  }

  private void _UpdateInformation() {
    if (this._raw == null || this._file == null) {
      this._information.Text = "Open a picture or a folder from the File tab.";
      return;
    }

    var image = this._raw;
    var text = new StringBuilder();
    text.AppendLine($"File: {this._file.Name}");
    text.AppendLine($"Path: {this._file.FullName}");
    text.AppendLine($"Format: {this._entry?.Name ?? this._format.ToString()} ({this._format})");
    text.AppendLine($"Size on disk: {(this._file.Exists ? _FormatSize(this._file.Length) : "unknown")}");
    text.AppendLine($"Dimensions: {image.Width} x {image.Height}");
    text.AppendLine($"Pixel format: {image.Format}");
    text.AppendLine($"Alpha: {(image.HasAlpha ? "yes" : "no")}");
    text.AppendLine($"Indexed: {(image.IsIndexed ? $"yes, {image.PaletteCount} colours" : "no")}");
    text.AppendLine($"Pages: {this._pageCount}");
    text.AppendLine($"Writable: {(this._entry?.SupportsWrite == true ? "yes" : "no")}");
    text.AppendLine($"Pixel aspect: {this._pixelAspect:0.###}:1");
    text.AppendLine($"Display filter: {this._filterOverride?.ToString() ?? $"as the format says ({this._formatFilter})"}");

    if (image.Metadata is { IsEmpty: false } metadata) {
      text.AppendLine();
      text.AppendLine("Metadata");
      if (metadata.DpiX != null || metadata.DpiY != null)
        text.AppendLine($"DPI: {metadata.DpiX?.ToString("0.##", CultureInfo.InvariantCulture) ?? "?"} x {metadata.DpiY?.ToString("0.##", CultureInfo.InvariantCulture) ?? "?"}");
      if (metadata.IccProfile != null)
        text.AppendLine($"ICC: {metadata.IccProfileName ?? "embedded profile"} ({_FormatSize(metadata.IccProfile.LongLength)})");
      if (metadata.Exif != null)
        text.AppendLine("EXIF: present");
      if (metadata.Iptc != null)
        text.AppendLine("IPTC: present");
      if (metadata.XmpPacket != null)
        text.AppendLine($"XMP: {_FormatSize(metadata.XmpPacket.LongLength)}");
      foreach (var entry in metadata.TextEntries.Take(24))
        text.AppendLine($"{(string.IsNullOrEmpty(entry.Keyword) ? "Comment" : entry.Keyword)}: {entry.Text}");
      if (metadata.TextEntries.Count > 24)
        text.AppendLine($"… and {metadata.TextEntries.Count - 24} more text entries");
    }

    this._information.Text = text.ToString();
  }

  /// <summary>Hands the platform back every image this window had realised.</summary>
  /// <remarks>
  /// Neither the list's image list nor the canvas owns what is put into them, and a form has no
  /// disposal of its own to hang this on, so the window releases them when it closes.
  /// </remarks>
  private void _ReleaseImages() {
    this._ReleaseDisplayed();
    this._thumbnails.Dispose();
  }

  private void _ReleaseDisplayed() {
    this._viewport.Image = null;
    this._displayed?.Dispose();
    this._displayed = null;
  }

  private void _Complain(string caption, string message)
    => MessageBox.Show(this, message, caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);

  /// <summary>Reports a failure in the status bar rather than in a dialog.</summary>
  /// <remarks>
  /// Opening files is not always something the user asked for one file at a time: walking a folder,
  /// running a slideshow and restoring a selection all open pictures on their own, and one that will
  /// not decode must not stop the application with a box somebody has to dismiss. A dialog here also
  /// wedges a run that has no one to dismiss it, which is exactly what the start-up check is.
  /// </remarks>
  private void _Note(string message) {
    this._notice = message;
    this._UpdateStatus();
  }

  private static string _ReadableFilter() {
    var patterns = FolderModel.ReadableExtensions.Select(e => $"*{e}").OrderBy(e => e, StringComparer.OrdinalIgnoreCase);
    return $"Pictures|{string.Join(";", patterns)}|All files|*.*";
  }

  private static string _FormatSize(long bytes) {
    string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
    var value = (double)Math.Max(0, bytes);
    var unit = 0;
    while (value >= 1024 && unit < units.Length - 1) {
      value /= 1024;
      ++unit;
    }

    return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.##} {units[unit]}";
  }
}
