# NowPlayingWidget

Small widget to see and control currently playing media on your computer without having to find or open the app everytime.

## Key Features

* **Now Playing Display:** Shows the current track title, artist, and album artwork in a compact desktop widget. Control buttons also let you pause or change track.
* * **Focus Media App:** Double-click the widget (or click the album art when not dragging) to bring the active media app to the front.
* **Multi-Source Media Tracking:** Works across active Windows media sessions (such as music players and browsers). Scroll while hovering to browse these sessions.
* **Drag & Remember Position:** Drag the widget anywhere on your screen; its position is saved locally (to `%LocalAppData%\NowPlayingWidget\position.json`) and restored automatically.
* **System Tray Integration:** Runs quietly with a custom vinyl tray icon and tray menu options.
<p align="center">
  <img width="59" height="58" alt="vinyl tray icon" src="https://github.com/user-attachments/assets/9c782f92-7e94-45c3-b941-47d73d910980" />
</p>

## Build and Run

### Publish a Standalone Executable
Target the `.csproj` file directly to build a self-contained single-file release executable without `.pdb` debug symbol files:

```powershell
dotnet publish .\NowPlayingWidget\NowPlayingWidget.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o .\publish-standalone

```

*(Adjust `.\NowPlayingWidget\NowPlayingWidget.csproj` if your project file is in a different path or has a different name.)*

### Run Automatically on Windows Startup

1. Press `Win + R`, type `shell:startup`, and press **Enter** to open your Windows Startup folder.
2. Create a shortcut to `NowPlayingWidget.exe` inside that folder.
3. The widget will launch automatically when you sign in to Windows.

## Important Information

* **Platform & Requirements:** Built for **Windows** using **C#, .NET 8, and WPF** (`net8.0-windows`). Running a framework-dependent build requires the .NET 8 Desktop Runtime on Windows; a self-contained build bundles the runtime inside the executable so target Windows machines do not need to install it separately.
* **Local Data & Privacy:** Media metadata and thumbnails are read locally on your machine. Window coordinates are stored locally in `%LocalAppData%\NowPlayingWidget\position.json` and validated against screen bounds on load. No personal data or telemetry is collected or sent externally.
* **Auto-Start on Windows (Optional):** As stated before, you can create a shortcut to `NowPlayingWidget.exe` and place it in your Windows Startup folder (`shell:startup`) to launch automatically when you sign in windows.
