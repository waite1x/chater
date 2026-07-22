# Chater

Chater is a cross-platform desktop AI assistant built with .NET and Avalonia UI. It provides a lightweight chat workspace with configurable AI providers, model selection, reusable skills, conversation history, and a system-tray workflow.

## Features

- Chat with multiple provider types:
  - OpenAI
  - Anthropic
  - Ollama
  - OpenAI-compatible endpoints
- Configure API keys, endpoints, active models, and model lists.
- Create, edit, and remove custom skills while keeping built-in skills read-only.
- Persist conversations and messages in SQLite.
- Render assistant responses with Markdown support.
- Switch between system, light, and dark themes.
- English, Simplified Chinese, and Traditional Chinese localization.
- Global keyboard shortcut for showing the chat window.
- System-tray actions for opening chat, opening settings, and exiting the application.
- Custom cross-platform window chrome with macOS title-bar integration.
- Native AOT-compatible project configuration and multi-platform publishing targets.

## Requirements

- .NET SDK 10.0 or later
- A desktop environment supported by Avalonia
- Provider credentials or a locally running Ollama instance, depending on the provider you configure

## Run locally

```bash
dotnet restore Chater.sln
dotnet run --project Chater/Chater.csproj
```

The application creates its SQLite database and support directories on first launch. The default data location is:

- macOS: `~/Library/Application Support/Chater`
- Windows and Linux: the platform's local application-data directory under `Chater`

## Build and test

```bash
dotnet build Chater.sln
dotnet test Chater.sln --no-build
```

For a Release build:

```bash
dotnet build Chater.sln --configuration Release
```

The application targets these publishing runtimes:

- `win-x64`
- `win-arm64`
- `osx-x64`
- `osx-arm64`

See [docs/release/build-and-artifacts.md](docs/release/build-and-artifacts.md) for the release and artifact requirements.

## Configuration

Open the settings window from the gear button in the chat window or from the system tray. The settings pages cover:

- General theme and language preferences
- API provider and model configuration
- Custom skill management
- Chat shortcut configuration

Settings and conversation data are stored locally. API keys are kept in the local SQLite database and are not sent anywhere except to the provider endpoint used for a request.

On macOS, the global shortcut may require Accessibility permission. If permission is unavailable, the application still supports the in-app shortcut and tray actions.

## Project structure

```text
Chater/
├── Data/          SQLite access, repositories, and migrations
├── Models/        Application data models
├── Providers/     Provider connection abstractions
├── Services/      Chat, settings, localization, and window services
├── ViewModels/    MVVM presentation logic
└── Views/         Avalonia windows and settings pages
```

## License

See [LICENSE](LICENSE) for licensing information.
