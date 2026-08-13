using Avalonia.Input;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Editor;

namespace Harness.Presentation.Avalonia;

internal enum VimEditorMode
{
    Inactive,
    Normal,
    Insert,
    Visual,
    VisualLine,
}

internal sealed class VimEditorController
{
    private const int MaximumCount = 9_999;
    private readonly IWorkbenchEditorAdapter editor;
    private VimOperator pendingOperator;
    private bool pendingGo;
    private int count;
    private int operatorCount = 1;
    private int visualAnchor;
    private string register = string.Empty;
    private bool registerIsLinewise;

    internal VimEditorController(
        IWorkbenchEditorAdapter editor,
        EditorInputMode inputMode)
    {
        this.editor = editor;
        SetInputMode(inputMode);
    }

    internal event EventHandler? StateChanged;
    internal VimEditorMode Mode { get; private set; }
    internal string StatusText => Mode switch
    {
        VimEditorMode.Inactive => "",
        VimEditorMode.Normal => PendingStatus("VIM NORMAL"),
        VimEditorMode.Insert => "VIM INSERT",
        VimEditorMode.Visual => PendingStatus("VIM VISUAL"),
        VimEditorMode.VisualLine => PendingStatus("VIM VISUAL LINE"),
        _ => "VIM",
    };

    internal void SetInputMode(EditorInputMode inputMode)
    {
        VimEditorMode next = inputMode is EditorInputMode.Vim
            ? VimEditorMode.Normal
            : VimEditorMode.Inactive;
        if (Mode == next) return;
        Mode = next;
        ResetPending();
        editor.Select(editor.CaretOffset, 0);
        Notify();
    }

    internal bool ShouldHandle(KeyEventArgs args)
    {
        if (Mode is VimEditorMode.Inactive ||
            args.KeyModifiers.HasFlag(KeyModifiers.Alt) ||
            args.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            return false;
        }
        if (args.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return args.KeyModifiers is KeyModifiers.Control &&
                   (!editor.IsTextCompositionActive && args.Key is Key.OemOpenBrackets ||
                    Mode is not VimEditorMode.Insert && args.Key is Key.R);
        }
        if (Mode is VimEditorMode.Insert)
        {
            return !editor.IsTextCompositionActive && args.Key is Key.Escape;
        }
        if (pendingOperator is not VimOperator.None || pendingGo) return true;
        if (args.Key is Key.Escape or >= Key.D0 and <= Key.D9) return true;
        return args.Key is Key.H or Key.J or Key.K or Key.L or Key.W or Key.B or Key.E or
            Key.G or Key.I or Key.A or Key.O or Key.X or Key.D or Key.C or Key.Y or
            Key.P or Key.U or Key.V;
    }

    internal bool Handle(KeyEventArgs args)
    {
        if (Mode is VimEditorMode.Inactive ||
            args.KeyModifiers.HasFlag(KeyModifiers.Alt) ||
            args.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            return false;
        }

        bool control = args.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = args.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (control)
        {
            if (args.Key is Key.OemOpenBrackets)
            {
                if (editor.IsTextCompositionActive) return false;
                EnterNormal();
                return true;
            }
            if (args.Key is Key.R && Mode is not VimEditorMode.Insert)
            {
                editor.Redo();
                ResetPending();
                Notify();
                return true;
            }
            return false;
        }

        if (Mode is VimEditorMode.Insert)
        {
            if (args.Key is not Key.Escape || editor.IsTextCompositionActive) return false;
            EnterNormal(moveLeft: true);
            return true;
        }

        if (args.Key is Key.Escape)
        {
            EnterNormal();
            return true;
        }

        if (!shift && args.Key is >= Key.D1 and <= Key.D9)
        {
            AppendCount((int)args.Key - (int)Key.D0);
            return true;
        }
        if (!shift && args.Key is Key.D0 && count > 0)
        {
            AppendCount(0);
            return true;
        }

        if (Mode is VimEditorMode.Visual or VimEditorMode.VisualLine)
        {
            return HandleVisual(args.Key, shift);
        }

        if (pendingOperator is not VimOperator.None)
        {
            return HandlePendingOperator(args.Key, shift);
        }
        if (pendingGo)
        {
            if (args.Key is Key.G && !shift)
            {
                MoveToLine(Math.Max(0, ConsumeCount(1) - 1));
                ResetPending();
                Notify();
                return true;
            }
            ResetPending();
        }

        bool hadExplicitCount = count > 0;
        int repetitions = ConsumeCount(1);
        switch (args.Key)
        {
            case Key.H when !shift:
                MoveHorizontal(-repetitions);
                break;
            case Key.L when !shift:
                MoveHorizontal(repetitions);
                break;
            case Key.J when !shift:
                MoveVertical(repetitions);
                break;
            case Key.K when !shift:
                MoveVertical(-repetitions);
                break;
            case Key.W when !shift:
                editor.CaretOffset = MoveWordForward(editor.CaretOffset, repetitions);
                break;
            case Key.B when !shift:
                editor.CaretOffset = MoveWordBackward(editor.CaretOffset, repetitions);
                break;
            case Key.E when !shift:
                editor.CaretOffset = MoveWordEnd(editor.CaretOffset, repetitions);
                break;
            case Key.D0 when !shift:
                editor.CaretOffset = LineStart(editor.CaretOffset);
                break;
            case Key.D4 when shift:
                editor.CaretOffset = LineEnd(editor.CaretOffset);
                break;
            case Key.G when shift:
                MoveToLine(hadExplicitCount ? repetitions - 1 : editor.LineCount - 1);
                break;
            case Key.G when !shift:
                count = repetitions == 1 ? 0 : repetitions;
                pendingGo = true;
                Notify();
                return true;
            case Key.I when !shift:
                EnterInsert();
                return true;
            case Key.I when shift:
                editor.CaretOffset = FirstNonWhitespace(editor.CaretOffset);
                EnterInsert();
                return true;
            case Key.A when !shift:
                editor.CaretOffset = Math.Min(LineEnd(editor.CaretOffset), editor.CaretOffset + 1);
                EnterInsert();
                return true;
            case Key.A when shift:
                editor.CaretOffset = LineEnd(editor.CaretOffset);
                EnterInsert();
                return true;
            case Key.O when !shift:
                OpenLine(after: true);
                return true;
            case Key.O when shift:
                OpenLine(after: false);
                return true;
            case Key.X when !shift:
                DeleteCharacters(repetitions, backwards: false);
                break;
            case Key.X when shift:
                DeleteCharacters(repetitions, backwards: true);
                break;
            case Key.D when shift:
                ApplyOperator(VimOperator.Delete, editor.CaretOffset,
                    LineEnd(editor.CaretOffset), linewise: false);
                break;
            case Key.C when shift:
                ApplyOperator(VimOperator.Change, editor.CaretOffset,
                    LineEnd(editor.CaretOffset), linewise: false);
                return true;
            case Key.Y when shift:
                ApplyLineOperator(VimOperator.Yank, repetitions);
                break;
            case Key.D when !shift:
                BeginOperator(VimOperator.Delete, repetitions);
                return true;
            case Key.Y when !shift:
                BeginOperator(VimOperator.Yank, repetitions);
                return true;
            case Key.C when !shift:
                BeginOperator(VimOperator.Change, repetitions);
                return true;
            case Key.P:
                Paste(before: shift, repetitions);
                break;
            case Key.U when !shift:
                editor.Undo();
                break;
            case Key.V when !shift:
                EnterVisual(linewise: false);
                return true;
            case Key.V when shift:
                EnterVisual(linewise: true);
                return true;
            default:
                ResetPending();
                return false;
        }

        ResetPending();
        Notify();
        return true;
    }

    private bool HandlePendingOperator(Key key, bool shift)
    {
        VimOperator operation = pendingOperator;
        int repetitions = Math.Min(MaximumCount, operatorCount * ConsumeCount(1));
        if ((operation is VimOperator.Delete && key is Key.D && !shift) ||
            (operation is VimOperator.Yank && key is Key.Y && !shift) ||
            (operation is VimOperator.Change && key is Key.C && !shift))
        {
            ApplyLineOperator(operation, repetitions);
            ResetPending();
            Notify();
            return true;
        }

        int start = editor.CaretOffset;
        int target = key switch
        {
            Key.W when !shift => MoveWordForward(start, repetitions),
            Key.E when !shift => Math.Min(editor.TextLength,
                MoveWordEnd(start, repetitions) + 1),
            Key.B when !shift => MoveWordBackward(start, repetitions),
            Key.D0 when !shift => LineStart(start),
            Key.D4 when shift => LineEnd(start),
            Key.G when shift => editor.TextLength,
            _ => -1,
        };
        if (target < 0)
        {
            ResetPending();
            Notify();
            return false;
        }

        ApplyOperator(operation, Math.Min(start, target), Math.Max(start, target),
            linewise: false);
        ResetPending();
        Notify();
        return true;
    }

    private bool HandleVisual(Key key, bool shift)
    {
        int repetitions = ConsumeCount(1);
        switch (key)
        {
            case Key.H when !shift: MoveHorizontal(-repetitions); break;
            case Key.L when !shift: MoveHorizontal(repetitions); break;
            case Key.J when !shift: MoveVertical(repetitions); break;
            case Key.K when !shift: MoveVertical(-repetitions); break;
            case Key.W when !shift:
                editor.CaretOffset = MoveWordForward(editor.CaretOffset, repetitions); break;
            case Key.B when !shift:
                editor.CaretOffset = MoveWordBackward(editor.CaretOffset, repetitions); break;
            case Key.E when !shift:
                editor.CaretOffset = MoveWordEnd(editor.CaretOffset, repetitions); break;
            case Key.D0 when !shift: editor.CaretOffset = LineStart(editor.CaretOffset); break;
            case Key.D4 when shift: editor.CaretOffset = LineEnd(editor.CaretOffset); break;
            case Key.G when shift: MoveToLine(editor.LineCount - 1); break;
            case Key.D when !shift:
            case Key.X when !shift:
                ApplyVisualOperator(VimOperator.Delete);
                return true;
            case Key.Y when !shift:
                ApplyVisualOperator(VimOperator.Yank);
                return true;
            case Key.C when !shift:
                ApplyVisualOperator(VimOperator.Change);
                return true;
            case Key.P:
                ReplaceVisual(repetitions);
                return true;
            case Key.V:
                EnterNormal();
                return true;
            default:
                ResetPending();
                return false;
        }
        UpdateVisualSelection();
        ResetPending();
        Notify();
        return true;
    }

    private void BeginOperator(VimOperator operation, int repetitions)
    {
        pendingOperator = operation;
        operatorCount = repetitions;
        count = 0;
        Notify();
    }

    private void ApplyLineOperator(VimOperator operation, int lines)
    {
        int start = LineStart(editor.CaretOffset);
        WorkbenchCodePosition position = editor.GetPosition(start);
        int endLine = Math.Min(editor.LineCount - 1, position.Line + Math.Max(1, lines) - 1);
        int end = endLine + 1 < editor.LineCount
            ? editor.GetOffset(new(endLine + 1, 0))
            : editor.TextLength;
        ApplyOperator(operation, start, end, linewise: true);
    }

    private void ApplyVisualOperator(VimOperator operation)
    {
        int start;
        int end;
        bool linewise = Mode is VimEditorMode.VisualLine;
        if (linewise)
        {
            int low = Math.Min(visualAnchor, editor.CaretOffset);
            int high = Math.Max(visualAnchor, editor.CaretOffset);
            start = LineStart(low);
            WorkbenchCodePosition highPosition = editor.GetPosition(high);
            end = highPosition.Line + 1 < editor.LineCount
                ? editor.GetOffset(new(highPosition.Line + 1, 0))
                : editor.TextLength;
        }
        else
        {
            start = Math.Min(visualAnchor, editor.CaretOffset);
            end = Math.Min(editor.TextLength, Math.Max(visualAnchor, editor.CaretOffset) + 1);
        }
        ApplyOperator(operation, start, end, linewise);
        if (operation is not VimOperator.Change) EnterNormal();
    }

    private void ApplyOperator(
        VimOperator operation,
        int start,
        int end,
        bool linewise)
    {
        start = Math.Clamp(start, 0, editor.TextLength);
        end = Math.Clamp(end, start, editor.TextLength);
        if (end == start) return;
        register = editor.Text.Substring(start, end - start);
        registerIsLinewise = linewise;
        CopyRegisterToClipboard();
        if (operation is VimOperator.Yank || editor.IsReadOnly)
        {
            editor.CaretOffset = start;
            editor.Select(start, 0);
            return;
        }
        editor.Replace(start, end - start, string.Empty);
        editor.CaretOffset = Math.Min(start, editor.TextLength);
        editor.Select(editor.CaretOffset, 0);
        if (operation is VimOperator.Change) EnterInsert();
    }

    private void DeleteCharacters(int amount, bool backwards)
    {
        int start = backwards
            ? Math.Max(LineStart(editor.CaretOffset), editor.CaretOffset - amount)
            : editor.CaretOffset;
        int end = backwards
            ? editor.CaretOffset
            : Math.Min(LineEnd(editor.CaretOffset), editor.CaretOffset + amount);
        ApplyOperator(VimOperator.Delete, start, end, linewise: false);
    }

    private void Paste(bool before, int repetitions)
    {
        if (editor.IsReadOnly || register.Length == 0) return;
        string text = string.Concat(Enumerable.Repeat(register, Math.Max(1, repetitions)));
        int offset;
        if (registerIsLinewise)
        {
            int start = LineStart(editor.CaretOffset);
            WorkbenchCodePosition position = editor.GetPosition(start);
            offset = before || position.Line + 1 >= editor.LineCount
                ? start
                : editor.GetOffset(new(position.Line + 1, 0));
            if (!before && position.Line + 1 >= editor.LineCount && editor.TextLength > 0 &&
                !EndsWithLineBreak(editor.Text))
            {
                text = Environment.NewLine + text;
                offset = editor.TextLength;
            }
        }
        else
        {
            offset = before ? editor.CaretOffset : Math.Min(editor.TextLength, editor.CaretOffset + 1);
        }
        editor.Insert(offset, text);
        editor.CaretOffset = Math.Min(editor.TextLength, offset + text.Length - 1);
        CopyRegisterToClipboard();
    }

    private void ReplaceVisual(int repetitions)
    {
        if (editor.IsReadOnly || register.Length == 0) return;
        int start = editor.SelectionStart;
        int length = editor.SelectionLength;
        editor.Replace(start, length,
            string.Concat(Enumerable.Repeat(register, Math.Max(1, repetitions))));
        editor.CaretOffset = Math.Min(start, editor.TextLength);
        EnterNormal();
        CopyRegisterToClipboard();
    }

    private void OpenLine(bool after)
    {
        if (editor.IsReadOnly) return;
        int start = LineStart(editor.CaretOffset);
        int end = LineEnd(editor.CaretOffset);
        string lineBreak = DetectLineBreak(editor.Text);
        int offset = after ? end : start;
        string inserted = after ? lineBreak : lineBreak;
        editor.Insert(offset, inserted);
        editor.CaretOffset = after ? offset + inserted.Length : offset;
        EnterInsert();
    }

    private void EnterVisual(bool linewise)
    {
        Mode = linewise ? VimEditorMode.VisualLine : VimEditorMode.Visual;
        visualAnchor = editor.CaretOffset;
        UpdateVisualSelection();
        ResetPending();
        Notify();
    }

    private void UpdateVisualSelection()
    {
        if (Mode is VimEditorMode.VisualLine)
        {
            int low = LineStart(Math.Min(visualAnchor, editor.CaretOffset));
            WorkbenchCodePosition high = editor.GetPosition(Math.Max(visualAnchor, editor.CaretOffset));
            int end = high.Line + 1 < editor.LineCount
                ? editor.GetOffset(new(high.Line + 1, 0))
                : editor.TextLength;
            editor.Select(low, Math.Max(0, end - low));
            return;
        }
        int start = Math.Min(visualAnchor, editor.CaretOffset);
        int endInclusive = Math.Max(visualAnchor, editor.CaretOffset);
        editor.Select(start, Math.Min(editor.TextLength - start, endInclusive - start + 1));
    }

    private void EnterInsert()
    {
        Mode = VimEditorMode.Insert;
        ResetPending();
        editor.Select(editor.CaretOffset, 0);
        Notify();
    }

    private void EnterNormal(bool moveLeft = false)
    {
        if (moveLeft && editor.CaretOffset > LineStart(editor.CaretOffset))
            editor.CaretOffset--;
        Mode = VimEditorMode.Normal;
        ResetPending();
        editor.Select(editor.CaretOffset, 0);
        Notify();
    }

    private void MoveHorizontal(int delta)
    {
        int start = LineStart(editor.CaretOffset);
        int end = LineEnd(editor.CaretOffset);
        editor.CaretOffset = Math.Clamp(editor.CaretOffset + delta, start, end);
    }

    private void MoveVertical(int delta)
    {
        WorkbenchCodePosition position = editor.CaretPosition;
        int targetLine = Math.Clamp(position.Line + delta, 0, editor.LineCount - 1);
        editor.CaretOffset = editor.GetOffset(new(targetLine, position.Character));
    }

    private void MoveToLine(int zeroBasedLine)
    {
        int line = Math.Clamp(zeroBasedLine, 0, editor.LineCount - 1);
        editor.CaretOffset = editor.GetOffset(new(line, 0));
    }

    private int MoveWordForward(int offset, int repetitions)
    {
        string text = editor.Text;
        for (int repetition = 0; repetition < repetitions && offset < text.Length; repetition++)
        {
            VimCharacterClass current = CharacterClass(text[offset]);
            while (offset < text.Length && CharacterClass(text[offset]) == current) offset++;
            while (offset < text.Length && char.IsWhiteSpace(text[offset])) offset++;
        }
        return Math.Clamp(offset, 0, text.Length);
    }

    private int MoveWordBackward(int offset, int repetitions)
    {
        string text = editor.Text;
        for (int repetition = 0; repetition < repetitions && offset > 0; repetition++)
        {
            offset--;
            while (offset > 0 && char.IsWhiteSpace(text[offset])) offset--;
            VimCharacterClass current = CharacterClass(text[offset]);
            while (offset > 0 && CharacterClass(text[offset - 1]) == current) offset--;
        }
        return Math.Clamp(offset, 0, text.Length);
    }

    private int MoveWordEnd(int offset, int repetitions)
    {
        string text = editor.Text;
        for (int repetition = 0; repetition < repetitions && offset < text.Length; repetition++)
        {
            while (offset < text.Length && char.IsWhiteSpace(text[offset])) offset++;
            if (offset >= text.Length) break;
            VimCharacterClass current = CharacterClass(text[offset]);
            while (offset + 1 < text.Length && CharacterClass(text[offset + 1]) == current) offset++;
            if (repetition + 1 < repetitions) offset++;
        }
        return Math.Clamp(offset, 0, Math.Max(0, text.Length - 1));
    }

    private int FirstNonWhitespace(int offset)
    {
        int start = LineStart(offset);
        int end = LineEnd(offset);
        while (start < end && char.IsWhiteSpace(editor.GetCharAt(start))) start++;
        return start;
    }

    private int LineStart(int offset) => editor.GetOffset(new(editor.GetPosition(offset).Line, 0));
    private int LineEnd(int offset) => editor.GetOffset(new(editor.GetPosition(offset).Line, int.MaxValue));

    private void AppendCount(int digit)
    {
        count = Math.Min(MaximumCount, checked(count * 10 + digit));
        Notify();
    }

    private int ConsumeCount(int fallback)
    {
        int value = count == 0 ? fallback : count;
        count = 0;
        return value;
    }

    private string PendingStatus(string mode)
    {
        string operation = pendingOperator switch
        {
            VimOperator.Delete => "d",
            VimOperator.Yank => "y",
            VimOperator.Change => "c",
            _ when pendingGo => "g",
            _ => string.Empty,
        };
        string activeCount = count == 0 ? string.Empty : count.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        string prefix = pendingOperator is VimOperator.None || operatorCount == 1
            ? string.Empty
            : operatorCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string pending = $"{prefix}{operation}{activeCount}";
        return pending.Length == 0
            ? mode
            : $"{mode} · {pending}";
    }

    private void ResetPending()
    {
        pendingOperator = VimOperator.None;
        operatorCount = 1;
        pendingGo = false;
        count = 0;
    }

    private void Notify() => StateChanged?.Invoke(this, EventArgs.Empty);

    private void CopyRegisterToClipboard()
    {
        try
        {
            ValueTask pending = editor.CopyToClipboardAsync(register);
            if (!pending.IsCompletedSuccessfully)
            {
                _ = IgnoreClipboardFailureAsync(pending);
            }
        }
        catch (InvalidOperationException)
        {
            // A clipboard is optional in headless and closing desktop sessions.
        }
        catch (PlatformNotSupportedException)
        {
            // The private Vim register remains available when no platform clipboard exists.
        }
    }

    private static async Task IgnoreClipboardFailureAsync(ValueTask pending)
    {
        try
        {
            await pending;
        }
        catch (InvalidOperationException)
        {
            // The edit already completed against the private Vim register.
        }
        catch (PlatformNotSupportedException)
        {
            // The edit already completed against the private Vim register.
        }
    }

    private static VimCharacterClass CharacterClass(char value) =>
        char.IsWhiteSpace(value)
            ? VimCharacterClass.Whitespace
            : char.IsLetterOrDigit(value) || value == '_'
                ? VimCharacterClass.Word
                : VimCharacterClass.Punctuation;

    private static string DetectLineBreak(string text) =>
        text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static bool EndsWithLineBreak(string text) =>
        text.EndsWith('\n') || text.EndsWith('\r');

    private enum VimOperator
    {
        None,
        Delete,
        Yank,
        Change,
    }

    private enum VimCharacterClass
    {
        Whitespace,
        Word,
        Punctuation,
    }
}
