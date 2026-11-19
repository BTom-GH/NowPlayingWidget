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

### Download (Recommended)
1. Go to the [Releases](../../releases) page and download the latest Windows `.zip` package:
   * **`NowPlayingWidget-v1.0.0-win-x64-standalone.zip`** — Self-contained (bundles the .NET 8 runtime; easiest for most users).
   * **`NowPlayingWidget-v1.0.0-win-x64-framework-dependent.zip`** — Smaller download, requires the **.NET 8 Desktop Runtime** already installed on Windows.
2. Extract all files into a folder of your choice (keep any accompanying `.dll` files in the same folder as `.exe` file if present).
3. Run `NowPlayingWidget-vx.x.x-win-x64-xxx.exe`.


### Build from Source
Requires the **.NET 8 SDK** on Windows. Run this to build a smaller framework-dependent release (requires the .NET 8 Desktop Runtime to run):

```powershell
dotnet publish .\NowPlayingWidget\NowPlayingWidget.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o .\publish

```

*(Change `--self-contained false` to `--self-contained true` if you want a standalone bundle that includes the .NET 8 runtime. Adjust the `.csproj` path if needed, and zip the output folder if sharing it.)*


### Run Automatically on Windows Startup

1. Press `Win + R`, type `shell:startup`, and press **Enter** to open your Windows Startup folder.
2. Create a shortcut to `NowPlayingWidget.exe` inside that folder.
3. The widget will launch automatically when you sign in to Windows.

## Important Information

* **Platform & Requirements:** Built for **Windows** using **C#, .NET 8, and WPF** (`net8.0-windows`). Running a framework-dependent build requires the .NET 8 Desktop Runtime on Windows; a self-contained build bundles the runtime inside the executable so target Windows machines do not need to install it separately.
* **Local Data & Privacy:** Media metadata and thumbnails are read locally on your machine. Window coordinates are stored locally in `%LocalAppData%\NowPlayingWidget\position.json` and validated against screen bounds on load. No personal data or telemetry is collected or sent externally.
* **Auto-Start on Windows (Optional):** As stated before, you can create a shortcut to `NowPlayingWidget.exe` and place it in your Windows Startup folder (`shell:startup`) to launch automatically when you sign in windows.
