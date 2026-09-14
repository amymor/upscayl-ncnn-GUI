# Upscayl GUI (VB.NET)

A lightweight, dark-mode GUI wrapper for `upscayl-bin.exe` built in VB.NET 2012 with .NET Framework 4.x compatibility. Provides a modern, borderless interface with native Windows integration for drag-and-drop file handling and folder selection.

## Features

### Core Functionality
- **Dark Mode UI**: Custom HTML/CSS interface hosted in an embedded WebBrowser control
- **Borderless Window**: Modern frameless design with rounded corners (Windows 11)
- **Custom Title Bar**: HTML-based title bar with drag, minimize, and close controls
- **Native File Drop Support**: Full drag-and-drop support via Windows shell integration
- **Folder Batch Processing**: Support for batch upscaling of entire directories
- **Real-time Progress Tracking**: Live output from upscayl-bin with percentage parsing

### Windows Integration
- **Native IFileDialog**: Uses Vista-era folder picker (FOS_PICKFOLDERS) for native Explorer-style dialogs
- **Shell Integration**: Open files/folders directly in Windows Explorer
- **Clipboard Support**: Copy results or paths to clipboard with retry logic
- **Vista/Windows 11 UI**: Respects modern Windows styling with DWM rounded corners

### Settings & Persistence
- **INI Configuration**: Stores user settings locally in `upscayl-gui.ini`
- **Relative Path Handling**: Automatically converts paths inside the exe folder to relative paths for portability
- **Command-Line Arguments**: Pass files/folders as startup arguments
- **Settings Migration**: Seamless persistence across sessions

## Architecture

### Key Components

#### **Bridge Class**
Object exposed to the HTML/JavaScript UI via `WebBrowser.ObjectForScripting`. Acts as the interface between the HTML frontend and VB.NET backend.

**Public Methods:**
- `PickFile(kind)` - Open file picker (engine, input, output)
- `PickFolder(kind, seed)` - Open folder picker with initial directory
- `FileExists(p)` - Check if file exists (relative or absolute path)
- `DirExists(p)` - Check if directory exists
- `ListModels(modelsPath, enginePath)` - Enumerate `.bin` model files
- `RunJob(...)` - Execute upscayl-bin with parameters
- `CancelRun()` - Terminate running job
- `OpenInShell(p)` - Open file/folder in Explorer
- `CopyToClipboard(text)` - Copy text with retry logic
- `SaveSettings(text)` - Persist INI settings
- `DragWindow()`, `MinimizeWindow()`, `CloseWindow()` - Title bar controls
- `ResolvePath(p)` - Convert relative path to absolute (static)

#### **MainForm Class**
The main application window hosting the HTML UI via WebBrowser control.

**Key Features:**
- **Message Filtering**: Intercepts WM_DROPFILES for shell-level drag-drop
- **Process Management**: Spawns `upscayl-bin.exe` as a child process
- **Output Parsing**: Monitors stdout/stderr, extracts progress percentages
- **Settings I/O**: Loads/saves INI configuration
- **Path Resolution**: Handles relative and absolute paths with startup folder anchoring

**Critical Methods:**
- `RunJob(...)` - Validates inputs, creates output directories, builds command line, spawns process
- `ListModels(...)` - Scans models directory for `.bin` files
- `PickFile(...)`, `PickFolder(...)` - File/folder dialogs
- `ShowVistaFolderDialog(...)` - Native IFileDialog implementation with fallback
- `SaveSettings(...)`, `LoadSettingsText()` - INI persistence
- `DragFromTitlebar()`, `MinimizeFromTitlebar()`, `CloseFromTitlebar()` - Window control

## Deployment

### File Structure
```
upscayl-gui.exe
upscayl-gui.html          (HTML/CSS UI)
upscayl-gui.ini           (Settings - auto-created)
upscayl-bin.exe           (Engine executable)
models/                   (Model files - .bin format, optional)
```

### Requirements
- **.NET Framework 4.0+** (VB.NET 2012 compatible)
- **Windows Vista+** (native IFileDialog support)
- **upscayl-bin.exe** in the same directory

### Compilation
Built as a single-file VB.NET 2012 project targeting .NET Framework 4.x.

## Key Implementation Details

### Batch Processing
When a directory is provided as input:
1. Output path is treated as a target directory
2. Output directory is created if it doesn't exist
3. All files in the input directory are processed
4. User receives feedback on directory creation

### Path Resolution
- **Relative paths** (e.g., `models/upscale.bin`) are resolved relative to `Application.StartupPath`
- **Absolute paths** remain unchanged
- **Environment.CurrentDirectory** is explicitly set to `Application.StartupPath` in `Main()` to prevent Windows from overriding it during drag-drop operations

### Process Execution
- Command-line arguments are built with proper quoting: `-i "input" -o "output" [options]`
- Process runs with `UseShellExecute = False` for output redirection
- UTF-8 encoding for stdout/stderr ensures proper character handling
- Progress percentages are extracted from output (e.g., `"45.5%"`)

### File Drop Handling
1. **Shell-level**: `WM_DROPFILES` message intercepted via `IMessageFilter.PreFilterMessage`
2. **WinForms-level**: `DragEnter`/`DragDrop` events handled
3. **WebBrowser child windows**: Enumerated and drop-enabled via `EnumChildWindows`
4. **Delivery**: Path passed to JavaScript `onFileDropped()` event

### Settings Format (INI)
```ini
[engine]
path=upscayl-bin.exe

[output]
format=png

[gpu]
device=auto
```

Paths inside the exe folder are stored relatively; external paths remain absolute.

## Command-Line Options

Full command line built by `RunJob()`:

```
upscayl-bin.exe -i <input> -o <output> [options]

-z <scale>           Model scale (e.g., 2, 3, 4)
-s <scale>           Output scale
-r <resize_mode>     Resize mode
-w <width>           Output width
-c <compress>        Compression level
-t <tile_size>       Tile size for memory efficiency
-m <models_path>     Path to models directory
-n <model_name>      Model name (without .bin extension)
-g <gpu_id>          GPU device ID (auto for default)
-j <threads>         Thread configuration (e.g., 1:2:2)
-x                   Enable TTA (test-time augmentation)
-f <format>          Output format (png, jpg, webp, etc.)
-v                   Verbose output
```

## Native Interop

### DLL Imports
- **user32.dll**: Window management, message handling (`SendMessage`, `ReleaseCapture`, `EnumChildWindows`, `GetAncestor`, `ChangeWindowMessageFilterEx`)
- **shell32.dll**: Drag-drop support (`DragAcceptFiles`, `DragQueryFile`, `DragFinish`), file dialogs (`SHCreateItemFromParsingName`)
- **dwmapi.dll**: Windows 11 rounded corners (`DwmSetWindowAttribute`)

### COM Interfaces
- **IShellItem**: COM interface for file system paths
- **IFileDialog**: Vista file dialog interface (FileOpenDialog variant for folder picking)

## Error Handling

- **Job Validation**: Input/output existence, engine availability
- **Process Management**: Graceful handling of process termination and disposal
- **UI Synchronization**: All script invocations use `SafeUI()` to marshal to UI thread
- **Settings I/O**: Try-catch blocks prevent corruption on save errors
- **Path Resolution**: Fallback behavior for invalid paths

## Performance Considerations

- **Static UI**: Animation-free design ensures smooth window dragging
- **Efficient Output Parsing**: Only processes lines ending with `%` for progress
- **Async Process Reading**: Uses `BeginOutputReadLine()` and `BeginErrorReadLine()` to avoid blocking
- **Memory Management**: Process objects disposed after completion

## Troubleshooting

### Working Directory Issues
If relative paths resolve incorrectly when launching via drag-drop, the `Main()` method explicitly sets `Environment.CurrentDirectory = Application.StartupPath` to override Windows' default behavior.

### Folder Dialog Fallback
The application attempts Vista IFileDialog first, then falls back to `OpenFileDialog` if COM interop fails.

### Model Discovery
`ListModels()` scans the models directory (default: `./models/` relative to exe) for `.bin` files. If the engine path is provided, it searches relative to the engine's directory first.

## Building from Source

Requires Visual Studio 2012 or later with VB.NET support targeting .NET Framework 4.0+.

```bash
vbc /target:winexe /out:upscayl-gui.exe upscayl-gui.vb
```

## License

See the main repository for licensing information.

## Related Files

- **upscayl-gui.html**: HTML/CSS UI frontend
- **upscayl-bin.exe**: NCNN-based upscaling engine
- **upscayl-ncnn**: Main C/C++ backend repository
