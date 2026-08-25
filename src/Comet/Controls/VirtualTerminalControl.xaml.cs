using Comet.Core.Terminal;
using Comet.Models;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace Comet.Controls;

public sealed partial class VirtualTerminalControl : UserControl
{
    private const double HORIZONTAL_PADDING = 16;
    private const int VIEWPORT_PREFETCH_LINES = 3;

    private readonly VirtualTerminalDocument _document = new();
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _caretTimer;
    private bool _isPointerSelecting;
    private int _selectionAnchor;
    private int _selectionActive;
    private double _characterWidth = 8;
    private double _lineHeight = 19;
    private bool _isInputEnabled;
    private bool _autoScroll = true;
    private bool _isClearingInputProxy;
    private bool _scrollToEndAfterLayout;
    private ScrollAnchor? _scrollAnchorAfterLayout;
    private bool _hasInputFocus;
    private bool _isCaretVisible;

    public VirtualTerminalControl()
    {
        InitializeComponent();
        _document.Clear();
        LineRepeater.ItemsSource = _document.Lines;

        AddHandler(PointerPressedEvent, new PointerEventHandler(OnPointerPressed), handledEventsToo: true);
        AddHandler(PointerMovedEvent, new PointerEventHandler(OnPointerMoved), handledEventsToo: true);
        AddHandler(PointerReleasedEvent, new PointerEventHandler(OnPointerReleased), handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnPointerCaptureLost), handledEventsToo: true);
        InputProxy.AddHandler(KeyDownEvent, new KeyEventHandler(OnInputProxyKeyDown), handledEventsToo: true);
        InputProxy.CharacterReceived += InputProxy_CharacterReceived;
        InputProxy.GotFocus += InputProxy_GotFocus;
        InputProxy.LostFocus += InputProxy_LostFocus;
        GotFocus += VirtualTerminalControl_GotFocus;
        LineRepeater.LayoutUpdated += LineRepeater_LayoutUpdated;

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
        LineRepeater.ItemsSource = _document.Lines;
        _selectionAnchor = 0;
        _selectionActive = 0;
        _scrollToEndAfterLayout = false;
        _scrollAnchorAfterLayout = null;
        ScrollHost.ChangeView(0, 0, null, disableAnimation: true);
        RaiseViewportChanged();
    }

    public void SetText(string text, bool shouldScrollToEnd)
    {
        var scrollRatio = ScrollHost.ScrollableHeight <= 0
            ? 0
            : ScrollHost.VerticalOffset / ScrollHost.ScrollableHeight;
        _document.SetText(text);
        LineRepeater.ItemsSource = _document.Lines;
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

    public void ScrollToEnd() => ScrollHost.ChangeView(
        null,
        ScrollHost.ScrollableHeight,
        null,
        disableAnimation: true);

    public (int FirstLine, int LastLine) GetVisibleLineRange()
    {
        if (_document.LineCount == 0)
        {
            return (0, 0);
        }

        var first = Math.Clamp((int)Math.Floor(ScrollHost.VerticalOffset / _lineHeight), 0, _document.LineCount - 1);
        var visibleCount = Math.Max(1, (int)Math.Ceiling(ScrollHost.ViewportHeight / _lineHeight));
        var last = Math.Clamp(first + visibleCount - 1, first, _document.LineCount - 1);
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

    private void VirtualTerminalControl_Loaded(object sender, RoutedEventArgs e)
    {
        MeasureTextMetrics();
        ReflowForCurrentWidth();
    }

    private void VirtualTerminalControl_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (IsLoaded)
        {
            MeasureTextMetrics();
            ReflowForCurrentWidth();
        }
    }

    private void MeasureTextMetrics()
    {
        var probe = new TextBlock
        {
            Text = new string('M', 100),
            FontFamily = FontFamily,
            FontSize = FontSize,
            TextWrapping = TextWrapping.NoWrap
        };
        probe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        _characterWidth = Math.Max(1, probe.DesiredSize.Width / 100.0);
        _lineHeight = Math.Max(1, Math.Ceiling(probe.DesiredSize.Height + 1));
    }

    private void ReflowForCurrentWidth()
    {
        var availableWidth = ScrollHost.ViewportWidth > 0 ? ScrollHost.ViewportWidth : ActualWidth;
        var columns = Math.Max(1, (int)Math.Floor((availableWidth - (HORIZONTAL_PADDING * 2)) / _characterWidth));
        var anchor = CaptureScrollAnchor();
        if (!_document.SetColumns(columns))
        {
            UpdateVisiblePresenters();
            return;
        }

        LineRepeater.ItemsSource = _document.Lines;
        RequestScrollAnchorAfterLayout(anchor);
    }

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
        RequestScrollAnchorAfterLayout(new ScrollAnchor(-1, Math.Clamp(ratio, 0, 1)));

    private void QueuePostLayoutViewportUpdate()
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            LineRepeater.UpdateLayout();
            ApplyPendingViewportChange();
        });
    }

    private void LineRepeater_LayoutUpdated(object? sender, object e) => ApplyPendingViewportChange();

    private void ApplyPendingViewportChange()
    {
        if (_scrollToEndAfterLayout)
        {
            _scrollToEndAfterLayout = false;
            ScrollToEnd();
        }
        else if (_scrollAnchorAfterLayout is ScrollAnchor anchor)
        {
            _scrollAnchorAfterLayout = null;
            if (anchor.DocumentOffset < 0)
            {
                ScrollHost.ChangeView(
                    null,
                    ScrollHost.ScrollableHeight * anchor.WithinLineOffset,
                    null,
                    disableAnimation: true);
            }
            else
            {
                RestoreScrollAnchor(anchor);
            }
        }
        else
        {
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

        var lineIndex = Math.Clamp((int)Math.Floor(ScrollHost.VerticalOffset / _lineHeight), 0, _document.LineCount - 1);
        var line = _document.GetLine(lineIndex);
        var withinLine = ScrollHost.VerticalOffset - (lineIndex * _lineHeight);
        return new ScrollAnchor(line.Start, Math.Max(0, withinLine));
    }

    private void RestoreScrollAnchor(ScrollAnchor anchor)
    {
        var lineIndex = _document.FindLineIndex(anchor.DocumentOffset);
        ScrollHost.ChangeView(
            null,
            (lineIndex * _lineHeight) + anchor.WithinLineOffset,
            null,
            disableAnimation: true);
    }

    private void LineRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is TerminalLinePresenter presenter && args.Index < _document.LineCount)
        {
            UpdatePresenter(presenter, args.Index);
        }
    }

    private void UpdateVisiblePresenters()
    {
        if (_document.LineCount == 0)
        {
            return;
        }

        var (first, last) = GetVisibleLineRange();
        first = Math.Max(0, first - VIEWPORT_PREFETCH_LINES);
        last = Math.Min(_document.LineCount - 1, last + VIEWPORT_PREFETCH_LINES);
        for (var index = first; index <= last; index++)
        {
            if (LineRepeater.TryGetElement(index) is TerminalLinePresenter presenter)
            {
                UpdatePresenter(presenter, index);
            }
        }
    }

    private void UpdatePresenter(TerminalLinePresenter presenter, int lineIndex)
    {
        var line = _document.GetLine(lineIndex);
        var selectionStart = Math.Min(_selectionAnchor, _selectionActive);
        var selectionEnd = Math.Max(_selectionAnchor, _selectionActive);
        var localStart = Math.Clamp(selectionStart, line.Start, line.End);
        var localEnd = Math.Clamp(selectionEnd, line.Start, line.End);
        var selectionStartCell = _document.GetCellOffset(line, localStart);
        var selectionEndCell = _document.GetCellOffset(line, localEnd);
        var includesLineBreak = line.BreakLength > 0 && selectionStart <= line.End && selectionEnd > line.End;
        var caretLineIndex = _document.FindLineIndex(_selectionActive);
        var showCaret = _hasInputFocus && _isCaretVisible && !HasSelection && lineIndex == caretLineIndex;
        var caretCell = showCaret ? _document.GetCellOffset(line, _selectionActive) : 0;

        presenter.Update(
            line.Text,
            _lineHeight,
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
        var point = args.GetCurrentPoint(LineRepeater);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        InputProxy.Focus(FocusState.Pointer);
        var offset = GetDocumentOffsetFromPoint(point.Position);
        _scrollToEndAfterLayout = false;
        if (!IsShiftDown())
        {
            _selectionAnchor = offset;
        }

        _selectionActive = offset;
        ResetCaretBlink();
        _isPointerSelecting = CapturePointer(args.Pointer);
        UpdateVisiblePresenters();
        args.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (!_isPointerSelecting)
        {
            return;
        }

        var rootPoint = args.GetCurrentPoint(RootGrid).Position;
        if (rootPoint.Y < 0)
        {
            ScrollHost.ChangeView(null, Math.Max(0, ScrollHost.VerticalOffset - (_lineHeight * 3)), null, true);
        }
        else if (rootPoint.Y > ActualHeight)
        {
            ScrollHost.ChangeView(null, ScrollHost.VerticalOffset + (_lineHeight * 3), null, true);
        }

        _selectionActive = GetDocumentOffsetFromPoint(args.GetCurrentPoint(LineRepeater).Position);
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

        _selectionActive = GetDocumentOffsetFromPoint(args.GetCurrentPoint(LineRepeater).Position);
        ResetCaretBlink();
        _isPointerSelecting = false;
        ReleasePointerCapture(args.Pointer);
        UpdateVisiblePresenters();
        RaiseViewportChanged();
        args.Handled = true;
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs args) => _isPointerSelecting = false;

    private void VirtualTerminalControl_GotFocus(object sender, RoutedEventArgs args)
    {
        if (!ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), InputProxy))
        {
            InputProxy.Focus(FocusState.Keyboard);
        }
    }

    private void InputProxy_GotFocus(object sender, RoutedEventArgs args)
    {
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
        var lineIndex = Math.Clamp((int)Math.Floor(position.Y / _lineHeight), 0, _document.LineCount - 1);
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

    private void InputProxy_CharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs args)
    {
        var character = args.Character;
        if (character == '\0')
        {
            return;
        }

        // CharacterReceived reports the composed character produced by the
        // current keyboard layout/IME. Handling it here gives the terminal a
        // direct key-input path without waiting for the invisible proxy to
        // retain editable text. Clipboard paste remains on TextChanged.
        args.Handled = true;
        if (_isInputEnabled && character is not '\b' and not '\u007F')
        {
            ResetCaretBlink();
            InputReceived?.Invoke(this, new TerminalInputEventArgs(character.ToString()));
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
        if (top < ScrollHost.VerticalOffset)
        {
            ScrollHost.ChangeView(null, top, null, true);
        }
        else if (bottom > ScrollHost.VerticalOffset + ScrollHost.ViewportHeight)
        {
            ScrollHost.ChangeView(null, bottom - ScrollHost.ViewportHeight, null, true);
        }
    }

    private void InputProxy_TextChanged(object sender, TextChangedEventArgs args)
    {
        if (_isClearingInputProxy || sender is not TextBox inputProxy || inputProxy.Text.Length == 0)
        {
            return;
        }

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

    private void ScrollHost_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        UpdateVisiblePresenters();
        RaiseViewportChanged();
    }

    private void RaiseViewportChanged() => ViewportChanged?.Invoke(this, EventArgs.Empty);

    private static bool IsControlDown() =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0;

    private static bool IsShiftDown() =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) != 0;

    private readonly record struct ScrollAnchor(int DocumentOffset, double WithinLineOffset);
}
