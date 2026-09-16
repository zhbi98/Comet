using Comet.Core.Terminal;
using Comet.Models;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace Comet.Controls;

/// <summary>
/// Presents a continuous terminal document while realizing only the rows near the viewport.
/// Selection, caret, and scroll positions are stored as document offsets rather than UI elements.
/// </summary>
public sealed partial class VirtualTerminalControl : UserControl
{
    private const double HORIZONTAL_PADDING = 16;
    private const int SCROLL_LINES_PER_STEP = 3;

    private readonly VirtualTerminalDocument _document = new();
    private readonly List<TerminalLinePresenter> _presenters = [];
    private readonly RectangleGeometry _viewportClip = new();
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _caretTimer;
    private bool _isPointerSelecting;
    // Both ends use UTF-16 document offsets so recycled row elements never own selection state.
    private int _selectionAnchor;
    private int _selectionActive;
    private double _characterWidth = 8;
    private double _lineHeight = 19;
    private double _caretHeight = 18;
    private bool _isInputEnabled;
    private bool _autoScroll = true;
    private bool _isClearingInputProxy;
    private bool _scrollToEndAfterLayout;
    private ScrollAnchor? _scrollAnchorAfterLayout;
    private double _verticalOffset;
    private double _maximumVerticalOffset;
    private bool _isUpdatingScrollBar;
    private bool _isViewportUpdateQueued;
    private bool _hasInputFocus;
    private bool _isCaretVisible;

    public VirtualTerminalControl()
    {
        InitializeComponent();
        _document.Clear();
        LineViewport.Clip = _viewportClip;
        var textCursor = InputCursor.CreateFromCoreCursor(new CoreCursor(CoreCursorType.IBeam, 0));
        ProtectedCursor = textCursor;

        // Register on the clipped text viewport so the native scroll bar keeps its own
        // pointer handling while terminal selection receives already-handled events.
        LineViewport.AddHandler(PointerPressedEvent, new PointerEventHandler(OnPointerPressed), handledEventsToo: true);
        LineViewport.AddHandler(PointerMovedEvent, new PointerEventHandler(OnPointerMoved), handledEventsToo: true);
        LineViewport.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnPointerReleased), handledEventsToo: true);
        LineViewport.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnPointerCaptureLost), handledEventsToo: true);
        LineViewport.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(OnPointerWheelChanged), handledEventsToo: true);
        InputProxy.AddHandler(KeyDownEvent, new KeyEventHandler(OnInputProxyKeyDown), handledEventsToo: true);
        InputProxy.Paste += InputProxy_Paste;
        InputProxy.GotFocus += InputProxy_GotFocus;
        InputProxy.LostFocus += InputProxy_LostFocus;
        GotFocus += VirtualTerminalControl_GotFocus;

        // The real TextBox caret is intentionally invisible. Blink only the lightweight
        // rectangle drawn by the currently realized line presenter.
        _caretTimer = DispatcherQueue.CreateTimer();
        _caretTimer.Interval = TimeSpan.FromMilliseconds(530);
        _caretTimer.IsRepeating = true;
        _caretTimer.Tick += (_, _) =>
        {
            if (!_hasInputFocus)
            {
                return;
            }

            _isCaretVisible = !_isCaretVisible;
            UpdateVisiblePresenters();
        };
        Unloaded += (_, _) => _caretTimer.Stop();

        var copyItem = new MenuFlyoutItem { Text = "复制" };
        copyItem.Click += (_, _) => CopySelection();
        var selectAllItem = new MenuFlyoutItem { Text = "全选" };
        selectAllItem.Click += (_, _) => SelectAll();
        ContextFlyout = new MenuFlyout
        {
            Items = { copyItem, selectAllItem }
        };
    }

    public event EventHandler<TerminalInputEventArgs>? InputReceived;

    public event EventHandler? ViewportChanged;

    public bool AutoScroll
    {
        get => _autoScroll;
        set
        {
            _autoScroll = value;
            if (!value)
            {
                _scrollToEndAfterLayout = false;
            }
        }
    }

    public bool IsInputEnabled
    {
        get => _isInputEnabled;
        set
        {
            _isInputEnabled = value;
            InputProxy.IsReadOnly = !value;
        }
    }

    public int CharacterCount => _document.CharacterCount;

    public int LineCount => _document.LineCount;

    public bool HasSelection => _selectionAnchor != _selectionActive;

    public string SelectedText
    {
        get
        {
            var start = Math.Min(_selectionAnchor, _selectionActive);
            var end = Math.Max(_selectionAnchor, _selectionActive);
            return _document.GetText(start, end - start);
        }
    }

    public void Clear()
    {
        _document.Clear();
        _selectionAnchor = 0;
        _selectionActive = 0;
        CancelPendingViewportChange();
        UpdateScrollRange();
        SetVerticalOffset(0);
    }

    public void SetText(string text, bool shouldScrollToEnd)
    {
        // Text and HEX documents have different lengths, so a mode switch preserves a
        // relative scroll position when following the tail is not requested.
        var scrollRatio = _maximumVerticalOffset <= 0
            ? 0
            : _verticalOffset / _maximumVerticalOffset;
        _document.SetText(text);
        _selectionAnchor = Math.Min(_selectionAnchor, _document.CharacterCount);
        _selectionActive = Math.Min(_selectionActive, _document.CharacterCount);

        if (shouldScrollToEnd && !HasSelection)
        {
            _selectionAnchor = _document.CharacterCount;
            _selectionActive = _document.CharacterCount;
            RequestScrollToEndAfterLayout();
        }
        else
        {
            RequestScrollRatioAfterLayout(scrollRatio);
        }
    }

    public void AppendText(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        // An active selection implies that the user is inspecting history and must not
        // be pulled away by incoming data, even when automatic scrolling is enabled.
        var shouldFollow = AutoScroll && !HasSelection;
        var anchor = CaptureScrollAnchor();
        _document.Append(text);

        if (shouldFollow)
        {
            _selectionAnchor = _document.CharacterCount;
            _selectionActive = _document.CharacterCount;
            RequestScrollToEndAfterLayout();
        }
        else
        {
            RequestScrollAnchorAfterLayout(anchor);
        }
    }

    public void ScrollToEnd()
    {
        CancelPendingViewportChange();
        UpdateScrollRange();
        SetVerticalOffset(_maximumVerticalOffset);
    }

    public (int FirstLine, int LastLine) GetVisibleLineRange()
    {
        if (_document.LineCount == 0)
        {
            return (0, 0);
        }

        var first = Math.Clamp((int)Math.Floor(_verticalOffset / _lineHeight), 0, _document.LineCount - 1);
        var viewportHeight = Math.Max(0, LineViewport.ActualHeight);
        var last = viewportHeight <= 0
            ? first
            : Math.Clamp(
                (int)Math.Ceiling((_verticalOffset + viewportHeight) / _lineHeight) - 1,
                first,
                _document.LineCount - 1);
        return (first, last);
    }

    public void SelectAll()
    {
        _selectionAnchor = 0;
        _selectionActive = _document.CharacterCount;
        UpdateVisiblePresenters();
        RaiseViewportChanged();
    }

    public void CopySelection()
    {
        if (!HasSelection)
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(SelectedText);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    /// <summary>
    /// Applies terminal typography as one visual transaction and recalculates soft
    /// wrapping without changing the underlying session text or selection offsets.
    /// </summary>
    public void ApplyTypography(FontFamily fontFamily, double fontSize)
    {
        if (FontFamily.Source == fontFamily.Source && Math.Abs(FontSize - fontSize) < double.Epsilon)
        {
            return;
        }

        FontFamily = fontFamily;
        FontSize = fontSize;
        if (IsLoaded)
        {
            MeasureTextMetrics();
            ReflowForCurrentWidth();
        }
    }

    private void VirtualTerminalControl_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateViewportClip();
        MeasureTextMetrics();
        ReflowForCurrentWidth();
    }

    private void VirtualTerminalControl_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (IsLoaded)
        {
            UpdateViewportClip();
            MeasureTextMetrics();
            ReflowForCurrentWidth();
        }
    }

    private void MeasureTextMetrics()
    {
        // Averaging a long monospace sample avoids rounding error from measuring one glyph.
        var probe = new TextBlock
        {
            Text = new string('M', 100),
            FontFamily = FontFamily,
            FontSize = FontSize,
            TextWrapping = TextWrapping.NoWrap
        };
        probe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        _characterWidth = Math.Max(1, probe.DesiredSize.Width / 100.0);

        // DesiredSize contains the font's complete line box, including ascenders,
        // descenders, and font-specific leading. The row adds one DIP of breathing room;
        // the presenter centers the caret's measured line box inside that row.
        _caretHeight = Math.Max(1, probe.DesiredSize.Height);
        _lineHeight = Math.Max(_caretHeight, Math.Ceiling(_caretHeight + 1));
    }

    private void ReflowForCurrentWidth()
    {
        var availableWidth = LineViewport.ActualWidth > 0 ? LineViewport.ActualWidth : ActualWidth;
        var columns = Math.Max(1, (int)Math.Floor((availableWidth - (HORIZONTAL_PADDING * 2)) / _characterWidth));
        // Width changes invalidate row numbers. A document offset remains stable across reflow.
        var anchor = CaptureScrollAnchor();
        if (!_document.SetColumns(columns))
        {
            UpdateScrollRange();
            UpdateVisiblePresenters();
            RaiseViewportChanged();
            return;
        }

        RequestScrollAnchorAfterLayout(anchor);
    }

    private void UpdateViewportClip() => _viewportClip.Rect = new Windows.Foundation.Rect(
        0,
        0,
        Math.Max(0, LineViewport.ActualWidth),
        Math.Max(0, LineViewport.ActualHeight));

    private void RequestScrollToEndAfterLayout()
    {
        _scrollToEndAfterLayout = true;
        _scrollAnchorAfterLayout = null;
        QueuePostLayoutViewportUpdate();
    }

    private void RequestScrollAnchorAfterLayout(ScrollAnchor anchor)
    {
        _scrollToEndAfterLayout = false;
        _scrollAnchorAfterLayout = anchor;
        QueuePostLayoutViewportUpdate();
    }

    private void RequestScrollRatioAfterLayout(double ratio) =>
        // A negative document offset distinguishes a relative position from a text anchor.
        RequestScrollAnchorAfterLayout(new ScrollAnchor(-1, Math.Clamp(ratio, 0, 1)));

    private void QueuePostLayoutViewportUpdate()
    {
        if (_isViewportUpdateQueued)
        {
            return;
        }

        _isViewportUpdateQueued = true;
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _isViewportUpdateQueued = false;
            UpdateScrollRange();
            ApplyPendingViewportChange();
        }))
        {
            _isViewportUpdateQueued = false;
        }
    }

    private void ApplyPendingViewportChange()
    {
        if (_scrollToEndAfterLayout)
        {
            _scrollToEndAfterLayout = false;
            SetVerticalOffset(_maximumVerticalOffset);
            return;
        }

        if (_scrollAnchorAfterLayout is ScrollAnchor anchor)
        {
            _scrollAnchorAfterLayout = null;
            if (anchor.DocumentOffset < 0)
            {
                // Negative offsets encode a proportional position used when switching
                // between text and HEX documents that do not share character offsets.
                SetVerticalOffset(_maximumVerticalOffset * anchor.WithinLineOffset);
            }
            else
            {
                RestoreScrollAnchor(anchor);
            }

            return;
        }

        UpdateVisiblePresenters();
        RaiseViewportChanged();
    }

    private ScrollAnchor CaptureScrollAnchor()
    {
        if (_document.LineCount == 0)
        {
            return new ScrollAnchor(0, 0);
        }

        // Store the first visible character plus its fractional row offset. This survives
        // appends and complete row-index rebuilds without depending on a stale row number.
        var lineIndex = Math.Clamp((int)Math.Floor(_verticalOffset / _lineHeight), 0, _document.LineCount - 1);
        var line = _document.GetLine(lineIndex);
        var withinLine = _verticalOffset - (lineIndex * _lineHeight);
        return new ScrollAnchor(line.Start, Math.Max(0, withinLine));
    }

    private void RestoreScrollAnchor(ScrollAnchor anchor)
    {
        var lineIndex = _document.FindLineIndex(anchor.DocumentOffset);
        SetVerticalOffset((lineIndex * _lineHeight) + anchor.WithinLineOffset);
    }

    private void UpdateScrollRange()
    {
        var viewportHeight = Math.Max(0, LineViewport.ActualHeight);
        var extentHeight = _document.LineCount * _lineHeight;
        _maximumVerticalOffset = Math.Max(0, extentHeight - viewportHeight);
        _verticalOffset = Math.Clamp(_verticalOffset, 0, _maximumVerticalOffset);

        _isUpdatingScrollBar = true;
        try
        {
            VerticalScrollBar.Minimum = 0;
            VerticalScrollBar.Maximum = _maximumVerticalOffset;
            VerticalScrollBar.ViewportSize = viewportHeight;
            VerticalScrollBar.SmallChange = _lineHeight * SCROLL_LINES_PER_STEP;
            VerticalScrollBar.LargeChange = Math.Max(_lineHeight, viewportHeight);
            VerticalScrollBar.Value = _verticalOffset;
            VerticalScrollBar.IsEnabled = _maximumVerticalOffset > 0;
        }
        finally
        {
            _isUpdatingScrollBar = false;
        }
    }

    private void SetVerticalOffset(double offset)
    {
        _verticalOffset = Math.Clamp(double.IsFinite(offset) ? offset : 0, 0, _maximumVerticalOffset);
        _isUpdatingScrollBar = true;
        try
        {
            VerticalScrollBar.Value = _verticalOffset;
        }
        finally
        {
            _isUpdatingScrollBar = false;
        }

        UpdateVisiblePresenters();
        RaiseViewportChanged();
    }

    private void SetVerticalOffsetFromUser(double offset)
    {
        CancelPendingViewportChange();
        SetVerticalOffset(offset);
    }

    private void CancelPendingViewportChange()
    {
        _scrollToEndAfterLayout = false;
        _scrollAnchorAfterLayout = null;
    }

    private void UpdateVisiblePresenters()
    {
        var viewportHeight = Math.Max(0, LineViewport.ActualHeight);
        var requiredCount = Math.Max(1, (int)Math.Ceiling(viewportHeight / _lineHeight) + 2);
        while (_presenters.Count < requiredCount)
        {
            var presenter = new TerminalLinePresenter();
            _presenters.Add(presenter);
            LineSurface.Children.Add(presenter);
        }

        var firstLine = Math.Clamp((int)Math.Floor(_verticalOffset / _lineHeight), 0, _document.LineCount - 1);
        var withinLineOffset = _verticalOffset - (firstLine * _lineHeight);
        for (var slot = 0; slot < _presenters.Count; slot++)
        {
            var presenter = _presenters[slot];
            var lineIndex = firstLine + slot;
            if (slot >= requiredCount || lineIndex >= _document.LineCount)
            {
                presenter.Visibility = Visibility.Collapsed;
                continue;
            }

            presenter.Width = Math.Max(0, LineViewport.ActualWidth);
            Canvas.SetLeft(presenter, 0);
            Canvas.SetTop(presenter, (slot * _lineHeight) - withinLineOffset);
            UpdatePresenter(presenter, lineIndex);
            presenter.Visibility = Visibility.Visible;
        }
    }

    private void UpdatePresenter(TerminalLinePresenter presenter, int lineIndex)
    {
        var line = _document.GetLine(lineIndex);
        // Intersect the document-wide selection with this realized row. Rows outside
        // the viewport do not need selection visuals and may not have UI elements.
        var selectionStart = Math.Min(_selectionAnchor, _selectionActive);
        var selectionEnd = Math.Max(_selectionAnchor, _selectionActive);
        var localStart = Math.Clamp(selectionStart, line.Start, line.End);
        var localEnd = Math.Clamp(selectionEnd, line.Start, line.End);
        var selectionStartCell = _document.GetCellOffset(line, localStart);
        var selectionEndCell = _document.GetCellOffset(line, localEnd);
        // A selected hard line break has no glyph, so the presenter draws one cell to
        // make a selection that crosses an empty line visually continuous.
        var includesLineBreak = line.BreakLength > 0 && selectionStart <= line.End && selectionEnd > line.End;
        var caretLineIndex = _document.FindLineIndex(_selectionActive);
        var showCaret = _hasInputFocus && _isCaretVisible && !HasSelection && lineIndex == caretLineIndex;
        var caretCell = showCaret ? _document.GetCellOffset(line, _selectionActive) : 0;

        presenter.Update(
            line.Text,
            _lineHeight,
            _caretHeight,
            HORIZONTAL_PADDING,
            _characterWidth,
            FontFamily,
            FontSize,
            Foreground,
            selectionStartCell,
            selectionEndCell,
            includesLineBreak,
            caretCell,
            showCaret);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (args.Pointer.PointerDeviceType == PointerDeviceType.Touch)
        {
            return;
        }

        var point = args.GetCurrentPoint(LineViewport);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        InputProxy.Focus(FocusState.Pointer);
        var offset = GetDocumentOffsetFromPoint(point.Position);
        CancelPendingViewportChange();
        if (!IsShiftDown())
        {
            _selectionAnchor = offset;
        }

        _selectionActive = offset;
        ResetCaretBlink();
        _isPointerSelecting = LineViewport.CapturePointer(args.Pointer);
        UpdateVisiblePresenters();
        args.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (!_isPointerSelecting)
        {
            return;
        }

        var viewportPoint = args.GetCurrentPoint(LineViewport).Position;
        if (viewportPoint.Y < 0)
        {
            SetVerticalOffsetFromUser(_verticalOffset - (_lineHeight * SCROLL_LINES_PER_STEP));
        }
        else if (viewportPoint.Y > LineViewport.ActualHeight)
        {
            SetVerticalOffsetFromUser(_verticalOffset + (_lineHeight * SCROLL_LINES_PER_STEP));
        }

        _selectionActive = GetDocumentOffsetFromPoint(viewportPoint);
        UpdateVisiblePresenters();
        RaiseViewportChanged();
        args.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs args)
    {
        if (!_isPointerSelecting)
        {
            return;
        }

        _selectionActive = GetDocumentOffsetFromPoint(args.GetCurrentPoint(LineViewport).Position);
        ResetCaretBlink();
        _isPointerSelecting = false;
        LineViewport.ReleasePointerCapture(args.Pointer);
        UpdateVisiblePresenters();
        RaiseViewportChanged();
        args.Handled = true;
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs args) => _isPointerSelecting = false;

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs args)
    {
        var delta = args.GetCurrentPoint(LineViewport).Properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }

        SetVerticalOffsetFromUser(
            _verticalOffset - ((delta / 120.0) * _lineHeight * SCROLL_LINES_PER_STEP));
        args.Handled = true;
    }

    private void LineViewport_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs args)
    {
        if (args.PointerDeviceType != PointerDeviceType.Touch)
        {
            return;
        }

        SetVerticalOffsetFromUser(_verticalOffset - args.Delta.Translation.Y);
        args.Handled = true;
    }

    private void VerticalScrollBar_ValueChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (!_isUpdatingScrollBar)
        {
            SetVerticalOffsetFromUser(args.NewValue);
        }
    }

    private void VirtualTerminalControl_GotFocus(object sender, RoutedEventArgs args)
    {
        if (!ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), InputProxy))
        {
            InputProxy.Focus(FocusState.Keyboard);
        }
    }

    private void InputProxy_GotFocus(object sender, RoutedEventArgs args)
    {
        // Keep focus on the invisible native text input surface while the visible
        // caret is rendered independently by a virtual row presenter.
        _hasInputFocus = true;
        ResetCaretBlink();
        if (!_caretTimer.IsRunning)
        {
            _caretTimer.Start();
        }
    }

    private void InputProxy_LostFocus(object sender, RoutedEventArgs args)
    {
        _hasInputFocus = false;
        _isCaretVisible = false;
        _caretTimer.Stop();
        UpdateVisiblePresenters();
    }

    private void ResetCaretBlink()
    {
        _isCaretVisible = true;
        UpdateVisiblePresenters();
    }

    private int GetDocumentOffsetFromPoint(Windows.Foundation.Point position)
    {
        var absoluteY = _verticalOffset + position.Y;
        var lineIndex = Math.Clamp((int)Math.Floor(absoluteY / _lineHeight), 0, _document.LineCount - 1);
        var cellPosition = Math.Max(0, (position.X - HORIZONTAL_PADDING) / _characterWidth);
        return _document.GetDocumentOffset(lineIndex, cellPosition);
    }

    private void OnInputProxyKeyDown(object sender, KeyRoutedEventArgs args)
    {
        var control = IsControlDown();
        if (control && args.Key == VirtualKey.A)
        {
            SelectAll();
            args.Handled = true;
            return;
        }

        if (control && (args.Key == VirtualKey.C || args.Key == VirtualKey.Insert))
        {
            CopySelection();
            args.Handled = true;
            return;
        }

        if (args.Key is VirtualKey.Delete or VirtualKey.Back)
        {
            args.Handled = true;
            return;
        }

        if (args.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down or
            VirtualKey.Home or VirtualKey.End or VirtualKey.PageUp or VirtualKey.PageDown)
        {
            MoveSelectionWithKeyboard(args.Key, IsShiftDown());
            args.Handled = true;
        }
    }

    private async void InputProxy_Paste(object sender, TextControlPasteEventArgs args)
    {
        // Prevent the TextBox from inserting the clipboard contents. Forwarding the
        // Paste event directly keeps pasted text separate from ordinary key input.
        args.Handled = true;
        if (!_isInputEnabled)
        {
            return;
        }

        try
        {
            var clipboardContent = Clipboard.GetContent();
            if (!clipboardContent.Contains(StandardDataFormats.Text))
            {
                return;
            }

            var pastedText = await clipboardContent.GetTextAsync();
            if (!_isInputEnabled || pastedText.Length == 0)
            {
                return;
            }

            ResetCaretBlink();
            InputReceived?.Invoke(this, new TerminalInputEventArgs(pastedText));
        }
        catch (Exception)
        {
            // Clipboard ownership can change while the asynchronous read is pending.
            // A transient clipboard failure must not terminate the terminal session.
        }
    }

    private void MoveSelectionWithKeyboard(VirtualKey key, bool extendSelection)
    {
        var active = _selectionActive;
        var lineIndex = _document.FindLineIndex(active);
        var line = _document.GetLine(lineIndex);
        var cell = _document.GetCellOffset(line, active);

        active = key switch
        {
            VirtualKey.Left => _document.MoveByCodePoint(active, -1),
            VirtualKey.Right => _document.MoveByCodePoint(active, 1),
            VirtualKey.Up => _document.GetDocumentOffset(Math.Max(0, lineIndex - 1), cell),
            VirtualKey.Down => _document.GetDocumentOffset(Math.Min(_document.LineCount - 1, lineIndex + 1), cell),
            VirtualKey.Home => line.Start,
            VirtualKey.End => line.End,
            VirtualKey.PageUp => _document.GetDocumentOffset(
                Math.Max(0, lineIndex - Math.Max(1, GetVisibleLineRange().LastLine - GetVisibleLineRange().FirstLine)),
                cell),
            VirtualKey.PageDown => _document.GetDocumentOffset(
                Math.Min(_document.LineCount - 1, lineIndex + Math.Max(1, GetVisibleLineRange().LastLine - GetVisibleLineRange().FirstLine)),
                cell),
            _ => active
        };

        if (!extendSelection)
        {
            _selectionAnchor = active;
        }

        _selectionActive = active;
        ResetCaretBlink();
        ScrollPositionIntoView(active);
        UpdateVisiblePresenters();
        RaiseViewportChanged();
    }

    private void ScrollPositionIntoView(int position)
    {
        var lineIndex = _document.FindLineIndex(position);
        var top = lineIndex * _lineHeight;
        var bottom = top + _lineHeight;
        if (top < _verticalOffset)
        {
            SetVerticalOffset(top);
        }
        else if (bottom > _verticalOffset + LineViewport.ActualHeight)
        {
            SetVerticalOffset(bottom - LineViewport.ActualHeight);
        }
    }

    private void InputProxy_TextChanged(object sender, TextChangedEventArgs args)
    {
        if (_isClearingInputProxy || sender is not TextBox inputProxy || inputProxy.Text.Length == 0)
        {
            return;
        }

        // TextChanged is the single path for committed keyboard text. Capture the
        // temporary value before clearing the invisible proxy under a reentrancy guard.
        var insertedText = inputProxy.Text;
        _isClearingInputProxy = true;
        inputProxy.Text = string.Empty;
        _isClearingInputProxy = false;
        if (_isInputEnabled)
        {
            ResetCaretBlink();
            InputReceived?.Invoke(this, new TerminalInputEventArgs(insertedText));
        }
    }

    private void RaiseViewportChanged() => ViewportChanged?.Invoke(this, EventArgs.Empty);

    private static bool IsControlDown() =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0;

    private static bool IsShiftDown() =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) != 0;

    private readonly record struct ScrollAnchor(int DocumentOffset, double WithinLineOffset);
}
