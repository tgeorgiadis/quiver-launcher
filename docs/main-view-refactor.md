# Launcher shell and feature ownership

`MainView` composes the launcher, connects host events, coordinates overlays and navigation,
and forwards host-facing operations. Its code behind is approximately 1,750 lines, down from
17,072. The former 2,316-line `MainView.Mods.cs` partial has been removed.

The approximate 1,000-line target was not reached. The remaining code includes constructor
composition, explicit feature/host adapters, shell input precedence, and navigation between
features. Feature workflows have independent owners; there is no replacement monolithic
controller or view model.

## Responsibility map

| Area | State and workflows | Controls and local interaction |
| --- | --- | --- |
| Shell | `ShellViewModel`, `LauncherSession`, `MainViewDependencies` | `MainView`, `ShellAppearance`, `ShellChromeNavigation`, `LauncherBannerView` |
| Library | `LibraryViewModel`, `LibraryActions`, `LibraryPersistenceService`, `LibraryCustomizationService` | `LibraryView`, `LibraryToolbarView`, `LibraryNavigation`, `LibraryLaunchController` |
| Catalog sources | `CatalogViewModel`, `CatalogSourcesService` | `CatalogSourcesView`, `CatalogSourcesNavigation` |
| Catalog review | `CatalogSyncViewModel`, `CatalogReviewWorkspace`, `CatalogReviewService` | `CatalogReviewView`, `CatalogReviewNavigation`, `CatalogDetailsView` |
| App updates | `AppUpdateReviewViewModel`, `UpdateCheckCoordinator`, `LauncherUpdateWorkflow`, `BackgroundUpdateScheduler` | `AppUpdateReviewView` |
| Mods | `ModsViewModel`, `ModsCatalogWorkspace`, `ModsActions`, `ModDetailsViewModel` | `ModsView`, `ModsNavigation`, `ModDetailsView` |
| Settings | `SettingsViewModel`, `InputBindingsViewModel`, existing `ISettingsStore` | `SettingsView`, `SettingsNavigationController`, `ThemeEditor` |
| Editing | `AppEntryEditorViewModel`, `MetadataEditorViewModel`, `DisplayFilterEditorViewModel`, corresponding persistence services | `AppEntryEditorView`, `MetadataEditorView`, `DisplayFilterEditorView`, `FeatureFormNavigation` |
| Library filters | `LibraryFiltersViewModel` | `LibraryFiltersView` |
| Documents | `DocumentViewModel`, `LibraryDocumentService`, `RepositoryReadmeService`, `MarkdownRenderer` | `DocumentView` |
| Prompts | `MessagePromptViewModel`, `LauncherPromptService`, `LauncherDialogLifetime` | `MessagePromptView` and existing native dialogs |
| Platform | `LauncherMusicService`, `LauncherForegroundController`, `DesktopHostController`, `LauncherInputController` | `MobileShellLayout`, `MobileInsetsController` |

Shared resources are in `Themes/LauncherStyles.axaml` and
`Themes/LauncherFeatureResources.axaml`. Extracted views use typed bindings. A feature may
find its own controls; the shell communicates through feature APIs instead of looking up
controls inside child views. Bindings on feature instances that reference shell state
explicitly use `#ShellRoot`, because those instances own their data contexts.

## Lifecycle and asynchronous work

The parameterless XAML constructor delegates to a construction path accepting
`MainViewDependencies`. Injected services remain caller-owned. Tests can disable startup,
input, and music while constructing the real visual tree.

`LauncherSession` admits initialization once, tracks asynchronous work, cancels on shutdown,
drains operations, and then releases owned dependencies. Shutdown is idempotent and tolerates
reentrant cancellation callbacks. Pending prompts settle before their services are disposed.
Service dialogs inherit the session's operation cancellation token. Callback leases only
unregister callbacks they still own and never restore a closed predecessor.

Document, catalog, and mods loads check cancellation and request identity before publishing.
Debounced searches and delayed focus work also reject closed or replaced owners. A mods
update batch retains the game and opening version it started with.

Visual detachment releases temporary layout subscriptions without disposing the session.
Desktop closure and Android view replacement/destruction perform final shutdown. Android's
directory callback captures the directory string rather than retaining the activity.

## Navigation and update policy

`IFeatureNavigationHandler` exposes directional navigation, confirm, cancel, options,
focus restoration, pointer synchronization, and zone transitions. `ShellNavigationRouter`
selects feature handlers; `LauncherInputController` retains input polling and routes keyboard
and gamepad events. Existing context-menu, text-edit, modal, and cross-zone helpers remain.

`UpdateCheckCoordinator` guards concurrent checks. `LauncherUpdateWorkflow` owns startup
prompt ordering and automatic-update policy, using the existing launcher, catalog, and
app-update services. Settings continue through the existing store with unchanged formats
and defaults. No framework, package-version, installation-engine, or persistence migration
was introduced.

## Validation

The full Release suite passed: **1,307 passed, zero failed or skipped**, including the slow
packaging test. Added coverage includes renderer structures, lifetime ownership and draining,
late completions, update concurrency and ordering, feature bindings/actions, nested overlays,
navigation/focus, and search after mobile control reparenting.

Reproduce the main checks with:

```powershell
dotnet test QuiverLauncher.Tests/QuiverLauncher.Tests.csproj -c Release --no-restore
dotnet build QuiverLauncher.Desktop/QuiverLauncher.Desktop.csproj -c Release --no-restore
dotnet build QuiverLauncher.Android/QuiverLauncher.Android.csproj --no-restore
```

The desktop Release and Android builds passed. Linux and macOS conditional compilation was checked by
building the shared application with `DefineConstants=LINUX` and `DefineConstants=OSX` into
separate output folders. These are compilation checks on Windows, not native platform runs.

Skia headless previews were inspected for library, settings, catalog sources, and app-update
review. This caught and led to a regression test for editor visibility after child data
contexts change. Preview generation and validation logs are local ignored artifacts.

Physical mouse/keyboard/gamepad workflows, tray behavior, music playback, Linux/macOS native
execution, and Android rotation/keyboard insets still require runtime checks on those hosts.
Headless focus/layout tests and successful compilation do not establish those results.
