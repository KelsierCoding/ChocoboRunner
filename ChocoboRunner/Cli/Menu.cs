namespace ChocoboRunner.Cli;

public class Menu<T>
{
    private readonly IList<T> _items;
    private readonly Func<T, string> _toText;

    public string? Title { get; set; }
    public int PageSize { get; set; } = 0;
    public bool ShowPagingHint { get; init; } = true;
    private char SelectorChar { get; set; } = '>';
    private char UnselectedChar { get; set; } = ' ';

    public int SelectedIndex { get; private set; } = 0;

    private int _lastRenderedLines = 0;
    private int _startLeft;
    private int _startTop;
    private int _origLeft;
    private int _origTop;

    public Menu(IEnumerable<T> items, Func<T, string>? toText = null)
    {
        _items = items as IList<T> ?? items.ToList();
        _toText = toText ?? (x => x?.ToString() ?? string.Empty);
    }

    public T? Show(bool askConfirmation = false)
    {
        if (_items.Count == 0) return default;
        Console.CursorVisible = false;
        try
        {
            SaveCursorPosition();
            int index = RunMenuLoop(askConfirmation);
            ClearRenderedArea();
            RestoreCursorPosition();

            return index < 0 ? default : _items[index];
        }
        finally
        {
            Console.ResetColor();
            Console.CursorVisible = true;
        }
    }

    public int ShowIndex(bool askConfirmation = false)
    {
        if (_items.Count == 0) return -1;
        Console.CursorVisible = false;
        try
        {
            SaveCursorPosition();
            int index = RunMenuLoop(askConfirmation);
            ClearRenderedArea();
            RestoreCursorPosition();
            return index;
        }
        finally
        {
            Console.ResetColor();
            Console.CursorVisible = true;
        }
    }

    private void SaveCursorPosition()
    {
        _origLeft = Console.CursorLeft;
        _origTop = Console.CursorTop;
        _startLeft = _origLeft;
        _startTop = _origTop;
    }

    private void RestoreCursorPosition()
    {
        int restoreLeft = Math.Min(_origLeft, Math.Max(0, Console.WindowWidth - 1));
        int restoreTop = Math.Min(_origTop, Math.Max(0, Console.WindowHeight - 1));
        Console.SetCursorPosition(restoreLeft, restoreTop);
    }

    private int RunMenuLoop(bool askConfirmation)
    {
        int topIndex = 0;
        if (PageSize <= 0) PageSize = _items.Count;

        do
        {
            Render(topIndex);
            ConsoleKeyInfo keyInfo = Console.ReadKey(true);
            ConsoleKey key = keyInfo.Key;

            switch (key)
            {
                case ConsoleKey.UpArrow:
                    SelectedIndex = (SelectedIndex - 1 + _items.Count) % _items.Count;
                    if (SelectedIndex < topIndex) topIndex = SelectedIndex;
                    break;
                case ConsoleKey.DownArrow:
                    SelectedIndex = (SelectedIndex + 1) % _items.Count;
                    if (SelectedIndex >= topIndex + PageSize) topIndex = SelectedIndex - PageSize + 1;
                    break;
                case ConsoleKey.PageUp:
                    SelectedIndex = Math.Max(0, SelectedIndex - PageSize);
                    topIndex = Math.Max(0, topIndex - PageSize);
                    break;
                case ConsoleKey.PageDown:
                    SelectedIndex = Math.Min(_items.Count - 1, SelectedIndex + PageSize);
                    topIndex = Math.Min(Math.Max(0, _items.Count - PageSize), topIndex + PageSize);
                    break;
                case ConsoleKey.Home:
                    SelectedIndex = 0;
                    topIndex = 0;
                    break;
                case ConsoleKey.End:
                    SelectedIndex = _items.Count - 1;
                    topIndex = Math.Max(0, _items.Count - PageSize);
                    break;
                case ConsoleKey.Enter:
                    if (!askConfirmation)
                    {
                        return SelectedIndex;
                    }
                    else
                    {
                        ClearRenderedArea();

                        int promptTop = _startTop;
                        if (promptTop >= Console.WindowHeight) promptTop = Console.WindowHeight - 1;

                        bool? confirmed = ConfirmSelection(promptTop);

                        ClearConfirmationArea(promptTop);
                        if (confirmed == true)
                        {
                            return SelectedIndex;
                        }
                    }

                    break;
            }
        } while (true);
    }

    private void Render(int topIndex)
    {
        int end = Math.Min(_items.Count, topIndex + PageSize);
        List<string> lines = [];

        if (!string.IsNullOrEmpty(Title))
        {
            lines.Add(Title);
            lines.Add(new string('-', Math.Min(Console.WindowWidth - 1, Title.Length)));
        }

        for (int i = topIndex; i < end; i++)
        {
            bool isSelected = i == SelectedIndex;
            char marker = isSelected ? SelectorChar : UnselectedChar;

            string text = _toText(_items[i]);
            int maxWidth = Math.Max(0, Console.WindowWidth - 4);
            if (text.Length > maxWidth) text = text[..(maxWidth - 1)] + "…";

            lines.Add($"{marker} {text}");
        }

        if (ShowPagingHint && _items.Count > PageSize)
        {
            lines.Add(string.Empty);
            lines.Add(
                $"Showing {topIndex + 1}-{end} of {_items.Count}. Use arrows, PageUp/PageDown, Home/End. Enter to select, Esc to cancel.");
        }

        _lastRenderedLines = lines.Count;

        int writeLeft = _startLeft;
        int writeTop = _startTop;

        for (int i = 0; i < lines.Count; i++)
        {
            int targetTop = writeTop + i;

            if (targetTop >= Console.WindowHeight)
            {
                int shift = targetTop - (Console.WindowHeight - 1);
                _startTop = Math.Max(0, _startTop - shift);
                writeTop = _startTop;
                targetTop = writeTop + i;
            }

            Console.SetCursorPosition(writeLeft, targetTop);

            string toWrite = lines[i];
            int width = Console.WindowWidth - writeLeft;
            if (toWrite.Length > width) toWrite = toWrite[..width];
            else if (toWrite.Length < width) toWrite = toWrite.PadRight(width);

            Console.Write(toWrite);
        }

        int afterTop = _startTop + _lastRenderedLines;
        if (afterTop >= Console.WindowHeight) afterTop = Console.WindowHeight - 1;
        Console.SetCursorPosition(_startLeft, afterTop);
    }

    private bool? ConfirmSelection(int promptTop)
    {
        string prompt = $"Confirm selection \"{_toText(_items[SelectedIndex])}\"? (Y/n) or press ESC to quit";
        int left = _startLeft;
        int width = Console.WindowWidth - left;
        string toWrite = prompt.Length > width ? prompt[..width] : prompt.PadRight(width);

        if (promptTop >= Console.WindowHeight) promptTop = Console.WindowHeight - 1;
        Console.SetCursorPosition(left, promptTop);
        Console.Write(toWrite, Console.ForegroundColor = ConsoleColor.Cyan);
        Console.ResetColor();

        while (true)
        {
            ConsoleKeyInfo keyInfo = Console.ReadKey(true);
            switch (keyInfo.Key)
            {
                case ConsoleKey.Y:
                case ConsoleKey.Enter:
                    return true;
                case ConsoleKey.N:
                    return false;
                case ConsoleKey.Escape:
                    Environment.Exit(0);
                    break;
            }
        }
    }

    private void ClearConfirmationArea(int promptTop)
    {
        int left = _startLeft;
        int width = Console.WindowWidth - left;
        string blank = new string(' ', Math.Max(0, width));
        if (promptTop >= Console.WindowHeight) return;
        Console.SetCursorPosition(left, promptTop);
        Console.Write(blank);
    }

    private void ClearRenderedArea()
    {
        int writeLeft = _startLeft;
        int writeTop = _startTop;
        int width = Console.WindowWidth - writeLeft;
        string blank = new string(' ', Math.Max(0, width));

        for (int i = 0; i < _lastRenderedLines; i++)
        {
            int targetTop = writeTop + i;
            if (targetTop >= Console.WindowHeight) break;
            Console.SetCursorPosition(writeLeft, targetTop);
            Console.Write(blank);
        }

        _lastRenderedLines = 0;
    }
}
