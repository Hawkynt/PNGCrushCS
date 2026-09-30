using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Core;
using Hawkynt.ColorProcessing.Adapter;
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

  /// <summary>How many thumbnails around the shown picture are decoded ahead of the rest.</summary>
  private const int _THUMBNAIL_LOOKAHEAD = 24;

  private readonly IPlatformBackend _backend;
  private readonly FolderModel _folder = new();
  private readonly ThumbnailLoader _thumbnailLoader;
  private readonly ShortcutTable _shortcuts = new();
  private readonly IconSet _icons = new();

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

  private readonly RibbonToggleButton _wrapToggle = new("Wrap around", RibbonItemSize.Small);
  private readonly RibbonToggleButton _slideshowToggle = new("Slideshow", RibbonItemSize.Large);
  private readonly RibbonToggleButton _showBrowser = new("Thumbnails", RibbonItemSize.Small) { Checked = true };
  private readonly RibbonToggleButton _showInformation = new("Information", RibbonItemSize.Small) { Checked = true };
  private readonly RibbonToggleButton _showRulers = new("Rulers", RibbonItemSize.Small);
  private readonly List<(RibbonToggleButton Button, DisplayFilter? Filter)> _filterButtons = [];
  private readonly RibbonButton _save = new("Save", RibbonItemSize.Large);
  private readonly RibbonButton _print = new("Print…", RibbonItemSize.Large);
  private readonly RibbonButton _previousPage = new("Previous page", RibbonItemSize.Small);
  private readonly RibbonButton _nextPage = new("Next page", RibbonItemSize.Small);
  private readonly RibbonButton _extractPages = new("Extract pages…", RibbonItemSize.Small);

  private IImage? _displayed;
  private RawImage? _raw;
  private FileInfo? _file;
  private FormatEntry? _entry;
  private ImageFormat _format = ImageFormat.Unknown;
  private int _page;
  private int _pageCount = 1;
  private double _pixelAspect = 1;
  private bool _edited;
  private DisplayFilter _formatFilter = DisplayFilter.None;
  private DisplayFilter? _filterOverride;
  private string? _notice;
  private bool _suppressBrowserSelection;
  private bool _suppressFilterButtons;
  private ZoomMode _zoomMode = ZoomMode.Fit;
  private double _freeZoom = 1;
  private int _zoomGuard;
  private bool _fullScreen;
  private FormBorderStyle _framedBorderStyle;
  private FormWindowState _framedWindowState;

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
    this._viewport.ZoomChanged += (_, _) => this._OnZoomChanged();
    this._slideshow.Tick += (_, _) => this._OnSlideshowTick();

    this._BuildRibbon();
    this._BuildBody();
    this._BuildStatusBar();
    this._BuildShortcuts();

    this.Controls.Add(this._ribbon);
    this.Controls.Add(this._statusBar);
    this.Controls.Add(this._outer);
    this.Controls.Add(this._shortcuts.Carrier);

    // The canvas rather than the ribbon, which is what a window otherwise gives its first tab stop
    // to. A focused ribbon claims Left and Right for walking its tabs — the one place the toolkit
    // takes a key before the form-wide table can see it — so the ribbon holds the focus only while
    // somebody is actually using it.
    this.ActiveControl = this._viewport;

    this.Load += (_, _) => this._OnLoad();
    this.FormClosing += (_, _) => this._thumbnailLoader.Cancel();
    this.FormClosed += (_, _) => this._ReleaseImages();
    this._UpdateInformation();
    this._UpdateStatus();
  }

  // ============================================================================================
  // Chrome
  // ============================================================================================

  /// <summary>Lays the commands out in six tabs, by what a command does to or with the picture.</summary>
  /// <remarks>
  /// <para>
  /// The split is the one an image application is expected to have: the file and the pictures around
  /// it, then how this one is being looked at and shaped, then colour, then size, then repairs, then
  /// effects. Walking the folder and paging through a document both live on File rather than on a tab
  /// of their own, because both are about which picture is on screen rather than about the picture —
  /// the same reason the thumbnail browser's toggle sits next to them instead of next to the other
  /// pane toggles on View.
  /// </para>
  /// <para>
  /// Geometry is on View with the zoom for the same reason the other way round: rotating, mirroring,
  /// cropping, adding a margin and letterboxing all change where the picture's own pixels sit in the
  /// frame, and somebody who has just turned a photograph upright reaches for the fit button next.
  /// Resizing is not there but on its own tab, because that one resamples and the resampler is a
  /// choice in its own right.
  /// </para>
  /// </remarks>
  private void _BuildRibbon() {
    this._ribbon.Tabs.Add(this._BuildFileTab());
    this._ribbon.Tabs.Add(this._BuildViewTab());
    this._ribbon.Tabs.Add(this._BuildColourTab());
    this._ribbon.Tabs.Add(this._BuildResizeTab());
    this._ribbon.Tabs.Add(this._BuildCorrectionsTab());
    this._ribbon.Tabs.Add(this._BuildEffectsTab());
    this._ribbon.SelectedIndex = 0;
    this._ribbon.PreferredHeightChanged += (_, _) => this._SizeRibbon();
    this._SizeRibbon();

    this._ribbon.QuickAccessItems.Add(this._Button("Open", () => this._OpenPicturePicker(), icon: "folder_image"));
    this._ribbon.QuickAccessItems.Add(this._Button("Previous", () => this.Walk(-1), icon: "resultset_previous"));
    this._ribbon.QuickAccessItems.Add(this._Button("Next", () => this.Walk(1), icon: "resultset_next"));
  }

  private RibbonTab _BuildFileTab() {
    var tab = new RibbonTab("File");

    var open = new RibbonGroup("Open");
    open.Items.Add(this._Button("Picture…", this._OpenPicturePicker, RibbonItemSize.Large, "document-open"));
    open.Items.Add(this._Button("Folder…", this._OpenFolderPicker, RibbonItemSize.Large, "document-open-folder"));
    tab.Groups.Add(open);

    var browse = new RibbonGroup("Browse");
    this._icons.Apply(this._slideshowToggle, "media-playback-start", RibbonItemSize.Large);
    this._slideshowToggle.ToolTipText = "Steps through the folder on a timer, at a rate it asks for.";
    this._slideshowToggle.CheckedChanged += (_, _) => this._ToggleSlideshow();
    browse.Items.Add(this._slideshowToggle);

    this._icons.Apply(this._showBrowser, "application_view_gallery");
    this._showBrowser.ToolTipText = "Shows or hides the thumbnails of the folder down the left-hand side.";
    this._showBrowser.CheckedChanged += (_, _) => this._outer.Panel1Collapsed = !this._showBrowser.Checked;
    browse.Items.Add(this._showBrowser);

    this._icons.Apply(this._wrapToggle, "media-playlist-repeat");
    this._wrapToggle.ToolTipText = "On: the last picture is followed by the first. Off: walking stops at the ends.";
    this._wrapToggle.CheckedChanged += (_, _) => {
      this._folder.WalkMode = this._wrapToggle.Checked ? WalkMode.Wrap : WalkMode.Stop;
      this._UpdateStatus();
    };
    browse.Items.Add(this._wrapToggle);

    browse.Items.Add(this._Button("First", () => this._OpenIndex(0), RibbonItemSize.Small, "resultset_first"));
    browse.Items.Add(this._Button("Previous", () => this.Walk(-1), RibbonItemSize.Small, "resultset_previous"));
    browse.Items.Add(this._Button("Next", () => this.Walk(1), RibbonItemSize.Small, "resultset_next"));
    browse.Items.Add(this._Button("Last", () => this._OpenIndex(this._folder.Files.Count - 1), RibbonItemSize.Small, "resultset_last"));
    tab.Groups.Add(browse);

    var pages = new RibbonGroup("Pages");
    this._icons.Apply(this._previousPage, "go-previous-view-page");
    this._icons.Apply(this._nextPage, "go-next-view-page");
    this._previousPage.Click += (_, _) => this._GoPage(-1);
    this._nextPage.Click += (_, _) => this._GoPage(1);
    pages.Items.Add(this._previousPage);
    pages.Items.Add(this._nextPage);

    this._icons.Apply(this._extractPages, "split");
    this._extractPages.ToolTipText = "Writes every page of this file into its own picture file.";
    this._extractPages.Click += (_, _) => this._ExtractPages();
    pages.Items.Add(this._extractPages);

    var assemble = new RibbonButton("Assemble pages…", RibbonItemSize.Small) {
      Enabled = false,
      ToolTipText = PageService.AssemblyUnavailable,
    };
    this._icons.Apply(assemble, "merge");
    pages.Items.Add(assemble);
    tab.Groups.Add(pages);

    var save = new RibbonGroup("Save");
    this._save.ToolTipText = "Writes the edited picture back over the file it came from.";
    this._save.Click += (_, _) => this._Save();
    this._icons.Apply(this._save, "document-save", RibbonItemSize.Large);
    save.Items.Add(this._save);
    save.Items.Add(this._Button("Save as…", this._SaveAs, RibbonItemSize.Small, "document-save-as"));
    tab.Groups.Add(save);

    var convert = new RibbonGroup("Convert");
    convert.Items.Add(this._Button("One file…", this._ConvertCurrent, RibbonItemSize.Small, "document-export"));
    convert.Items.Add(this._Button("Whole folder…", this._ConvertFolder, RibbonItemSize.Small, "folder-sync"));
    tab.Groups.Add(convert);

    var devices = new RibbonGroup("Print and scan");
    this._icons.Apply(this._print, "document-print", RibbonItemSize.Large);
    this._print.ToolTipText = PrintService.Unavailable
      ?? "Writes the picture as a PDF with this suite's own writer and hands it to the platform to print.";
    this._print.Click += (_, _) => this._Print();
    devices.Items.Add(this._print);

    var acquire = new RibbonButton("Scan…", RibbonItemSize.Large) {
      Enabled = false,
      ToolTipText = PlannedCommands.AcquireUnavailable,
    };
    this._icons.Apply(acquire, "scanner", RibbonItemSize.Large);
    devices.Items.Add(acquire);
    tab.Groups.Add(devices);

    var about = new RibbonGroup("Help");
    about.Items.Add(this._Button("About", this._About, RibbonItemSize.Small, "help"));
    tab.Groups.Add(about);

    this._UpdatePageButtons();
    return tab;
  }

  /// <remarks>
  /// The three signal standards among the display filters go without artwork on purpose: no icon set
  /// draws NTSC, S-Video and PAL as three different things, and one television on all three would be
  /// a picture of the group rather than of the choice.
  /// </remarks>
  private RibbonTab _BuildViewTab() {
    var tab = new RibbonTab("View");

    var zoom = new RibbonGroup("Zoom");
    zoom.Items.Add(this._Button("Fit", this._FitToWindow, RibbonItemSize.Large, "zoom-fit-best"));
    zoom.Items.Add(this._Button("Actual size", this._ActualSize, RibbonItemSize.Small, "zoom-original"));
    zoom.Items.Add(this._Button("Zoom out", () => this._ZoomBy(1 / Math.Sqrt(2)), RibbonItemSize.Small, "zoom_out"));
    zoom.Items.Add(this._Button("Zoom in", () => this._ZoomBy(Math.Sqrt(2)), RibbonItemSize.Small, "zoom_in"));
    tab.Groups.Add(zoom);

    var panes = new RibbonGroup("Panes");
    this._icons.Apply(this._showInformation, "information");
    this._icons.Apply(this._showRulers, "measure");
    this._showInformation.CheckedChanged += (_, _) => this._inner.Panel2Collapsed = !this._showInformation.Checked;
    this._showRulers.CheckedChanged += (_, _) => this._viewport.ShowRulers = this._showRulers.Checked;
    panes.Items.Add(this._showInformation);
    panes.Items.Add(this._showRulers);

    // A plain button rather than a toggle: full screen hides the ribbon, so a checked state nobody
    // can see is state to keep in step for nothing. Escape and F11 both come back out.
    panes.Items.Add(this._Button("Full screen", this._ToggleFullScreen, RibbonItemSize.Small, "view-fullscreen"));
    tab.Groups.Add(panes);

    var rotate = new RibbonGroup("Rotate");
    rotate.Items.Add(this._Button("Right", () => this._Apply(i => ImageTransformer.Rotate(i, RotateAngle.CW90)), RibbonItemSize.Small, "arrow_rotate_clockwise"));
    rotate.Items.Add(this._Button("Left", () => this._Apply(i => ImageTransformer.Rotate(i, RotateAngle.CW270)), RibbonItemSize.Small, "arrow_rotate_anticlockwise"));
    rotate.Items.Add(this._Button("Half turn", () => this._Apply(i => ImageTransformer.Rotate(i, RotateAngle.CW180)), RibbonItemSize.Small, "transform_rotate_180"));
    tab.Groups.Add(rotate);

    var mirror = new RibbonGroup("Mirror");
    mirror.Items.Add(this._Button("Horizontally", () => this._Apply(i => ImageTransformer.Flip(i, FlipDirection.Horizontal)), RibbonItemSize.Small, "shape_flip_horizontal"));
    mirror.Items.Add(this._Button("Vertically", () => this._Apply(i => ImageTransformer.Flip(i, FlipDirection.Vertical)), RibbonItemSize.Small, "shape_flip_vertical"));
    tab.Groups.Add(mirror);

    var frame = new RibbonGroup("Frame");
    frame.Items.Add(this._Button("Crop…", this._Crop, RibbonItemSize.Large, "transform-crop"));
    frame.Items.Add(this._Button("Margin…", this._Margin, RibbonItemSize.Large, "page-4sides"));
    frame.Items.Add(this._Button("Letterbox…", this._Letterbox, RibbonItemSize.Large, "picture_position"));
    tab.Groups.Add(frame);

    var filters = new RibbonGroup("Display filter");
    this._AddFilterButton(filters, "As the format says", null, "video_mode");
    this._AddFilterButton(filters, "Off", DisplayFilter.None, "television_off");
    this._AddFilterButton(filters, "NTSC composite", DisplayFilter.NtscComposite);
    this._AddFilterButton(filters, "NTSC S-Video", DisplayFilter.NtscSvideo);
    this._AddFilterButton(filters, "PAL", DisplayFilter.Pal);
    this._filterButtons[0].Button.Checked = true;
    tab.Groups.Add(filters);

    return tab;
  }

  /// <remarks>
  /// Reducing colours is the one command here with a whole library behind it, so it gets the tab's
  /// large button and a chooser of its own. Everything to the right of Channels is waiting on the
  /// pixel operations and says so.
  /// </remarks>
  private RibbonTab _BuildColourTab() {
    var tab = new RibbonTab("Colour");

    var palette = new RibbonGroup("Palette");
    palette.Items.Add(this._Button("Reduce colours…", this._ReduceColours, RibbonItemSize.Large, "color-management"));
    tab.Groups.Add(palette);

    var channels = new RibbonGroup("Channels");
    channels.Items.Add(this._Button("Greyscale", () => this._Apply(ImageTransforms.Grayscale), RibbonItemSize.Small, "convert_color_to_gray"));
    channels.Items.Add(this._Button("Invert", () => this._Apply(ImageTransforms.Invert), RibbonItemSize.Small, "film_photo"));
    tab.Groups.Add(channels);

    tab.Groups.Add(this._PlannedGroup("Tone", PlannedCommands.Tone));
    tab.Groups.Add(this._PlannedGroup("Colour", PlannedCommands.Colour));

    return tab;
  }

  private RibbonTab _BuildResizeTab() {
    var tab = new RibbonTab("Resize");

    var scale = new RibbonGroup("Scale");
    scale.Items.Add(this._Button("Resize…", this._Resize, RibbonItemSize.Large, "transform-scale"));
    scale.Items.Add(this._Button("Half", () => this._ScaleBy(0.5), RibbonItemSize.Small, "arrow_in"));
    scale.Items.Add(this._Button("Double", () => this._ScaleBy(2), RibbonItemSize.Small, "arrow_out"));
    tab.Groups.Add(scale);

    tab.Groups.Add(this._PlannedGroup("Resample", PlannedCommands.Resample));
    tab.Groups.Add(this._PlannedGroup("Regenerate", PlannedCommands.Regenerate));

    return tab;
  }

  private RibbonTab _BuildCorrectionsTab() {
    var tab = new RibbonTab("Corrections");

    var repair = new RibbonGroup("Repair");
    repair.Items.Add(this._Planned(PlannedCommands.Repair[0], RibbonItemSize.Large));
    for (var i = 1; i < PlannedCommands.Repair.Length; ++i)
      repair.Items.Add(this._Planned(PlannedCommands.Repair[i]));
    tab.Groups.Add(repair);

    tab.Groups.Add(this._PlannedGroup("Sharpness", PlannedCommands.Sharpness));
    tab.Groups.Add(this._PlannedGroup("Automatic", PlannedCommands.Automatic));

    return tab;
  }

  private RibbonTab _BuildEffectsTab() {
    var tab = new RibbonTab("Effects");

    tab.Groups.Add(this._PlannedGroup("Blur", PlannedCommands.Blur));
    tab.Groups.Add(this._PlannedGroup("Stylise", PlannedCommands.Stylise));
    tab.Groups.Add(this._PlannedGroup("Tint", PlannedCommands.Tint));

    return tab;
  }

  private RibbonGroup _PlannedGroup(string text, PlannedCommand[] commands) {
    var group = new RibbonGroup(text);
    foreach (var command in commands)
      group.Items.Add(this._Planned(command));

    return group;
  }

  /// <summary>Builds the button a command that does not exist yet gets: off, iconed, and explained.</summary>
  private RibbonButton _Planned(PlannedCommand command, RibbonItemSize size = RibbonItemSize.Small) {
    var button = new RibbonButton(command.Text, size) {
      Enabled = false,
      ToolTipText = command.ToolTipText,
    };

    if (command.Icon != null)
      this._icons.Apply(button, command.Icon, size);

    return button;
  }

  private void _AddFilterButton(RibbonGroup group, string text, DisplayFilter? filter, string? icon = null) {
    var button = new RibbonToggleButton(text, RibbonItemSize.Small);
    if (icon != null)
      this._icons.Apply(button, icon);

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
    this._icons.Apply(this._statusFormat, "image");
    this._icons.Apply(this._statusDimensions, "shape_handles");
    this._icons.Apply(this._statusBytes, "disk");
    this._icons.Apply(this._statusPosition, "images");
    this._icons.Apply(this._statusZoom, "magnifier");
    this._statusBar.Items.Add(this._statusFormat);
    this._statusBar.Items.Add(this._statusDimensions);
    this._statusBar.Items.Add(this._statusBytes);
    this._statusBar.Items.Add(this._statusPosition);
    this._statusBar.Items.Add(this._statusZoom);
  }

  /// <summary>The keys a picture viewer is expected to answer to, wherever the focus sits.</summary>
  /// <remarks>
  /// <para>
  /// The bindings are the ones a user of the viewer this replaces reaches for, with two departures
  /// worth naming. PageUp and PageDown walk the folder rather than the pages of a document, because
  /// that is what they do in every picture viewer and almost no picture has a second page; paging
  /// within a file moved onto Control with them. And the toolkit's canvas already answers Control+0
  /// and Control+9 for actual size and fit, so both are bound here too — not for the sake of a
  /// second way to do it, but so that every route to a zoom goes through the one place that records
  /// what kind of zoom it was.
  /// </para>
  /// <para>
  /// Nothing is bound to a bare letter. A viewer that will grow a "go to file" box wants the letters
  /// left alone, and a bare letter swallowed window-wide is the kind of binding that later turns out
  /// to have been eating somebody's typing.
  /// </para>
  /// </remarks>
  private void _BuildShortcuts() {
    this._shortcuts.Bind("Next picture", () => this.Walk(1), Keys.Right, Keys.Space, Keys.PageDown);
    this._shortcuts.Bind("Previous picture", () => this.Walk(-1), Keys.Left, Keys.Back, Keys.PageUp);
    this._shortcuts.Bind("First picture", () => this._OpenIndex(0), Keys.Home);
    this._shortcuts.Bind("Last picture", () => this._OpenIndex(this._folder.Files.Count - 1), Keys.End);

    this._shortcuts.Bind("Next page", () => this._GoPage(1), Keys.Control | Keys.PageDown);
    this._shortcuts.Bind("Previous page", () => this._GoPage(-1), Keys.Control | Keys.PageUp);

    // Every shape a plus and a minus arrive in. The keypad has codes of its own; the number row's
    // plus needs Shift on most layouts, and which key code that leaves behind depends on the
    // platform — Windows reports the physical key and X11 reports the character it produced — so
    // both, with and without Shift, are bound rather than guessed at.
    this._shortcuts.Bind(
      "Zoom in",
      () => this._ZoomBy(Math.Sqrt(2)),
      Keys.Oemplus, Keys.Shift | Keys.Oemplus, Keys.Add, Keys.Shift | Keys.Add);
    this._shortcuts.Bind(
      "Zoom out",
      () => this._ZoomBy(1 / Math.Sqrt(2)),
      Keys.OemMinus, Keys.Shift | Keys.OemMinus, Keys.Subtract, Keys.Shift | Keys.Subtract);
    this._shortcuts.Bind("Fit to window", this._FitToWindow, Keys.Control | Keys.F, Keys.Control | Keys.D9);
    this._shortcuts.Bind("Actual size", this._ActualSize, Keys.Control | Keys.D1, Keys.Control | Keys.D0);

    this._shortcuts.Bind("Thumbnails", () => this._showBrowser.Checked = !this._showBrowser.Checked, Keys.Control | Keys.T);
    this._shortcuts.Bind("Information", () => this._showInformation.Checked = !this._showInformation.Checked, Keys.Control | Keys.I);
    this._shortcuts.Bind("Wrap around", () => this._wrapToggle.Checked = !this._wrapToggle.Checked, Keys.Control | Keys.W);

    this._shortcuts.Bind("Re-read the folder", this._RefreshFolder, Keys.F5);
    this._shortcuts.Bind("Full screen", this._ToggleFullScreen, Keys.F11);
    this._shortcuts.Bind("Leave full screen", () => this._SetFullScreen(false), Keys.Escape);

    this._shortcuts.Bind("Open picture", this._OpenPicturePicker, Keys.Control | Keys.O);
    this._shortcuts.Bind("Open folder", this._OpenFolderPicker, Keys.Control | Keys.Shift | Keys.O);
    this._shortcuts.Bind("Save", this._Save, Keys.Control | Keys.S);
    this._shortcuts.Bind("Save as", this._SaveAs, Keys.Control | Keys.Shift | Keys.S);
    this._shortcuts.Bind("Print", this._Print, Keys.Control | Keys.P);
  }

  /// <summary>A ribbon command that hands the keyboard back to the picture when it is done.</summary>
  /// <remarks>
  /// A toolbar that keeps the focus after a click is a toolbar that has quietly taken the arrow keys
  /// away — the ribbon walks its own tabs with them — so every command returns the focus to the
  /// canvas, the same way pressing a button on a toolbar does everywhere else.
  /// </remarks>
  private RibbonButton _Button(string text, Action action, RibbonItemSize size = RibbonItemSize.Small, string? icon = null) {
    var button = new RibbonButton(text, size);
    button.Click += (_, _) => {
      action();
      this._viewport.Focus();
    };

    if (icon != null)
      this._icons.Apply(button, icon, size);

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

  /// <summary>The ribbon's tabs, by the name each shows.</summary>
  internal IEnumerable<string> TabNames => this._ribbon.Tabs.Select(t => t.Text);

  /// <summary>Brings the named ribbon tab to the front; false when no tab has that name.</summary>
  internal bool SelectTab(string name) {
    for (var i = 0; i < this._ribbon.Tabs.Count; ++i)
      if (string.Equals(this._ribbon.Tabs[i].Text, name, StringComparison.OrdinalIgnoreCase)) {
        this._ribbon.SelectedIndex = i;
        return true;
      }

    return false;
  }

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
    this._thumbnailLoader.Prioritise(index, _THUMBNAIL_LOOKAHEAD);
    this._Load(this._folder.Files[index]);
  }

  /// <summary>Lists the folder again, in case it changed under the window.</summary>
  private void _RefreshFolder() {
    if (this._folder.Folder == null)
      return;

    this._folder.Refresh();
    this._RefreshBrowser();
    if (this._folder.Index < 0 && this._folder.Files.Count > 0)
      this._OpenIndex(0);
  }

  private void _ToggleFullScreen() => this._SetFullScreen(!this._fullScreen);

  /// <summary>Gives the picture the whole screen, or gives the window its chrome back.</summary>
  /// <remarks>
  /// The chrome is hidden rather than the window merely maximised: a full screen that still shows a
  /// ribbon and a thumbnail strip is not one. The pane toggles are restored from the buttons that own
  /// them rather than from what the panes happened to be doing, so leaving full screen cannot turn a
  /// pane back on that the user had switched off.
  /// </remarks>
  private void _SetFullScreen(bool on) {
    if (on == this._fullScreen)
      return;

    if (on) {
      this._framedBorderStyle = this.FormBorderStyle;
      this._framedWindowState = this.WindowState;
      this._ribbon.Visible = false;
      this._statusBar.Visible = false;
      this._outer.Panel1Collapsed = true;
      this._inner.Panel2Collapsed = true;
      this.FormBorderStyle = FormBorderStyle.None;
      this.WindowState = FormWindowState.Maximized;
    } else {
      this.FormBorderStyle = this._framedBorderStyle;
      this.WindowState = this._framedWindowState;
      this._ribbon.Visible = true;
      this._statusBar.Visible = true;
      this._outer.Panel1Collapsed = !this._showBrowser.Checked;
      this._inner.Panel2Collapsed = !this._showInformation.Checked;
    }

    this._fullScreen = on;
    this._viewport.Focus();
    this._ApplyZoomMode();
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
      this._edited = false;
      this._pageCount = PageService.PageCount(file, entry);
      this._PickVideoMode();
      this._Render();
      this._ApplyZoomMode();
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
      this._edited = false;
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
    if (this._folder.Index > 0)
      this._thumbnailLoader.Prioritise(this._folder.Index, _THUMBNAIL_LOOKAHEAD);
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
    this._thumbnailLoader.Prioritise(index, _THUMBNAIL_LOOKAHEAD);
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

  /// <remarks>
  /// Guarded, because handing the canvas an image makes it fit that image to the window, and a fit
  /// this window asked for is not the user choosing a scale — which is what an unguarded
  /// <see cref="ZoomPanel.ZoomChanged"/> is taken to mean.
  /// </remarks>
  private void _Render() {
    ++this._zoomGuard;
    try {
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
    } finally {
      --this._zoomGuard;
    }

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
  }

  private void _FitToWindow() => this._SetZoomMode(ZoomMode.Fit);

  private void _ActualSize() => this._SetZoomMode(ZoomMode.Actual);

  /// <summary>Scales the picture the way the mode says, and remembers that this is how it was asked for.</summary>
  private void _SetZoomMode(ZoomMode mode) {
    this._zoomMode = mode;
    this._ApplyZoomMode();
    this._UpdateStatus();
  }

  /// <summary>Scales the picture that has just arrived the way the last one was being looked at.</summary>
  /// <remarks>
  /// Assigning the canvas an image fits it to the window, which is the right answer for the fit mode
  /// and wrong for the other two: somebody comparing a detail across a folder at 400% wants the next
  /// picture at 400%, and somebody at actual size wants actual size. So the mode is re-applied after
  /// every load, and the free scale is remembered rather than read back off the canvas, which by then
  /// holds the fit it computed for itself.
  /// </remarks>
  private void _ApplyZoomMode() {
    ++this._zoomGuard;
    try {
      switch (this._zoomMode) {
        case ZoomMode.Actual:
          this._viewport.ActualSize();
          break;
        case ZoomMode.Free:
          this._viewport.Zoom = this._freeZoom;
          break;
        default:
          this._viewport.FitToWindow();
          break;
      }
    } finally {
      --this._zoomGuard;
    }
  }

  /// <summary>Notes a zoom nobody here asked for as the scale the user wants to keep.</summary>
  /// <remarks>
  /// There are four ways to change the scale that do not go through this class — Control with the
  /// wheel, the slider the canvas draws in its corner, dragging that slider's ends, and the canvas's
  /// own keys — and every one of them means the same thing: the user has chosen a scale, and it is to
  /// survive the next picture. Rather than intercept each, anything that is not this window loading a
  /// picture or applying a mode is taken as that choice. The guard is what tells the two apart.
  /// </remarks>
  private void _OnZoomChanged() {
    if (this._zoomGuard == 0) {
      this._zoomMode = ZoomMode.Free;
      this._freeZoom = this._viewport.Zoom;
    }

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
      this._edited = true;
      this._Render();
      this._UpdateInformation();
      this._UpdateStatus();
    } catch (Exception ex) {
      this._Complain("Picture", ex.Message);
    }
  }

  private void _Crop() {
    if (this._raw == null)
      return;

    var dialog = new CropOptionsDialog(this._raw.Width, this._raw.Height);
    if (dialog.ShowDialog(this) != DialogResult.OK)
      return;

    var region = dialog.Region;
    this._Apply(i => ImageTransformer.Crop(i, region));
  }

  private void _Resize() {
    if (this._raw == null)
      return;

    var dialog = new ResizeOptionsDialog(this._raw.Width, this._raw.Height, ImageTransformer.GuessInterpolation(this._raw));
    if (dialog.ShowDialog(this) != DialogResult.OK)
      return;

    this._Apply(i => ImageTransformer.Resize(i, dialog.TargetWidth, dialog.TargetHeight, dialog.Mode, dialog.Interpolation));
  }

  private void _Margin() {
    if (this._raw == null)
      return;

    var dialog = new MarginOptionsDialog(this._raw.Width, this._raw.Height);
    if (dialog.ShowDialog(this) != DialogResult.OK)
      return;

    var margin = dialog.MarginWidth;
    var fill = dialog.Fill;
    this._Apply(i => ImageTransformer.ExtendCanvas(
      i, i.Width + margin * 2, i.Height + margin * 2, AnchorPosition.Center, fill));
  }

  private void _Letterbox() {
    if (this._raw == null)
      return;

    var dialog = new LetterboxOptionsDialog(
      this._raw.Width, this._raw.Height, ImageTransformer.GuessInterpolation(this._raw));
    if (dialog.ShowDialog(this) != DialogResult.OK)
      return;

    this._Apply(i => ImageTransformer.Resize(
      i, dialog.FrameWidth, dialog.FrameHeight, ResizeMode.Fit, dialog.Interpolation, dialog.Bars));
  }

  /// <summary>Scales the picture by a fixed factor, with the filter the picture itself suggests.</summary>
  /// <remarks>
  /// Halving and doubling are the two scales that need no dialog, and both are clamped to the range
  /// the resize dialog offers: a picture already at one pixel cannot be halved, and one at the far end
  /// cannot be doubled, so the factor is applied to whatever room is left rather than refused.
  /// </remarks>
  private void _ScaleBy(double factor) {
    if (this._raw == null)
      return;

    const int largest = 16384;
    var width = Math.Clamp((int)Math.Round(this._raw.Width * factor), 1, largest);
    var height = Math.Clamp((int)Math.Round(this._raw.Height * factor), 1, largest);
    if (width == this._raw.Width && height == this._raw.Height)
      return;

    var hint = ImageTransformer.GuessInterpolation(this._raw);
    this._Apply(i => ImageTransformer.Resize(i, width, height, ResizeMode.Stretch, hint));
  }

  private void _ReduceColours() {
    if (this._raw == null)
      return;

    var current = this._raw.IsIndexed ? Math.Clamp(this._raw.PaletteCount, 2, 256) : 256;
    var describe = this._raw.IsIndexed
      ? $"The picture holds {this._raw.PaletteCount} colour(s) in a palette."
      : "The picture is not palettised; reducing it builds a palette and maps every pixel onto it.";
    var dialog = new ReduceColoursDialog(describe, current);
    if (dialog.ShowDialog(this) != DialogResult.OK)
      return;

    var quantizer = dialog.Quantizer;
    var ditherer = dialog.Ditherer;
    var colours = dialog.Colours;
    this._Apply(i => ColorReductionDispatch.Reduce(i, quantizer, ditherer, colours));
  }

  // ============================================================================================
  // Writing
  // ============================================================================================

  /// <summary>Writes the edited picture back over the file it came from.</summary>
  /// <remarks>
  /// <para>
  /// Falls through to Save as whenever writing in place would not be writing the same picture back:
  /// nothing has been edited, the picture did not come from a file, or no registered writer can
  /// produce the format it was read from. That is the rule the viewer this replaces used, and it is
  /// what keeps Save from quietly being the only way to lose a read-only format's file.
  /// </para>
  /// <para>
  /// A file holding several pages falls through too, and that part is new. Every writer encodes one
  /// picture, so writing back what is on screen would replace a twelve-page document with its
  /// seventh page and report it as saved — a one-keystroke way to destroy eleven pages that no
  /// dialog warned about.
  /// </para>
  /// </remarks>
  private void _Save() {
    if (this._raw == null)
      return;

    if (!this._edited || this._file == null || this._pageCount > 1 || this._entry?.SupportsWrite != true) {
      this._SaveAs();
      return;
    }

    var result = ConversionService.Write(this._raw, this._entry, this._file, this._file);
    if (!result.Succeeded) {
      this._Complain("Save picture", result.Message);
      return;
    }

    this._edited = false;
    this._file.Refresh();
    this._UpdateInformation();
    this._UpdateStatus();
  }

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

    // What was just written is now the picture on screen, so Save from here on writes back to it
    // rather than to the file it was read from. Leaving the old file as the target would make a
    // converted picture's next Save overwrite the original in its original format.
    target.Refresh();
    this._file = target;
    this._entry = entry;
    this._format = entry.Format;
    this._edited = false;

    if (this._folder.Folder != null && target.Directory != null
        && string.Equals(this._folder.Folder.FullName, target.Directory.FullName, StringComparison.OrdinalIgnoreCase)) {
      this._folder.Refresh();
      this._folder.Select(target);
      this._RefreshBrowser();
    }

    this._UpdateInformation();
    this._UpdateStatus();
  }

  private void _About()
    => MessageBox.Show(
      this,
      "Crush Viewer\n\nBrowser, viewer, converter and page extractor for the PNGCrushCS format suite, "
      + "built on NativeForms over the platform's own widgets.\n\n"
      + "Icons: Silk icon set 1.3 by Mark James, http://www.famfamfam.com/lab/icons/silk/, "
      + "licensed under Creative Commons Attribution 2.5.\n"
      + "Silk Companion #1 by Damien Guard, http://damieng.com/creative/icons/silk-companion-1-icons, "
      + "licensed under Creative Commons Attribution 2.5.\n"
      + "Fugue Icons (C) 2013 Yusuke Kamiyamane, http://p.yusukekamiyamane.com/, "
      + "licensed under Creative Commons Attribution 3.0.\n"
      + "FatCow Farm-Fresh Web Icons, http://www.fatcow.com/free-icons, "
      + "licensed under Creative Commons Attribution 3.0.\n"
      + "Oxygen Icon Theme, (C) 2007 The Oxygen Team, https://github.com/KDE/oxygen-icons5, "
      + "under the GNU Lesser General Public License, version 3 or later.",
      "About Crush Viewer",
      MessageBoxButtons.OK,
      MessageBoxIcon.Information);

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

  /// <summary>Puts the picture on paper, by way of a PDF and the platform's own print path.</summary>
  /// <remarks>
  /// What is printed is what is on screen, edits and all, and not the file on disk — which is the
  /// answer somebody who has just straightened a scan expects. The picture is handed over as it
  /// stands rather than re-read.
  /// </remarks>
  private void _Print() {
    if (this._raw == null)
      return;

    if (PrintService.Unavailable is { } why) {
      this._Complain("Print picture", why);
      return;
    }

    var dialog = new PrintOptionsDialog(this._file?.Name ?? "The picture");
    if (dialog.ShowDialog(this) != DialogResult.OK)
      return;

    var destination = dialog.Destination;
    var failure = PrintService.Send(this._raw, this._file?.Name ?? "picture", destination);
    if (failure != null) {
      this._Complain("Print picture", failure);
      return;
    }

    this._Note(destination.Route == PrintRoute.Printer
      ? "Sent to the printer."
      : "Handed to the application that opens a PDF here.");
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

    this._save.Enabled = this._raw != null;
    this._print.Enabled = this._raw != null && PrintService.Unavailable == null;
    this.Text = this._file == null ? _TITLE : $"{(this._edited ? "* " : "")}{this._file.Name} — {_TITLE}";
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
    this._icons.Dispose();
  }

  private void _ReleaseDisplayed() {
    ++this._zoomGuard;
    try {
      this._viewport.Image = null;
    } finally {
      --this._zoomGuard;
    }

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
