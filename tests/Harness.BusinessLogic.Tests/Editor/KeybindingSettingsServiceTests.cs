using Harness.BusinessLogic.Editor;
using Harness.DataAccess.Editor;

namespace Harness.BusinessLogic.Tests.Editor;

public sealed class KeybindingSettingsServiceTests
{
    [Fact]
    public async Task Defaults_are_complete_conflict_free_and_include_alternate_navigation_keys()
    {
        MemoryStore store = new(new(true, []));
        KeybindingSettingsService service = new(store);

        KeybindingSettingsSnapshot snapshot = await service.GetAsync();

        Assert.True(snapshot.UsesDefaults);
        Assert.Empty(snapshot.Issues);
        Assert.Equal(Enum.GetValues<KeybindingCommand>().Length, snapshot.Bindings.Count);
        Assert.Equal("Shift+F12; Alt+F7", snapshot.DisplayFor(KeybindingCommand.FindReferences));
        Assert.Equal("Ctrl+,", snapshot.DisplayFor(KeybindingCommand.OpenSettings));
    }

    [Fact]
    public async Task Saves_normalized_custom_bindings_and_round_trips_safe_json()
    {
        MemoryStore store = new(new(true, []));
        KeybindingSettingsService service = new(store);
        KeybindingUpdateRequest update = Request((KeybindingCommand.ShowChat, "alt + c"));

        KeybindingSettingsSnapshot saved = await service.SaveAsync(update);
        string exported = await service.ExportAsync();
        await service.ResetAsync();
        KeybindingSettingsSnapshot imported = await service.ImportAsync(exported);

        Assert.False(saved.UsesDefaults);
        Assert.Equal("Alt+C", saved.DisplayFor(KeybindingCommand.ShowChat));
        Assert.Contains("\"format\": \"harness-keybindings-v1\"", exported,
            StringComparison.Ordinal);
        Assert.Equal("Alt+C", imported.DisplayFor(KeybindingCommand.ShowChat));
        Assert.False(store.Current.UseDefaults);
    }

    [Fact]
    public async Task Vim_mode_persists_while_keybinding_reset_and_import_only_replace_bindings()
    {
        MemoryStore store = new(new(true, []));
        KeybindingSettingsService service = new(store);
        KeybindingUpdateRequest vim = Request() with { InputMode = EditorInputMode.Vim };

        KeybindingSettingsSnapshot saved = await service.SaveAsync(vim);
        string exported = await service.ExportAsync();
        KeybindingSettingsSnapshot reset = await service.ResetAsync();
        KeybindingSettingsSnapshot imported = await service.ImportAsync(exported);

        Assert.Equal(EditorInputMode.Vim, saved.InputMode);
        Assert.Equal(EditorInputMode.Vim, reset.InputMode);
        Assert.True(reset.UsesDefaults);
        Assert.Equal(EditorInputMode.Vim, imported.InputMode);
    }

    [Fact]
    public void Conflicts_reserved_desktop_keys_and_missing_commands_block_save()
    {
        KeybindingSettingsService service = new(new MemoryStore(new(true, [])));
        KeybindingUpdateRequest conflict = Request((KeybindingCommand.ShowChat, "Ctrl+P"));
        KeybindingUpdateRequest reserved = Request((KeybindingCommand.ShowChat, "Alt+F4"));
        KeybindingUpdateRequest incomplete = new(
            [new(KeybindingCommand.ShowChat, "Ctrl+Shift+C")]);
        KeybindingUpdateRequest tooMany = Request((KeybindingCommand.ShowChat,
            "Alt+A; Alt+B; Alt+C; Alt+D; Alt+E; Alt+F; Alt+G; Alt+H; Alt+I"));

        Assert.Contains(service.Validate(conflict).Issues,
            issue => issue.Kind is KeybindingIssueKind.Conflict);
        Assert.Contains(service.Validate(reserved).Issues,
            issue => issue.Kind is KeybindingIssueKind.ReservedShortcut);
        Assert.Contains(service.Validate(incomplete).Issues,
            issue => issue.Kind is KeybindingIssueKind.MissingCommand);
        Assert.Contains(service.Validate(tooMany).Issues,
            issue => issue.Kind is KeybindingIssueKind.InvalidDocument);
    }

    [Theory]
    [InlineData("{\"format\":\"harness-keybindings-v1\",\"bindings\":[],\"script\":\"x\"}")]
    [InlineData("{\"format\":\"other\",\"bindings\":[]}")]
    [InlineData("{\"format\":\"harness-keybindings-v1\",\"bindings\":[{\"command\":\"Shell\",\"gestures\":[]}]}")]
    [InlineData("{\"format\":\"harness-keybindings-v1\",\"bindings\":[{\"command\":1,\"gestures\":[]}]}")]
    public async Task Import_rejects_unknown_schema_fields_formats_and_commands(string document)
    {
        KeybindingSettingsService service = new(new MemoryStore(new(true, [])));

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await service.ImportAsync(document));
    }

    [Fact]
    public async Task Corrupt_stored_configuration_falls_back_to_safe_defaults_with_status()
    {
        MemoryStore store = new(new(false,
            [new(new("RemovedCommand"), 0, new("Ctrl+Q"))]));
        KeybindingSettingsService service = new(store);

        KeybindingSettingsSnapshot snapshot = await service.GetAsync();

        Assert.True(snapshot.UsesDefaults);
        Assert.NotEmpty(snapshot.Issues);
        Assert.Contains("rejected", snapshot.Status, StringComparison.OrdinalIgnoreCase);
    }

    private static KeybindingUpdateRequest Request(
        params (KeybindingCommand Command, string Gesture)[] replacements)
    {
        KeybindingSettingsSnapshot defaults = KeybindingSettingsSnapshot.Default;
        return new(defaults.Bindings.Select(binding => new KeybindingUpdateEntry(
            binding.Definition.Command,
            replacements.FirstOrDefault(item => item.Command == binding.Definition.Command) is
            { Gesture.Length: > 0 } replacement
                    ? replacement.Gesture
                    : binding.DisplayText)).ToArray());
    }

    private sealed class MemoryStore(StoredKeybindingPreferences current) : IKeybindingPreferenceStore
    {
        internal StoredKeybindingPreferences Current { get; private set; } = current;

        public ValueTask<StoredKeybindingPreferences> GetAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Current);

        public ValueTask<StoredKeybindingPreferences> SaveAsync(
            StoredKeybindingPreferences preferences,
            CancellationToken cancellationToken = default)
        {
            Current = preferences;
            return ValueTask.FromResult(Current);
        }

        public ValueTask<StoredKeybindingPreferences> ResetAsync(
            CancellationToken cancellationToken = default)
        {
            Current = new(true, [], Current.InputMode);
            return ValueTask.FromResult(Current);
        }
    }
}
