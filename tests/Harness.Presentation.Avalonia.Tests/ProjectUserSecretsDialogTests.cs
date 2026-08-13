using Avalonia.Automation;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Harness.BusinessLogic.Privacy;
using Harness.BusinessLogic.ProjectSecrets;
using Harness.BusinessLogic.Workspaces;

namespace Harness.Presentation.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class ProjectUserSecretsDialogTests
{
    [Fact]
    public async Task Developer_surface_masks_values_and_exposes_distinct_actions()
    {
        Service service = new();
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            ProjectUserSecretsDialog dialog = new(
                service, new("workspace-a"), CancellationToken.None);
            dialog.Show();
            Dispatcher.UIThread.RunJobs();
            dialog.UpdateLayout();

            Assert.Equal('\0', dialog.SecretValue.PasswordChar);
            Assert.Equal("••••••••", dialog.SecretValue.Text);
            Assert.DoesNotContain("private-value", dialog.SecretValue.Text, StringComparison.Ordinal);
            string?[] names = dialog.GetLogicalDescendants().OfType<Control>()
                .Select(AutomationProperties.GetName).ToArray();
            Assert.Contains("Reveal selected project secret", names);
            Assert.Contains("Copy selected project secret", names);
            Assert.Contains("Add project secret", names);
            Assert.Contains("Change selected project secret", names);
            Assert.Contains("Delete selected project secret", names);
            Point keyOrigin = dialog.SecretKeys.TranslatePoint(new Point(), dialog)!.Value;
            Point addOrigin = dialog.AddButton.TranslatePoint(new Point(), dialog)!.Value;
            Assert.True(dialog.SecretKeys.Bounds.Height >= 100,
                $"Secret key list height was {dialog.SecretKeys.Bounds.Height}.");
            Assert.True(addOrigin.Y >= keyOrigin.Y + dialog.SecretKeys.Bounds.Height,
                $"Add action at {addOrigin.Y} overlapped key list ending at " +
                $"{keyOrigin.Y + dialog.SecretKeys.Bounds.Height}.");

            dialog.RevealButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal('\0', dialog.SecretValue.PasswordChar);
            Assert.Equal("private-value", dialog.SecretValue.Text);
            Assert.False(service.Lease.IsDisposed);

            dialog.Close();
            Assert.True(service.Lease.IsDisposed);
        }, CancellationToken.None);
    }

    private sealed class Service : IProjectUserSecretsService
    {
        internal Lease Lease { get; } = new();

        public ValueTask<ProjectUserSecretsProjectListResult> ListProjectsAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ProjectUserSecretsProjectListResult(
                [Project()], null, null));
        public ValueTask<ProjectUserSecretListResult> ListAsync(WorkspaceId workspaceId, ProjectUserSecretsProjectPath projectPath, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ProjectUserSecretListResult(
                Project(), [new("ApiKey")], null, null));
        public ValueTask<ProjectUserSecretRevealResult> RevealAsync(WorkspaceId workspaceId, ProjectUserSecretsProjectPath projectPath, ProjectUserSecretKey key, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ProjectUserSecretRevealResult(
                ProjectUserSecretValueOutcome.Succeeded,
                new(new("private-value"), Lease), null, null));
        public ValueTask<ProjectUserSecretCopyResult> CopyAsync(WorkspaceId workspaceId, ProjectUserSecretsProjectPath projectPath, ProjectUserSecretKey key, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ProjectUserSecretCopyResult(
                ProjectUserSecretValueOutcome.Succeeded, new("private-value"), null, null));
        public ValueTask<ProjectUserSecretMutationResult> AddAsync(WorkspaceId workspaceId, ProjectUserSecretsProjectPath projectPath, ProjectUserSecretKey key, ProjectUserSecretValue value, CancellationToken cancellationToken = default) => Mutation();
        public ValueTask<ProjectUserSecretMutationResult> ChangeAsync(WorkspaceId workspaceId, ProjectUserSecretsProjectPath projectPath, ProjectUserSecretKey key, ProjectUserSecretValue value, CancellationToken cancellationToken = default) => Mutation();
        public ValueTask<ProjectUserSecretMutationResult> DeleteAsync(WorkspaceId workspaceId, ProjectUserSecretsProjectPath projectPath, ProjectUserSecretKey key, CancellationToken cancellationToken = default) => Mutation();

        private static ProjectUserSecretsProjectView Project() => new(
            new("src/App/App.csproj"), ProjectUserSecretsProjectState.Available, 1, "1 secret");
        private static ValueTask<ProjectUserSecretMutationResult> Mutation() =>
            ValueTask.FromResult(new ProjectUserSecretMutationResult(
                ProjectUserSecretMutationOutcome.Succeeded, Project(), null, null));
    }

    internal sealed class Lease : ISensitiveDisplayLease
    {
        internal bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
    }
}
