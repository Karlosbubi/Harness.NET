using System.Text.Json;
using Harness.DataAccess.Editor;

namespace Harness.BusinessLogic.Editor;

public enum EditorInputMode
{
    Standard,
    Vim,
}

public enum KeybindingCommand
{
    ShowCommandPalette,
    QuickOpen,
    OpenWorkspace,
    ManageWorkspaces,
    ManageProjectUserSecrets,
    OpenSettings,
    InspectSemanticContext,
    ManageFramework,
    ManageOperations,
    RefreshProviderHealth,
    ReloadUserThemes,
    ShowChat,
    ShowFiles,
    ShowGit,
    OpenWorkingTreeDiff,
    ShowRunOutput,
    ShowProblems,
    SaveWorkbenchLayout,
    ResetWorkbenchLayout,
    FocusNextRegion,
    SaveDocument,
    CloseDocument,
    ShowCompletion,
    ShowQuickInfo,
    GoToDefinition,
    FindReferences,
    FindImplementations,
    RenameSymbol,
    FormatDocument,
    FormatSelection,
    FormatChangedCode,
    OrganizeImports,
    RemoveUnusedImports,
    ShowQuickFixes,
    RefreshFiles,
    SearchWorkspace,
    RefreshSolution,
    BuildStartupProject,
    RebuildStartupProject,
    RefreshTestExplorer,
    RefreshGit,
    StageGitChange,
    UnstageGitChange,
    SelectWholeGitFile,
    DiscardGitChange,
    DeleteUntrackedGitFile,
    CommitGitChange,
    RefreshGitBranches,
    CreateGitBranch,
    SwitchGitBranch,
    RenameGitBranch,
    DeleteGitBranch,
    RefreshGitTags,
    CreateGitTag,
    DeleteGitTag,
    RefreshGitWorktrees,
    CreateGitWorktree,
    OpenGitWorktree,
    RemoveGitWorktree,
    RefreshGitStashes,
    CreateGitStash,
    ApplyGitStash,
    DeleteGitStash,
    RefreshGitRemotes,
    FetchGitRemote,
    IntegrateGitRemote,
    PushGitRemote,
    RefreshGitHistory,
    LoadMoreGitHistory,
    ShowGitBlame,
    RefreshGitConflicts,
    SaveGitConflict,
    StageGitConflict,
    UseGitConflictBase,
    UseGitConflictOurs,
    UseGitConflictTheirs,
    RefreshRunOutput,
    StopSelectedRun,
    ToggleProblemWarnings,
    ToggleProblemInformation,
    ToggleProblemHidden,
    OpenGoalPlan,
    OpenGoalEvidence,
}

[Flags]
public enum KeybindingModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Meta = 8,
}

public enum KeybindingKey
{
    A, B, C, D, E, F, G, H, I, J, K, L, M,
    N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    Space, Enter, Tab, Escape, Backspace, Delete, Insert,
    Home, End, PageUp, PageDown, Up, Down, Left, Right,
    Comma, Period, Slash, Backslash, Semicolon, Quote,
    LeftBracket, RightBracket, Minus, Equal,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    F13, F14, F15, F16, F17, F18, F19, F20, F21, F22, F23, F24,
}

public sealed record KeybindingGesture(
    KeybindingModifiers Modifiers,
    KeybindingKey Key)
{
    public override string ToString() => KeybindingGestureParser.Format(this);

    public string ToDisplayString() => KeybindingGestureParser.Display(this);
}

public sealed record KeybindingCommandDefinition(
    KeybindingCommand Command,
    string Category,
    string Title);

public sealed record KeybindingCommandBindings(
    KeybindingCommandDefinition Definition,
    IReadOnlyList<KeybindingGesture> Gestures)
{
    public string DisplayText => string.Join("; ", Gestures.Select(item => item.ToDisplayString()));
}

public sealed record KeybindingUpdateEntry(
    KeybindingCommand Command,
    string GestureText);

public sealed record KeybindingUpdateRequest(
    IReadOnlyList<KeybindingUpdateEntry> Entries,
    EditorInputMode InputMode = EditorInputMode.Standard);

public enum KeybindingIssueKind
{
    InvalidGesture,
    DuplicateCommand,
    DuplicateGesture,
    Conflict,
    ReservedShortcut,
    MissingCommand,
    UnknownCommand,
    InvalidDocument,
}

public sealed record KeybindingIssue(
    KeybindingIssueKind Kind,
    KeybindingCommand? Command,
    string Message);

public sealed record KeybindingValidationResult(
    bool IsValid,
    IReadOnlyList<KeybindingIssue> Issues,
    IReadOnlyList<KeybindingCommandBindings> Bindings);

public sealed record KeybindingSettingsSnapshot(
    IReadOnlyList<KeybindingCommandBindings> Bindings,
    IReadOnlyList<KeybindingIssue> Issues,
    bool UsesDefaults,
    EditorInputMode InputMode,
    string Status)
{
    public static KeybindingSettingsSnapshot Default { get; } = new(
        KeybindingCatalog.DefaultBindings, [], true, EditorInputMode.Standard,
        "Default keybindings are active. Shortcuts are validated before dispatch.");

    public IReadOnlyList<KeybindingGesture> GesturesFor(KeybindingCommand command) =>
        Bindings.FirstOrDefault(item => item.Definition.Command == command)?.Gestures ?? [];

    public string DisplayFor(KeybindingCommand command) =>
        Bindings.FirstOrDefault(item => item.Definition.Command == command)?.DisplayText ?? string.Empty;
}

public interface IKeybindingSettingsService
{
    ValueTask<KeybindingSettingsSnapshot> GetAsync(
        CancellationToken cancellationToken = default);

    KeybindingValidationResult Validate(KeybindingUpdateRequest request);

    ValueTask<KeybindingSettingsSnapshot> SaveAsync(
        KeybindingUpdateRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<KeybindingSettingsSnapshot> ResetAsync(
        CancellationToken cancellationToken = default);

    ValueTask<string> ExportAsync(CancellationToken cancellationToken = default);

    ValueTask<KeybindingSettingsSnapshot> ImportAsync(
        string document,
        CancellationToken cancellationToken = default);
}

internal sealed class KeybindingSettingsService(
    IKeybindingPreferenceStore store) : IKeybindingSettingsService
{
    private const int MaximumImportCharacters = 65_536;
    private const string FormatName = "harness-keybindings-v1";

    public async ValueTask<KeybindingSettingsSnapshot> GetAsync(
        CancellationToken cancellationToken = default)
    {
        StoredKeybindingPreferences stored = await store.GetAsync(cancellationToken);
        if (stored.UseDefaults)
        {
            return Snapshot(KeybindingCatalog.DefaultBindings, true, Decode(stored.InputMode),
                "Default keybindings are active. Shortcuts are validated before dispatch.");
        }

        KeybindingValidationResult decoded = Decode(stored.Bindings);
        return decoded.IsValid
            ? Snapshot(decoded.Bindings, false, Decode(stored.InputMode),
                "Custom keybindings are active. Shortcuts are validated before dispatch.")
            : Snapshot(KeybindingCatalog.DefaultBindings, true, EditorInputMode.Standard,
                "Stored keybindings were rejected; safe defaults are active.", decoded.Issues);
    }

    public KeybindingValidationResult Validate(KeybindingUpdateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        List<KeybindingIssue> issues = [];
        if (!Enum.IsDefined(request.InputMode))
        {
            issues.Add(new(KeybindingIssueKind.InvalidDocument, null,
                "The editor input mode is not supported."));
        }
        Dictionary<KeybindingCommand, IReadOnlyList<KeybindingGesture>> parsed = [];
        if (request.Entries.Count > 96)
        {
            issues.Add(new(KeybindingIssueKind.InvalidDocument, null,
                "A keybinding configuration may contain at most 96 command entries."));
        }
        foreach (IGrouping<KeybindingCommand, KeybindingUpdateEntry> group in
                 request.Entries.GroupBy(entry => entry.Command))
        {
            if (!Enum.IsDefined(group.Key))
            {
                issues.Add(new(KeybindingIssueKind.UnknownCommand, null,
                    $"Command value '{group.Key}' is not supported."));
                continue;
            }
            if (group.Count() != 1)
            {
                issues.Add(new(KeybindingIssueKind.DuplicateCommand, group.Key,
                    $"{KeybindingCatalog.Definition(group.Key).Title} appears more than once."));
                continue;
            }

            List<KeybindingGesture> gestures = [];
            string[] gestureTexts = Split(group.Single().GestureText).ToArray();
            if (gestureTexts.Length > 8)
            {
                issues.Add(new(KeybindingIssueKind.InvalidDocument, group.Key,
                    $"{KeybindingCatalog.Definition(group.Key).Title} may have at most 8 gestures."));
            }
            foreach (string text in gestureTexts.Take(8))
            {
                if (!KeybindingGestureParser.TryParse(text, out KeybindingGesture? gesture,
                        out string? error) || gesture is null)
                {
                    issues.Add(new(KeybindingIssueKind.InvalidGesture, group.Key,
                        $"{KeybindingCatalog.Definition(group.Key).Title}: {error}"));
                    continue;
                }

                if (Reserved(gesture, out string? reason))
                {
                    issues.Add(new(KeybindingIssueKind.ReservedShortcut, group.Key,
                        $"{gesture} is reserved: {reason}"));
                    continue;
                }

                if (gestures.Contains(gesture))
                {
                    issues.Add(new(KeybindingIssueKind.DuplicateGesture, group.Key,
                        $"{gesture} is repeated for {KeybindingCatalog.Definition(group.Key).Title}."));
                    continue;
                }

                gestures.Add(gesture);
            }
            parsed[group.Key] = gestures;
        }

        foreach (KeybindingCommandDefinition definition in KeybindingCatalog.Definitions)
        {
            if (!parsed.ContainsKey(definition.Command))
            {
                issues.Add(new(KeybindingIssueKind.MissingCommand, definition.Command,
                    $"The document has no entry for {definition.Title}."));
                parsed[definition.Command] = [];
            }
        }

        foreach (IGrouping<KeybindingGesture, (KeybindingCommand Command, KeybindingGesture Gesture)> conflict in
                 parsed.SelectMany(pair => pair.Value.Select(gesture => (pair.Key, gesture)))
                     .GroupBy(item => item.gesture)
                     .Where(group => group.Count() > 1))
        {
            string commands = string.Join(", ", conflict.Select(item =>
                KeybindingCatalog.Definition(item.Command).Title));
            foreach ((KeybindingCommand command, _) in conflict)
            {
                issues.Add(new(KeybindingIssueKind.Conflict, command,
                    $"{conflict.Key} conflicts between {commands}."));
            }
        }

        IReadOnlyList<KeybindingCommandBindings> bindings = KeybindingCatalog.Definitions
            .Select(definition => new KeybindingCommandBindings(
                definition, parsed.GetValueOrDefault(definition.Command) ?? []))
            .ToArray();
        return new(issues.Count == 0, issues, bindings);
    }

    public async ValueTask<KeybindingSettingsSnapshot> SaveAsync(
        KeybindingUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        KeybindingValidationResult validation = Validate(request);
        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join(' ', validation.Issues.Select(issue => issue.Message)),
                nameof(request));
        }

        StoredKeybindingPreferences saved = await store.SaveAsync(new(false,
            Encode(validation.Bindings), Encode(request.InputMode)), cancellationToken);
        KeybindingValidationResult decoded = Decode(saved.Bindings);
        if (!decoded.IsValid)
        {
            throw new InvalidDataException("Saved keybindings did not round-trip exactly.");
        }
        return Snapshot(decoded.Bindings, false, Decode(saved.InputMode),
            "Custom keybindings saved and active.");
    }

    public async ValueTask<KeybindingSettingsSnapshot> ResetAsync(
        CancellationToken cancellationToken = default)
    {
        StoredKeybindingPreferences reset = await store.ResetAsync(cancellationToken);
        return Snapshot(KeybindingCatalog.DefaultBindings, true, Decode(reset.InputMode),
            "Default keybindings restored and active.");
    }

    public async ValueTask<string> ExportAsync(CancellationToken cancellationToken = default)
    {
        KeybindingSettingsSnapshot snapshot = await GetAsync(cancellationToken);
        return JsonSerializer.Serialize(new ExportDocument(
            FormatName,
            snapshot.Bindings.Select(binding => new ExportBinding(
                binding.Definition.Command.ToString(),
                binding.Gestures.Select(gesture => gesture.ToString()).ToArray())).ToArray()),
            new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
    }

    public async ValueTask<KeybindingSettingsSnapshot> ImportAsync(
        string document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Length is < 1 or > MaximumImportCharacters)
        {
            throw new InvalidDataException("The keybinding document must contain 1–65,536 characters.");
        }

        KeybindingUpdateRequest request = ParseImport(document) with
        {
            InputMode = (await GetAsync(cancellationToken)).InputMode,
        };
        return await SaveAsync(request, cancellationToken);
    }

    private KeybindingUpdateRequest ParseImport(string document)
    {
        try
        {
            using JsonDocument parsed = JsonDocument.Parse(document, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8,
            });
            JsonElement root = parsed.RootElement;
            RequireObject(root, "root", ["format", "bindings"]);
            if (root.GetProperty("format").ValueKind is not JsonValueKind.String ||
                root.GetProperty("format").GetString() != FormatName)
            {
                throw new InvalidDataException($"The format must be '{FormatName}'.");
            }

            JsonElement bindings = root.GetProperty("bindings");
            if (bindings.ValueKind is not JsonValueKind.Array || bindings.GetArrayLength() > 96)
            {
                throw new InvalidDataException("Bindings must be an array with at most 96 entries.");
            }

            List<KeybindingUpdateEntry> entries = [];
            foreach (JsonElement binding in bindings.EnumerateArray())
            {
                RequireObject(binding, "binding", ["command", "gestures"]);
                JsonElement commandElement = binding.GetProperty("command");
                if (commandElement.ValueKind is not JsonValueKind.String)
                {
                    throw new InvalidDataException("Every command must be a string.");
                }
                string? commandText = commandElement.GetString();
                if (!Enum.TryParse(commandText, ignoreCase: false, out KeybindingCommand command) ||
                    !Enum.IsDefined(command))
                {
                    throw new InvalidDataException($"Unknown keybinding command '{commandText}'.");
                }
                JsonElement gestures = binding.GetProperty("gestures");
                if (gestures.ValueKind is not JsonValueKind.Array || gestures.GetArrayLength() > 8)
                {
                    throw new InvalidDataException($"{command} must contain at most 8 gestures.");
                }
                string[] values = gestures.EnumerateArray().Select(item =>
                    item.ValueKind is JsonValueKind.String && item.GetString() is { } value
                        ? value
                        : throw new InvalidDataException("Every gesture must be a string.")).ToArray();
                entries.Add(new(command, string.Join("; ", values)));
            }
            return new(entries);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The keybinding document is not valid JSON.", exception);
        }
    }

    private KeybindingValidationResult Decode(IReadOnlyList<StoredKeybinding> stored)
    {
        List<KeybindingUpdateEntry> entries = [];
        foreach (IGrouping<string, StoredKeybinding> group in stored.GroupBy(
                     binding => binding.Command.Value, StringComparer.Ordinal))
        {
            if (!Enum.TryParse(group.Key, ignoreCase: false, out KeybindingCommand command) ||
                !Enum.IsDefined(command))
            {
                return new(false,
                    [new(KeybindingIssueKind.UnknownCommand, null,
                        $"Stored command '{group.Key}' is not supported.")], []);
            }
            entries.Add(new(command, string.Join("; ", group.OrderBy(item => item.Position)
                .Select(item => item.Gesture.Value))));
        }

        foreach (KeybindingCommandDefinition definition in KeybindingCatalog.Definitions
                     .Where(definition => entries.All(entry => entry.Command != definition.Command)))
        {
            entries.Add(new(definition.Command, string.Empty));
        }
        return Validate(new(entries));
    }

    private static IReadOnlyList<StoredKeybinding> Encode(
        IReadOnlyList<KeybindingCommandBindings> bindings) => bindings.SelectMany(binding =>
            binding.Gestures.Select((gesture, position) => new StoredKeybinding(
                new(binding.Definition.Command.ToString()), position, new(gesture.ToString())))).ToArray();

    private static KeybindingSettingsSnapshot Snapshot(
        IReadOnlyList<KeybindingCommandBindings> bindings,
        bool defaults,
        EditorInputMode inputMode,
        string status,
        IReadOnlyList<KeybindingIssue>? issues = null) =>
        new(bindings, issues ?? [], defaults, inputMode,
            $"{status} {(inputMode is EditorInputMode.Vim ? "Vim" : "Standard")} editor input is active.");

    private static EditorInputMode Decode(StoredEditorInputMode mode) => mode switch
    {
        StoredEditorInputMode.Standard => EditorInputMode.Standard,
        StoredEditorInputMode.Vim => EditorInputMode.Vim,
        _ => throw new InvalidDataException("Stored editor input mode is not supported."),
    };

    private static StoredEditorInputMode Encode(EditorInputMode mode) => mode switch
    {
        EditorInputMode.Standard => StoredEditorInputMode.Standard,
        EditorInputMode.Vim => StoredEditorInputMode.Vim,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private static IEnumerable<string> Split(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static bool Reserved(KeybindingGesture gesture, out string? reason)
    {
        reason = gesture switch
        {
            { Modifiers: KeybindingModifiers.None, Key: not (>= KeybindingKey.F1 and <= KeybindingKey.F24) } =>
                "unmodified typing, navigation, Escape, and accessibility keys remain owned by the focused control",
            { Modifiers: KeybindingModifiers.Alt, Key: KeybindingKey.F4 } =>
                "the desktop window-close shortcut remains available",
            {
                Modifiers: KeybindingModifiers.Control | KeybindingModifiers.Alt,
                Key: >= KeybindingKey.F1 and <= KeybindingKey.F12
            } =>
                "Linux virtual-terminal switching remains available",
            {
                Modifiers: KeybindingModifiers.Control | KeybindingModifiers.Alt,
                Key: KeybindingKey.Delete or KeybindingKey.Backspace
            } =>
                "the operating-system session shortcut remains available",
            { Modifiers: KeybindingModifiers.Meta, Key: KeybindingKey.L } =>
                "the desktop lock shortcut remains available",
            _ => null,
        };
        return reason is not null;
    }

    private static void RequireObject(JsonElement element, string name, string[] properties)
    {
        if (element.ValueKind is not JsonValueKind.Object)
        {
            throw new InvalidDataException($"The {name} must be an object.");
        }
        string[] actual = element.EnumerateObject().Select(property => property.Name).ToArray();
        if (actual.Length != properties.Length || properties.Any(property =>
                actual.Count(item => item == property) != 1))
        {
            throw new InvalidDataException(
                $"The {name} must contain exactly: {string.Join(", ", properties)}.");
        }
    }

    private sealed record ExportDocument(string Format, IReadOnlyList<ExportBinding> Bindings);
    private sealed record ExportBinding(string Command, IReadOnlyList<string> Gestures);
}

internal static class KeybindingGestureParser
{
    internal static bool TryParse(
        string text,
        out KeybindingGesture? gesture,
        out string? error)
    {
        gesture = null;
        error = null;
        string[] parts = text.Split('+', StringSplitOptions.TrimEntries |
                                         StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 5)
        {
            error = $"'{text}' is not a key gesture.";
            return false;
        }

        KeybindingModifiers modifiers = KeybindingModifiers.None;
        foreach (string modifier in parts[..^1])
        {
            KeybindingModifiers next = modifier.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => KeybindingModifiers.Control,
                "ALT" => KeybindingModifiers.Alt,
                "SHIFT" => KeybindingModifiers.Shift,
                "META" or "SUPER" or "CMD" => KeybindingModifiers.Meta,
                _ => KeybindingModifiers.None,
            };
            if (next is KeybindingModifiers.None || modifiers.HasFlag(next))
            {
                error = $"'{modifier}' is not a unique supported modifier.";
                return false;
            }
            modifiers |= next;
        }

        string keyText = NormalizeKey(parts[^1]);
        if (!Enum.TryParse(keyText, ignoreCase: true, out KeybindingKey key) ||
            !Enum.IsDefined(key))
        {
            error = $"'{parts[^1]}' is not a supported key.";
            return false;
        }
        gesture = new(modifiers, key);
        return true;
    }

    internal static string Format(KeybindingGesture gesture)
    {
        List<string> parts = [];
        if (gesture.Modifiers.HasFlag(KeybindingModifiers.Control)) parts.Add("Ctrl");
        if (gesture.Modifiers.HasFlag(KeybindingModifiers.Alt)) parts.Add("Alt");
        if (gesture.Modifiers.HasFlag(KeybindingModifiers.Shift)) parts.Add("Shift");
        if (gesture.Modifiers.HasFlag(KeybindingModifiers.Meta)) parts.Add("Meta");
        parts.Add(gesture.Key.ToString());
        return string.Join('+', parts);
    }

    internal static string Display(KeybindingGesture gesture)
    {
        string normalized = Format(gesture);
        string key = gesture.Key switch
        {
            >= KeybindingKey.D0 and <= KeybindingKey.D9 =>
                ((int)gesture.Key - (int)KeybindingKey.D0).ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
            KeybindingKey.Comma => ",",
            KeybindingKey.Period => ".",
            KeybindingKey.Slash => "/",
            KeybindingKey.Backslash => "\\",
            KeybindingKey.Quote => "'",
            KeybindingKey.LeftBracket => "[",
            KeybindingKey.RightBracket => "]",
            KeybindingKey.Minus => "-",
            KeybindingKey.Equal => "=",
            _ => gesture.Key.ToString(),
        };
        int separator = normalized.LastIndexOf('+');
        return separator < 0 ? key : normalized[..(separator + 1)] + key;
    }

    private static string NormalizeKey(string value) => value.Trim() switch
    {
        "," => nameof(KeybindingKey.Comma),
        "." => nameof(KeybindingKey.Period),
        "/" => nameof(KeybindingKey.Slash),
        "\\" => nameof(KeybindingKey.Backslash),
        ";" => nameof(KeybindingKey.Semicolon),
        "'" => nameof(KeybindingKey.Quote),
        "[" => nameof(KeybindingKey.LeftBracket),
        "]" => nameof(KeybindingKey.RightBracket),
        "-" => nameof(KeybindingKey.Minus),
        "=" => nameof(KeybindingKey.Equal),
        { Length: 1 } digit when char.IsDigit(digit[0]) => $"D{digit}",
        var key => key,
    };
}
