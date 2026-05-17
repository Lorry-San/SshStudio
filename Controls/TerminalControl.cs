using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace SshStudio.Controls;

public sealed class TerminalControl : Control
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<TerminalControl, string>(nameof(Text), "");

    public static readonly RoutedEvent<TerminalInputEventArgs> TerminalInputEvent =
        RoutedEvent.Register<TerminalControl, TerminalInputEventArgs>(
            nameof(TerminalInput),
            RoutingStrategies.Bubble);

    public static readonly RoutedEvent<TerminalResizeEventArgs> TerminalResizeEvent =
        RoutedEvent.Register<TerminalControl, TerminalResizeEventArgs>(
            nameof(TerminalResize),
            RoutingStrategies.Bubble);

    private const double FontSizeValue = 15.0;
    private const double LineHeightValue = 21.0;
    private const double PaddingX = 14.0;
    private const double PaddingY = 12.0;
    private int _rows = 24;
    private int _columns = 120;
    private Cell[,] _main;
    private Cell[,] _alternate;
    private readonly List<string> _scrollback = [];
    private Cell[,] _screen;
    private readonly StringBuilder _escape = new();
    private bool _inEscape;
    private int _row;
    private int _col;
    private int _savedRow;
    private int _savedCol;
    private bool _cursorVisible = true;
    private bool _alternateScreen;
    private bool _wrapPending;
    private int _parsedLength;
    private bool _isSelecting;
    private int _selectionStartRow = -1;
    private int _selectionStartCol = -1;
    private int _selectionEndRow = -1;
    private int _selectionEndCol = -1;
    private int _lastFirstScreenRow;
    private double _lastCharWidth = 9;
    private int _scrollOffset;
    private bool _inverseVideo;
    private static readonly Color DefaultForeground = Color.Parse("#D6E4F0");
    private static readonly Color DefaultBackground = Color.Parse("#07111F");
    private static readonly Color InverseForeground = Color.Parse("#E6FBFF");
    private static readonly Color InverseBackground = Color.Parse("#1D3557");
    private Color _foreground = DefaultForeground;
    private Color _background = DefaultBackground;

    public TerminalControl()
    {
        Focusable = true;
        ContextMenu = new ContextMenu
        {
            ItemsSource = new[]
            {
                new MenuItem { Header = "复制当前屏幕", Command = ReactiveCommand("copy") },
                new MenuItem { Header = "粘贴", Command = ReactiveCommand("paste") }
            }
        };
        _main = CreateBuffer(_rows, _columns);
        _alternate = CreateBuffer(_rows, _columns);
        _screen = _main;
        Clear(_main);
        Clear(_alternate);
        ContextMenu = BuildContextMenu();
    }

    private ContextMenu BuildContextMenu()
    {
        return new ContextMenu
        {
            ItemsSource = new[]
            {
                new MenuItem { Header = "\u590d\u5236\u5f53\u524d\u5c4f\u5e55", Command = ReactiveCommand("copy") },
                new MenuItem { Header = "\u7c98\u8d34", Command = ReactiveCommand("paste") }
            }
        };
    }

    public event EventHandler<TerminalInputEventArgs> TerminalInput
    {
        add => AddHandler(TerminalInputEvent, value);
        remove => RemoveHandler(TerminalInputEvent, value);
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public event EventHandler<TerminalResizeEventArgs> TerminalResize
    {
        add => AddHandler(TerminalResizeEvent, value);
        remove => RemoveHandler(TerminalResizeEvent, value);
    }

    public void FeedOutput(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var wasAtBottom = _scrollOffset == 0;
        Feed(text);
        if (wasAtBottom)
        {
            _scrollOffset = 0;
        }
        else
        {
            ClampScrollOffset();
        }
        InvalidateVisual();
    }

    public void ResetTerminal()
    {
        Reset();
        _scrollOffset = 0;
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            var text = change.GetNewValue<string>() ?? "";
            if (text.Length < _parsedLength)
            {
                Reset();
            }
            if (text.Length > _parsedLength)
            {
                Feed(text[_parsedLength..]);
                _parsedLength = text.Length;
                InvalidateVisual();
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        UpdateTerminalSize();
        context.FillRectangle(new SolidColorBrush(Color.Parse("#07111F")), Bounds);

        var typeface = new Typeface("Cascadia Mono, Consolas");
        var fontSize = FontSizeValue;
        var lineHeight = LineHeightValue;
        var charWidth = MeasureCharWidth(typeface, fontSize);
        var cursorBrush = new SolidColorBrush(Color.Parse("#2DD4BF"));
        var visibleRows = _rows;
        ClampScrollOffset();
        var firstVirtualRow = _alternateScreen
            ? 0
            : Math.Max(0, _scrollback.Count + _rows - visibleRows - _scrollOffset);
        _lastFirstScreenRow = firstVirtualRow - _scrollback.Count;
        _lastCharWidth = charWidth;
        var y = PaddingY;

        var drawnRows = 0;
        for (var virtualRow = firstVirtualRow; drawnRows < visibleRows && y < Bounds.Height - lineHeight; virtualRow++, drawnRows++)
        {
            if (!_alternateScreen && virtualRow < _scrollback.Count)
            {
                DrawText(context, _scrollback[virtualRow], PaddingX, y, typeface, fontSize, new SolidColorBrush(DefaultForeground));
            }
            else
            {
                var screenRow = _alternateScreen ? virtualRow : virtualRow - _scrollback.Count;
                if (screenRow >= 0 && screenRow < _rows)
                {
                    DrawLine(context, _screen, screenRow, PaddingX, y, charWidth, lineHeight, typeface, fontSize, _columns);
                    DrawSelectionForRow(context, screenRow, drawnRows, charWidth, lineHeight);
                }
            }
            y += lineHeight;
        }

        if (_cursorVisible && IsFocused && (_alternateScreen || _scrollOffset == 0))
        {
            var cursorVirtualRow = _alternateScreen ? _row : _scrollback.Count + _row;
            var cursorY = PaddingY + Math.Max(0, cursorVirtualRow - firstVirtualRow) * lineHeight;
            var cursorX = PaddingX + _col * charWidth;
            if (cursorY >= PaddingY && cursorY < Bounds.Height - PaddingY)
            {
                context.FillRectangle(cursorBrush, new Rect(cursorX, cursorY + 3, 2.2, lineHeight - 6));
            }
        }

        DrawScrollBar(context, visibleRows, lineHeight);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateTerminalSize();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var (row, col) = PointToCell(e.GetPosition(this));
            _selectionStartRow = row;
            _selectionStartCol = col;
            _selectionEndRow = row;
            _selectionEndCol = col;
            _isSelecting = true;
            e.Pointer.Capture(this);
            InvalidateVisual();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isSelecting)
        {
            return;
        }

        var (row, col) = PointToCell(e.GetPosition(this));
        _selectionEndRow = row;
        _selectionEndCol = col;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isSelecting)
        {
            _isSelecting = false;
            e.Pointer.Capture(null);
            InvalidateVisual();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_alternateScreen)
        {
            RaiseEvent(new TerminalInputEventArgs(TerminalInputEvent, e.Delta.Y > 0 ? "\x1B[A" : "\x1B[B"));
            e.Handled = true;
            return;
        }

        var lines = Math.Max(1, (int)Math.Round(Math.Abs(e.Delta.Y) * 3));
        if (e.Delta.Y > 0)
        {
            _scrollOffset += lines;
        }
        else
        {
            _scrollOffset -= lines;
        }

        ClampScrollOffset();
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (!string.IsNullOrEmpty(e.Text))
        {
            RaiseEvent(new TerminalInputEventArgs(TerminalInputEvent, e.Text));
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
            e.KeyModifiers.HasFlag(KeyModifiers.Shift) &&
            e.Key == Key.C)
        {
            e.Handled = true;
            CopyCurrentScreen();
            return;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
            e.KeyModifiers.HasFlag(KeyModifiers.Shift) &&
            e.Key == Key.V)
        {
            e.Handled = true;
            PasteFromClipboard();
            return;
        }

        var sequence = KeyToSequence(e);
        if (sequence is null)
        {
            return;
        }
        e.Handled = true;
        RaiseEvent(new TerminalInputEventArgs(TerminalInputEvent, sequence));
    }

    private System.Windows.Input.ICommand ReactiveCommand(string action)
    {
        return new SimpleCommand(() =>
        {
            if (action == "copy")
            {
                CopyCurrentScreen();
            }
            else
            {
                PasteFromClipboard();
            }
        });
    }

    private async void CopyCurrentScreen()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        var selected = GetSelectedText();
        await clipboard.SetTextAsync(string.IsNullOrWhiteSpace(selected) ? GetVisibleText() : selected);
    }

    private async void PasteFromClipboard()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        var text = await clipboard.GetTextAsync();
        if (!string.IsNullOrEmpty(text))
        {
            RaiseEvent(new TerminalInputEventArgs(TerminalInputEvent, text.Replace("\r\n", "\n", StringComparison.Ordinal)));
        }
    }

    private static string? KeyToSequence(KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return e.Key switch
            {
                Key.A => "\x01",
                Key.B => "\x02",
                Key.C => "\x03",
                Key.D => "\x04",
                Key.E => "\x05",
                Key.F => "\x06",
                Key.G => "\x07",
                Key.H => "\x08",
                Key.I => "\t",
                Key.J => "\n",
                Key.K => "\x0B",
                Key.L => "\x0C",
                Key.M => "\r",
                Key.N => "\x0E",
                Key.O => "\x0F",
                Key.P => "\x10",
                Key.Q => "\x11",
                Key.R => "\x12",
                Key.S => "\x13",
                Key.T => "\x14",
                Key.U => "\x15",
                Key.V => "\x16",
                Key.W => "\x17",
                Key.X => "\x18",
                Key.Y => "\x19",
                Key.Z => "\x1A",
                _ => null
            };
        }

        return e.Key switch
        {
            Key.Enter => "\r",
            Key.Back => "\x7F",
            Key.Tab => "\t",
            Key.Escape => "\x1B",
            Key.Up => "\x1B[A",
            Key.Down => "\x1B[B",
            Key.Right => "\x1B[C",
            Key.Left => "\x1B[D",
            Key.Home => "\x1B[H",
            Key.End => "\x1B[F",
            Key.Delete => "\x1B[3~",
            Key.PageUp => "\x1B[5~",
            Key.PageDown => "\x1B[6~",
            _ => null
        };
    }

    private void Feed(string text)
    {
        text = text.Replace("?2004h", "", StringComparison.Ordinal)
            .Replace("?2004l", "", StringComparison.Ordinal);
        foreach (var ch in text)
        {
            if (_inEscape)
            {
                _escape.Append(ch);
                if (IsEscapeTerminated(_escape.ToString(), ch))
                {
                    HandleEscape(_escape.ToString());
                    _escape.Clear();
                    _inEscape = false;
                }
                continue;
            }

            switch (ch)
            {
                case '\x1B':
                    _inEscape = true;
                    _escape.Clear();
                    break;
                case '\r':
                    _wrapPending = false;
                    _col = 0;
                    break;
                case '\n':
                    _wrapPending = false;
                    NewLine();
                    break;
                case '\b':
                    _wrapPending = false;
                    _col = Math.Max(0, _col - 1);
                    break;
                case '\t':
                    _wrapPending = false;
                    _col = Math.Min(_columns - 1, ((_col / 8) + 1) * 8);
                    break;
                case >= ' ':
                    Put(ch);
                    break;
            }
        }
    }

    private void HandleEscape(string sequence)
    {
        _wrapPending = false;

        if (sequence is "c")
        {
            Reset();
            return;
        }
        if (sequence == "7")
        {
            SaveCursor();
            return;
        }
        if (sequence == "8")
        {
            RestoreCursor();
            return;
        }
        if (sequence.StartsWith(']'))
        {
            return;
        }
        if (sequence.StartsWith('(') || sequence.StartsWith(')') || sequence.StartsWith('*') || sequence.StartsWith('+'))
        {
            return;
        }
        if (sequence == "D")
        {
            NewLine();
            return;
        }
        if (sequence == "E")
        {
            _col = 0;
            NewLine();
            return;
        }
        if (sequence == "M")
        {
            ReverseIndex();
            return;
        }
        if (sequence == "[?1049h" || sequence == "[?47h" || sequence == "[?1047h")
        {
            _alternateScreen = true;
            _screen = _alternate;
            Clear(_screen);
            _row = 0;
            _col = 0;
            return;
        }
        if (sequence == "[?1049l" || sequence == "[?47l" || sequence == "[?1047l")
        {
            _alternateScreen = false;
            _screen = _main;
            _row = Math.Min(_row, _rows - 1);
            _col = Math.Min(_col, _columns - 1);
            return;
        }
        if (sequence == "[?25l")
        {
            _cursorVisible = false;
            return;
        }
        if (sequence == "[?25h")
        {
            _cursorVisible = true;
            return;
        }
        if (!sequence.StartsWith('['))
        {
            return;
        }

        var command = sequence[^1];
        var body = sequence[1..^1];
        var parts = string.IsNullOrEmpty(body)
            ? []
            : body.Split(';', StringSplitOptions.None)
            .Select(part => int.TryParse(part.TrimStart('?'), out var value) ? value : 0)
            .ToArray();

        switch (command)
        {
            case 'H':
            case 'f':
                _row = Clamp((parts.Length > 0 ? parts[0] : 1) - 1, 0, _rows - 1);
                _col = Clamp((parts.Length > 1 ? parts[1] : 1) - 1, 0, _columns - 1);
                break;
            case 'A':
                _row = Clamp(_row - Param(parts, 0, 1), 0, _rows - 1);
                break;
            case 'B':
            case 'e':
                _row = Clamp(_row + Param(parts, 0, 1), 0, _rows - 1);
                break;
            case 'C':
                _col = Clamp(_col + Param(parts, 0, 1), 0, _columns - 1);
                break;
            case 'D':
                _col = Clamp(_col - Param(parts, 0, 1), 0, _columns - 1);
                break;
            case '@':
                InsertBlankChars(Param(parts, 0, 1));
                break;
            case 'P':
                DeleteChars(Param(parts, 0, 1));
                break;
            case 'L':
                InsertLines(Param(parts, 0, 1));
                break;
            case 'M':
                DeleteLines(Param(parts, 0, 1));
                break;
            case 'S':
                ScrollUp(Param(parts, 0, 1));
                break;
            case 'T':
                ScrollDown(Param(parts, 0, 1));
                break;
            case 'E':
                _row = Clamp(_row + Param(parts, 0, 1), 0, _rows - 1);
                _col = 0;
                break;
            case 'F':
                _row = Clamp(_row - Param(parts, 0, 1), 0, _rows - 1);
                _col = 0;
                break;
            case 'G':
            case '`':
                _col = Clamp(Param(parts, 0, 1) - 1, 0, _columns - 1);
                break;
            case 'd':
                _row = Clamp(Param(parts, 0, 1) - 1, 0, _rows - 1);
                break;
            case 'J':
                ClearDisplay(parts.Length > 0 ? parts[0] : 0);
                break;
            case 'K':
                ClearLine(parts.Length > 0 ? parts[0] : 0);
                break;
            case 'm':
                ApplySgr(parts);
                break;
            case 's':
                SaveCursor();
                break;
            case 'u':
                RestoreCursor();
                break;
        }
    }

    private void Put(char ch)
    {
        if (_wrapPending)
        {
            _wrapPending = false;
            NewLine();
            _col = 0;
        }

        var width = CharDisplayWidth(ch);
        if (width == 2 && _col == _columns - 1)
        {
            NewLine();
            _col = 0;
        }

        ClearCellForWrite(_row, _col);
        _screen[_row, _col].Rune = ch;
        _screen[_row, _col].Foreground = _foreground;
        _screen[_row, _col].Background = _background;
        _screen[_row, _col].Width = width;

        if (width == 2 && _col + 1 < _columns)
        {
            _screen[_row, _col + 1] = new Cell
            {
                Rune = ' ',
                Foreground = _foreground,
                Background = _background,
                Width = 0
            };
        }

        if (_col + width >= _columns)
        {
            _col = _columns - 1;
            _wrapPending = true;
            return;
        }

        _col += width;
    }

    private void ClearCellForWrite(int row, int col)
    {
        if (col > 0 && _screen[row, col].Width == 0 && _screen[row, col - 1].Width == 2)
        {
            _screen[row, col - 1] = new Cell();
        }

        if (_screen[row, col].Width == 2 && col + 1 < _columns)
        {
            _screen[row, col + 1] = new Cell();
        }

        _screen[row, col] = new Cell();
    }

    private void NewLine()
    {
        if (_row == _rows - 1)
        {
            if (!_alternateScreen)
            {
                _scrollback.Add(GetLine(0));
                if (_scrollback.Count > 800)
                {
                    _scrollback.RemoveAt(0);
                }
                ClampScrollOffset();
            }
            ScrollUp();
            return;
        }
        _row++;
    }

    private void ScrollUp()
    {
        ScrollUp(1);
    }

    private void ScrollUp(int count)
    {
        _wrapPending = false;
        count = Clamp(count, 1, _rows);
        for (var i = 0; i < count; i++)
        {
            for (var r = 1; r < _rows; r++)
            {
                for (var c = 0; c < _columns; c++)
                {
                    _screen[r - 1, c] = _screen[r, c];
                }
            }
            for (var c = 0; c < _columns; c++)
            {
                _screen[_rows - 1, c] = new Cell();
            }
        }
    }

    private void ScrollDown(int count)
    {
        _wrapPending = false;
        count = Clamp(count, 1, _rows);
        for (var i = 0; i < count; i++)
        {
            for (var r = _rows - 2; r >= 0; r--)
            {
                for (var c = 0; c < _columns; c++)
                {
                    _screen[r + 1, c] = _screen[r, c];
                }
            }
            for (var c = 0; c < _columns; c++)
            {
                _screen[0, c] = new Cell();
            }
        }
    }

    private void ReverseIndex()
    {
        _wrapPending = false;
        if (_row > 0)
        {
            _row--;
            return;
        }
        ScrollDown(1);
    }

    private void InsertBlankChars(int count)
    {
        _wrapPending = false;
        count = Clamp(count, 1, _columns - _col);
        for (var c = _columns - 1; c >= _col + count; c--)
        {
            _screen[_row, c] = _screen[_row, c - count];
        }
        for (var c = _col; c < _col + count; c++)
        {
            _screen[_row, c] = new Cell();
        }
    }

    private void DeleteChars(int count)
    {
        _wrapPending = false;
        count = Clamp(count, 1, _columns - _col);
        for (var c = _col; c < _columns - count; c++)
        {
            _screen[_row, c] = _screen[_row, c + count];
        }
        for (var c = _columns - count; c < _columns; c++)
        {
            _screen[_row, c] = new Cell();
        }
    }

    private void InsertLines(int count)
    {
        _wrapPending = false;
        count = Clamp(count, 1, _rows - _row);
        for (var r = _rows - 1; r >= _row + count; r--)
        {
            for (var c = 0; c < _columns; c++)
            {
                _screen[r, c] = _screen[r - count, c];
            }
        }
        for (var r = _row; r < _row + count; r++)
        {
            for (var c = 0; c < _columns; c++)
            {
                _screen[r, c] = new Cell();
            }
        }
    }

    private void DeleteLines(int count)
    {
        _wrapPending = false;
        count = Clamp(count, 1, _rows - _row);
        for (var r = _row; r < _rows - count; r++)
        {
            for (var c = 0; c < _columns; c++)
            {
                _screen[r, c] = _screen[r + count, c];
            }
        }
        for (var r = _rows - count; r < _rows; r++)
        {
            for (var c = 0; c < _columns; c++)
            {
                _screen[r, c] = new Cell();
            }
        }
    }

    private void ClearDisplay(int mode)
    {
        if (mode == 2 || mode == 3)
        {
            Clear(_screen);
            if (mode == 3)
            {
                _scrollback.Clear();
                _scrollOffset = 0;
            }
            _row = 0;
            _col = 0;
            _wrapPending = false;
            return;
        }

        if (mode == 1)
        {
            for (var r = 0; r <= _row; r++)
            {
                var end = r == _row ? _col : _columns - 1;
                for (var c = 0; c <= end; c++)
                {
                    _screen[r, c] = new Cell();
                }
            }
            return;
        }

        for (var r = _row; r < _rows; r++)
        {
            var start = r == _row ? _col : 0;
            for (var c = start; c < _columns; c++)
            {
                _screen[r, c] = new Cell();
            }
        }
    }

    private void ClearLine(int mode)
    {
        var start = mode == 1 ? 0 : _col;
        var end = mode == 0 ? _columns - 1 : _col;
        if (mode == 2)
        {
            start = 0;
            end = _columns - 1;
        }
        for (var c = start; c <= end; c++)
        {
            _screen[_row, c] = new Cell();
        }
        _wrapPending = false;
    }

    private string GetLine(int row)
    {
        var builder = new StringBuilder();
        for (var c = 0; c < _columns; c++)
        {
            if (_screen[row, c].Width != 0)
            {
                builder.Append(_screen[row, c].Rune);
            }
        }
        return builder.ToString().TrimEnd();
    }

    private string GetVisibleText()
    {
        var builder = new StringBuilder();
        if (!_alternateScreen)
        {
            foreach (var line in _scrollback.TakeLast(200))
            {
                builder.AppendLine(line);
            }
        }

        for (var r = 0; r < _rows; r++)
        {
            builder.AppendLine(GetLine(r));
        }

        return builder.ToString().TrimEnd();
    }

    private string GetSelectedText()
    {
        if (!HasSelection())
        {
            return "";
        }

        var (startRow, startCol, endRow, endCol) = NormalizedSelection();
        var builder = new StringBuilder();
        for (var row = startRow; row <= endRow; row++)
        {
            var from = row == startRow ? startCol : 0;
            var to = row == endRow ? endCol : _columns - 1;
            if (to < from)
            {
                continue;
            }

            var line = GetLineRange(row, from, to);
            builder.AppendLine(line.TrimEnd());
        }

        return builder.ToString().TrimEnd();
    }

    private string GetLineRange(int row, int from, int to)
    {
        var builder = new StringBuilder();
        row = Clamp(row, 0, _rows - 1);
        from = Clamp(from, 0, _columns - 1);
        to = Clamp(to, 0, _columns - 1);
        for (var c = from; c <= to; c++)
        {
            if (_screen[row, c].Width != 0)
            {
                builder.Append(_screen[row, c].Rune);
            }
        }
        return builder.ToString();
    }

    private void ClampScrollOffset()
    {
        if (_alternateScreen)
        {
            _scrollOffset = 0;
            return;
        }

        _scrollOffset = Clamp(_scrollOffset, 0, _scrollback.Count);
    }

    private (int Row, int Col) PointToCell(Point point)
    {
        var row = _lastFirstScreenRow + Clamp((int)((point.Y - PaddingY) / LineHeightValue), 0, _rows - 1);
        var col = Clamp((int)((point.X - PaddingX) / Math.Max(1, _lastCharWidth)), 0, _columns - 1);
        return (Clamp(row, 0, _rows - 1), col);
    }

    private bool HasSelection()
    {
        return _selectionStartRow >= 0 &&
               _selectionEndRow >= 0 &&
               (_selectionStartRow != _selectionEndRow || _selectionStartCol != _selectionEndCol);
    }

    private (int StartRow, int StartCol, int EndRow, int EndCol) NormalizedSelection()
    {
        var startRow = _selectionStartRow;
        var startCol = _selectionStartCol;
        var endRow = _selectionEndRow;
        var endCol = _selectionEndCol;
        if (startRow > endRow || (startRow == endRow && startCol > endCol))
        {
            (startRow, endRow) = (endRow, startRow);
            (startCol, endCol) = (endCol, startCol);
        }
        return (startRow, startCol, endRow, endCol);
    }

    private void DrawSelectionForRow(DrawingContext context, int screenRow, int drawnRow, double charWidth, double lineHeight)
    {
        if (!HasSelection())
        {
            return;
        }

        var (startRow, startCol, endRow, endCol) = NormalizedSelection();
        if (screenRow < startRow || screenRow > endRow)
        {
            return;
        }

        var from = screenRow == startRow ? startCol : 0;
        var to = screenRow == endRow ? endCol : _columns - 1;
        if (to < from)
        {
            return;
        }

        var x = PaddingX + from * charWidth;
        var y = PaddingY + drawnRow * lineHeight;
        var width = Math.Max(charWidth, (to - from + 1) * charWidth);
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(110, 45, 212, 191)), new Rect(x, y, width, lineHeight));
    }

    private void Reset()
    {
        Clear(_main);
        Clear(_alternate);
        _screen = _main;
        _scrollback.Clear();
        _row = 0;
        _col = 0;
        _savedRow = 0;
        _savedCol = 0;
        _wrapPending = false;
        _parsedLength = 0;
        _inEscape = false;
        _alternateScreen = false;
        _inverseVideo = false;
        _foreground = DefaultForeground;
        _background = DefaultBackground;
    }

    private void ApplySgr(int[] parts)
    {
        if (parts.Length == 0)
        {
            parts = [0];
        }

        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part == 0)
            {
                _inverseVideo = false;
                _foreground = DefaultForeground;
                _background = DefaultBackground;
            }
            else if (part == 27)
            {
                _inverseVideo = false;
                _foreground = DefaultForeground;
                _background = DefaultBackground;
            }
            else if (part == 39)
            {
                _foreground = _inverseVideo ? InverseForeground : DefaultForeground;
            }
            else if (part == 49)
            {
                _background = _inverseVideo ? InverseBackground : DefaultBackground;
            }
            else if (part == 7)
            {
                _inverseVideo = true;
                _foreground = InverseForeground;
                _background = InverseBackground;
            }
            else if (part is >= 30 and <= 37)
            {
                _foreground = AnsiColor(part - 30, bright: false);
            }
            else if (part == 38)
            {
                if (TryReadExtendedColor(parts, ref i, out var color))
                {
                    _foreground = color;
                }
            }
            else if (part is >= 90 and <= 97)
            {
                _foreground = AnsiColor(part - 90, bright: true);
            }
            else if (part is >= 40 and <= 47)
            {
                _background = AnsiColor(part - 40, bright: false);
            }
            else if (part == 48)
            {
                if (TryReadExtendedColor(parts, ref i, out var color))
                {
                    _background = MapTerminalBackground(color);
                }
            }
            else if (part is >= 100 and <= 107)
            {
                _background = AnsiColor(part - 100, bright: true);
            }
        }
    }

    private void SaveCursor()
    {
        _savedRow = _row;
        _savedCol = _col;
    }

    private void RestoreCursor()
    {
        _row = Clamp(_savedRow, 0, _rows - 1);
        _col = Clamp(_savedCol, 0, _columns - 1);
        _wrapPending = false;
    }

    private void UpdateTerminalSize()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var typeface = new Typeface("Cascadia Mono, Consolas");
        var charWidth = MeasureCharWidth(typeface, FontSizeValue);
        var columns = Clamp((int)((Bounds.Width - PaddingX * 2) / charWidth), 20, 240);
        var rows = Clamp((int)((Bounds.Height - PaddingY * 2) / LineHeightValue), 6, 80);
        if (columns == _columns && rows == _rows)
        {
            return;
        }

        _columns = columns;
        _rows = rows;
        ResizeBuffers(rows, columns);
        RaiseEvent(new TerminalResizeEventArgs(TerminalResizeEvent, _columns, _rows, (int)Bounds.Width, (int)Bounds.Height));
    }

    private void ResizeBuffers(int rows, int columns)
    {
        _main = ResizeBuffer(_main, rows, columns);
        _alternate = ResizeBuffer(_alternate, rows, columns);
        _screen = _alternateScreen ? _alternate : _main;
        _row = Clamp(_row, 0, rows - 1);
        _col = Clamp(_col, 0, columns - 1);
        _wrapPending = false;
    }

    private static Cell[,] ResizeBuffer(Cell[,] oldBuffer, int rows, int columns)
    {
        var buffer = CreateBuffer(rows, columns);
        var copyRows = Math.Min(rows, oldBuffer.GetLength(0));
        var copyCols = Math.Min(columns, oldBuffer.GetLength(1));
        for (var r = 0; r < copyRows; r++)
        {
            for (var c = 0; c < copyCols; c++)
            {
                buffer[r, c] = oldBuffer[r, c];
            }
        }
        return buffer;
    }

    private static Cell[,] CreateBuffer(int rows, int columns)
    {
        var buffer = new Cell[rows, columns];
        Clear(buffer);
        return buffer;
    }

    private static void Clear(Cell[,] buffer)
    {
        for (var r = 0; r < buffer.GetLength(0); r++)
        {
            for (var c = 0; c < buffer.GetLength(1); c++)
            {
                buffer[r, c] = new Cell();
            }
        }
    }

    private static bool IsEscapeTerminated(string sequence, char ch)
    {
        if (sequence.StartsWith(']'))
        {
            return ch == '\a' || sequence.EndsWith("\x1B\\", StringComparison.Ordinal);
        }
        if (sequence is "[" or "]" or "(" or ")" or "*" or "+")
        {
            return false;
        }
        if (sequence.StartsWith('(') || sequence.StartsWith(')') || sequence.StartsWith('*') || sequence.StartsWith('+') || sequence.StartsWith('#') || sequence.StartsWith('%'))
        {
            return sequence.Length >= 2;
        }
        if (sequence.StartsWith('[') && (ch is >= '0' and <= '?' || ch is >= ' ' and <= '/'))
        {
            return false;
        }
        if (!sequence.StartsWith('['))
        {
            return true;
        }
        return ch is >= '@' and <= '~';
    }

    private static int Param(int[] parts, int index, int fallback)
    {
        if (index >= parts.Length || parts[index] == 0)
        {
            return fallback;
        }
        return parts[index];
    }

    private static int Clamp(int value, int min, int max)
    {
        return Math.Min(max, Math.Max(min, value));
    }

    private static int CharDisplayWidth(char ch)
    {
        if (ch == '\0')
        {
            return 0;
        }

        var code = ch;
        return code is
            >= '\u1100' and <= '\u115F' or
            >= '\u2329' and <= '\u232A' or
            >= '\u2E80' and <= '\uA4CF' or
            >= '\uAC00' and <= '\uD7A3' or
            >= '\uF900' and <= '\uFAFF' or
            >= '\uFE10' and <= '\uFE19' or
            >= '\uFE30' and <= '\uFE6F' or
            >= '\uFF00' and <= '\uFF60' or
            >= '\uFFE0' and <= '\uFFE6'
            ? 2
            : 1;
    }

    private static void DrawText(DrawingContext context, string text, double x, double y, Typeface typeface, double size, IBrush brush)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            size,
            brush);
        context.DrawText(formatted, new Point(x, y));
    }

    private static double MeasureCharWidth(Typeface typeface, double size)
    {
        var formatted = new FormattedText(
            "M",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            size,
            Brushes.White);
        return Math.Ceiling(formatted.WidthIncludingTrailingWhitespace);
    }

    private static void DrawLine(
        DrawingContext context,
        Cell[,] screen,
        int row,
        double x,
        double y,
        double charWidth,
        double lineHeight,
        Typeface typeface,
        double size,
        int columns)
    {
        var run = new StringBuilder();
        Color? foreground = null;
        Color? background = null;
        var start = 0;
        var runCells = 0;
        for (var c = 0; c < columns; c++)
        {
            var cell = screen[row, c];
            if (cell.Width == 0)
            {
                continue;
            }

            if (foreground is null)
            {
                foreground = cell.Foreground;
                background = cell.Background;
                start = c;
                runCells = 0;
            }
            if (cell.Foreground != foreground || cell.Background != background)
            {
                DrawRun(context, run.ToString(), runCells, x + start * charWidth, y, lineHeight, charWidth, typeface, size, foreground.Value, background ?? Color.Parse("#07111F"));
                run.Clear();
                foreground = cell.Foreground;
                background = cell.Background;
                start = c;
                runCells = 0;
            }
            run.Append(cell.Rune);
            runCells += Math.Max(1, cell.Width);
        }
        if (foreground is not null)
        {
            var finalBackground = background ?? DefaultBackground;
            var finalText = finalBackground == DefaultBackground ? run.ToString().TrimEnd() : run.ToString();
            var finalCells = finalText.Length == run.Length ? runCells : finalText.Sum(CharDisplayWidth);
            DrawRun(context, finalText, finalCells, x + start * charWidth, y, lineHeight, charWidth, typeface, size, foreground.Value, finalBackground);
        }
    }

    private static void DrawRun(DrawingContext context, string text, int cellWidth, double x, double y, double lineHeight, double charWidth, Typeface typeface, double size, Color foreground, Color background)
    {
        if (text.Length == 0)
        {
            return;
        }
        if (background != DefaultBackground)
        {
            context.FillRectangle(new SolidColorBrush(background), new Rect(x, y, cellWidth * charWidth, lineHeight));
        }
        DrawText(context, text, x, y, typeface, size, new SolidColorBrush(foreground));
    }

    private void DrawScrollBar(DrawingContext context, int visibleRows, double lineHeight)
    {
        if (_alternateScreen || _scrollback.Count == 0 || Bounds.Height <= PaddingY * 2)
        {
            return;
        }

        var totalRows = _scrollback.Count + _rows;
        if (totalRows <= visibleRows)
        {
            return;
        }

        var trackX = Bounds.Width - 8;
        var trackY = PaddingY;
        var trackHeight = Math.Max(12, Bounds.Height - PaddingY * 2);
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(70, 148, 163, 184)), new Rect(trackX, trackY, 4, trackHeight), 2);

        var thumbHeight = Math.Max(28, trackHeight * visibleRows / totalRows);
        var maxOffset = Math.Max(1, _scrollback.Count);
        var topRatio = 1.0 - (_scrollOffset / (double)maxOffset);
        var thumbY = trackY + (trackHeight - thumbHeight) * topRatio;
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(210, 45, 212, 191)), new Rect(trackX, thumbY, 4, thumbHeight), 2);
    }

    private static Color AnsiColor(int index, bool bright)
    {
        Color[] normal =
        [
            Color.Parse("#07111F"), Color.Parse("#EF4444"), Color.Parse("#22C55E"), Color.Parse("#F59E0B"),
            Color.Parse("#3B82F6"), Color.Parse("#A855F7"), Color.Parse("#14B8A6"), Color.Parse("#26364A")
        ];
        Color[] vivid =
        [
            Color.Parse("#64748B"), Color.Parse("#F87171"), Color.Parse("#86EFAC"), Color.Parse("#FCD34D"),
            Color.Parse("#60A5FA"), Color.Parse("#C084FC"), Color.Parse("#5EEAD4"), Color.Parse("#334155")
        ];
        return (bright ? vivid : normal)[Math.Clamp(index, 0, 7)];
    }

    private static bool TryReadExtendedColor(int[] parts, ref int index, out Color color)
    {
        color = DefaultForeground;
        if (index + 2 >= parts.Length)
        {
            return false;
        }

        var mode = parts[index + 1];
        if (mode == 5)
        {
            color = Xterm256(parts[index + 2]);
            index += 2;
            return true;
        }

        if (mode == 2 && index + 4 < parts.Length)
        {
            color = Color.FromRgb(
                (byte)Clamp(parts[index + 2], 0, 255),
                (byte)Clamp(parts[index + 3], 0, 255),
                (byte)Clamp(parts[index + 4], 0, 255));
            index += 4;
            return true;
        }

        return false;
    }

    private static Color Xterm256(int index)
    {
        index = Clamp(index, 0, 255);
        if (index < 16)
        {
            return AnsiColor(index % 8, index >= 8);
        }

        if (index is >= 16 and <= 231)
        {
            var value = index - 16;
            var r = value / 36;
            var g = value / 6 % 6;
            var b = value % 6;
            return Color.FromRgb(Component(r), Component(g), Component(b));
        }

        var gray = (byte)(8 + (index - 232) * 10);
        return Color.FromRgb(gray, gray, gray);
    }

    private static byte Component(int value)
    {
        return (byte)(value == 0 ? 0 : 55 + value * 40);
    }

    private static Color MapTerminalBackground(Color color)
    {
        var brightness = (color.R * 0.299) + (color.G * 0.587) + (color.B * 0.114);
        if (brightness < 185)
        {
            return color;
        }

        return Color.FromRgb(
            (byte)Math.Max(24, color.R * 0.25),
            (byte)Math.Max(34, color.G * 0.28),
            (byte)Math.Max(48, color.B * 0.32));
    }

    private struct Cell
    {
        public char Rune = ' ';
        public Color Foreground = Color.Parse("#D6E4F0");
        public Color Background = Color.Parse("#07111F");
        public int Width = 1;

        public Cell()
        {
        }
    }
}

public sealed class TerminalInputEventArgs : RoutedEventArgs
{
    public TerminalInputEventArgs(RoutedEvent routedEvent, string text)
        : base(routedEvent)
    {
        Text = text;
    }

    public string Text { get; }
}

public sealed class TerminalResizeEventArgs : RoutedEventArgs
{
    public TerminalResizeEventArgs(RoutedEvent routedEvent, int columns, int rows, int width, int height)
        : base(routedEvent)
    {
        Columns = columns;
        Rows = rows;
        Width = width;
        Height = height;
    }

    public int Columns { get; }
    public int Rows { get; }
    public int Width { get; }
    public int Height { get; }
}

internal sealed class SimpleCommand : System.Windows.Input.ICommand
{
    private readonly Action _execute;

    public SimpleCommand(Action execute)
    {
        _execute = execute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter)
    {
        return true;
    }

    public void Execute(object? parameter)
    {
        _execute();
    }
}
