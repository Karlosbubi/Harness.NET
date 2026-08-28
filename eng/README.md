# Verification and capture catalog

Run scripts from the repository root. Deterministic scripts never authorize paid or
live model use. Graphical scripts require Linux, a desktop session, and the listed
AT-SPI/portal tools. Durations are typical developer-machine ranges, not budgets.

| Entry point | Purpose | Prerequisites | Used by | Typical duration |
|---|---|---|---|---|
| `verify-v1-release.sh` | Deterministic tests, local-model fixtures, and Linux publish gate. | .NET 10, Python 3, Linux publish prerequisites. | Repository release gate. | 3–8 min |
| `verify-v1-desktop-release.sh` | Complete deterministic, AT-SPI/Orca, workflow, and publish gate. | Graphical Linux, accessibility bus, Orca. | Desktop release acceptance. | 5–12 min |
| `verify-linux-x64-publish.sh` | Self-contained publish, lifecycle, persistence, backup, and recovery smoke checks. | Linux x64, `sqlite3`. | Release and backup acceptance. | 1–4 min |
| `verify-avalonia-atspi.py` | Production workbench accessibility, layout, focus, and optional Orca checks. | Graphical Linux, `python3-dbus`, AT-SPI; Orca for `--with-orca`. | Workbench and refactor slices. | 1–3 min |
| `verify-avalonia-workflow.py` | Deterministic end-to-end goal workflow through the production UI and typed tools. | Graphical Linux, AT-SPI, .NET 10. | Chat/goal workflow acceptance. | 1–3 min |
| `verify-editor-intelligence.py` | Focused Roslyn/editor tests with optional desktop and complete-Linux gates. | .NET 10; graphical dependencies for optional flags. | Editor intelligence acceptance. | 1–8 min |
| `verify-local-model-regression.py` | Deterministic regression corpus; `--live` explicitly enables local Ollama comparisons, repeatable `--reasoning-off-role` flags exercise persisted role opt-outs, and failed/cancelled Harness operations stop polling immediately with their durable error. | Python 3; Ollama only for explicit live mode. | Task 038/069/070 regression records. | <1 min fixtures; model-dependent live |
| `test_local_model_regression.py` | Unit tests for regression schemas, validators, comparison, and transport fakes. | Python 3. | `verify-v1-release.sh`. | <10 s |
| `verify-ollama-tictactoe-usability.py` | Non-trivial isolated local-model usability run and independent validator. | Explicit local Ollama model, .NET 10. | Tic-Tac-Toe usability record. | 10–40 min |
| `capture-diff-viewer.py` | Capture command palette and inline/side-by-side diff surfaces. | Graphical Linux, `python3-dbus`, screenshot tool. | Diff/workbench design evidence. | 1–3 min |
| `capture-settings.py` | Capture real searchable Settings at repeatable sizes. | Graphical Linux, `python3-dbus`, `wmctrl`, screenshot tool. | Settings acceptance evidence. | 1–3 min |
| `capture-source-editor.py` | Capture production source editor against an isolated real Git repository. | Graphical Linux, `python3-dbus`, `wmctrl`, screenshot tool. | Source-editor evidence. | 1–3 min |
| `capture-project-user-secrets.py` | Capture the secrets dialog without reading a secret. | Graphical Linux, `python3-dbus`, screenshot tool. | Project User Secrets evidence. | 1–3 min |
| `verify-repository-metadata.py` | Check local docs links, ADR index statuses, acceptance labels, notice versions, and preview exit records. | Python 3 standard library. | Every pull request. | <5 s |

`local_model_regression.py` is the shared library for the regression entry points;
`local-model-regression/scenarios/v1/` is the versioned fixture/live scenario corpus.
Capture tools may write directly to `docs/acceptance/` only when the output has been
reviewed under the [evidence policy](../docs/acceptance/README.md). Otherwise use the
ignored `artifacts/` tree or a temporary directory.
