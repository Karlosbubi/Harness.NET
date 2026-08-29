using Harness.BusinessLogic.Research;
using Harness.BusinessLogic.Retrieval;

namespace Harness.Presentation.Avalonia;

internal sealed partial class AvaloniaPresentationStore
{
    internal async ValueTask SaveResearchSettingsAsync(
        ResearchSettingsUpdate update,
        CancellationToken cancellationToken)
    {
        if (researchSettingsService is null)
        {
            Publish(Current with
            {
                Settings = Current.Settings with
                { Status = "Documentation and dependency settings are unavailable." }
            });
            return;
        }
        Publish(Current with
        {
            Settings = Current.Settings with
            { IsBusy = true, Status = "Saving documentation and dependency settings…" }
        });
        ResearchSettingsResult result = await researchSettingsService.SaveAsync(update, cancellationToken);
        Publish(Current with
        {
            Settings = Current.Settings with
            {
                ResearchSettings = result.Snapshot ?? Current.Settings.ResearchSettings,
                IsBusy = false,
                Status = result.Error ?? "Documentation and dependency settings saved.",
            }
        });
    }

    internal async ValueTask CleanupResearchCacheAsync(CancellationToken cancellationToken)
    {
        if (researchSettingsService is null)
        {
            return;
        }
        Publish(Current with
        {
            Settings = Current.Settings with
            { IsBusy = true, Status = "Cleaning documentation cache…" }
        });
        ResearchSettingsSnapshot snapshot = await researchSettingsService.CleanupCacheAsync(cancellationToken);
        Publish(Current with
        {
            Settings = Current.Settings with
            {
                ResearchSettings = snapshot,
                IsBusy = false,
                Status = "Documentation cache retention applied.",
            }
        });
    }

    internal async ValueTask LookupDocumentationAsync(
        string library,
        string? version,
        string question,
        CancellationToken cancellationToken)
    {
        if (documentationResearchService is null)
        {
            return;
        }
        Publish(Current with
        {
            Settings = Current.Settings with
            { IsBusy = true, Status = "Looking up documentation on demand…" }
        });
        DocumentationLookupResult result = await documentationResearchService.LookupAsync(new(
            GoalId: null,
            new(library),
            string.IsNullOrWhiteSpace(version) ? null : new(version),
            new(question)), cancellationToken);
        Publish(Current with
        {
            Settings = Current.Settings with
            {
                DocumentationLookup = result,
                IsBusy = false,
                Status = result.Error ?? $"Documentation lookup returned {result.Results.Count} result(s).",
            }
        });
    }

    internal async ValueTask InspectDependenciesAsync(CancellationToken cancellationToken)
    {
        if (dependencyResearchService is null)
        {
            return;
        }
        Publish(Current with
        {
            Settings = Current.Settings with
            { IsBusy = true, Status = "Reading dependency evidence…" }
        });
        DependencyInspectionResult result = await dependencyResearchService.InspectAsync(
            new(GoalId: null), cancellationToken);
        Publish(Current with
        {
            Settings = Current.Settings with
            {
                DependencyInspection = result,
                IsBusy = false,
                Status = result.Error ?? $"Inspected {result.Projects.Count} project(s) without restoring.",
            }
        });
    }

    internal async ValueTask ValidatePackageCandidateAsync(
        string package,
        string version,
        bool allowPrerelease,
        CancellationToken cancellationToken)
    {
        if (dependencyResearchService is null)
        {
            return;
        }
        Publish(Current with
        {
            Settings = Current.Settings with
            { IsBusy = true, Status = "Validating exact package evidence…" }
        });
        PackageCandidateValidationResult result = await dependencyResearchService
            .ValidateCandidateAsync(new(null, new(package), new(version), allowPrerelease),
                cancellationToken);
        Publish(Current with
        {
            Settings = Current.Settings with
            {
                PackageCandidateValidation = result,
                IsBusy = false,
                Status = result.Error ?? $"Candidate decision: {result.Decision}.",
            }
        });
    }

    internal async ValueTask PreviewSbomAsync(CancellationToken cancellationToken)
    {
        if (dependencyResearchService is null)
        {
            return;
        }
        Publish(Current with
        {
            Settings = Current.Settings with
            { IsBusy = true, Status = "Generating deterministic SBOM preview…" }
        });
        SbomPreviewResult result = await dependencyResearchService.PreviewSbomAsync(
            new(GoalId: null), cancellationToken);
        Publish(Current with
        {
            Settings = Current.Settings with
            {
                SbomPreview = result,
                IsBusy = false,
                Status = result.Error ?? $"Generated {result.Sbom!.Format} preview.",
            }
        });
    }

    internal async ValueTask PreviewPackageChangeAsync(
        string package,
        string version,
        bool allowPrerelease,
        CancellationToken cancellationToken)
    {
        if (dependencyResearchService is null)
        {
            return;
        }
        Publish(Current with
        {
            Settings = Current.Settings with
            { IsBusy = true, Status = "Preparing package and SBOM diff…" }
        });
        PackageChangePreviewResult result = await dependencyResearchService.PreviewPackageChangeAsync(
            new(null, new(package), new(version), allowPrerelease), cancellationToken);
        Publish(Current with
        {
            Settings = Current.Settings with
            {
                PackageChangePreview = result,
                IsBusy = false,
                Status = result.Error ?? "Package and SBOM diff ready; no project files were changed.",
            }
        });
    }

    internal async ValueTask ExportSbomAsync(
        string path,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        if (dependencyResearchService is null)
        {
            return;
        }
        Publish(Current with
        {
            Settings = Current.Settings with
            { IsBusy = true, Status = "Exporting explicitly requested SBOM…" }
        });
        SbomExportResult result = await dependencyResearchService.ExportSbomAsync(
            new(null, new(path), overwrite), cancellationToken);
        Publish(Current with
        {
            Settings = Current.Settings with
            {
                SbomExport = result,
                IsBusy = false,
                Status = result.Error ?? $"SBOM exported to {result.Path.Value}.",
            }
        });
    }

}
