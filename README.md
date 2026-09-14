## Upscayl GUI

A lightweight, dark-mode GUI wrapper for `upscayl-bin.exe` built in VB.NET 2012 with .NET Framework 4.x compatibility. Provides a modern, borderless interface with native Windows integration for drag-and-drop file handling and folder selection.

### Features
- **Dark Mode UI**: Custom HTML/CSS interface
- **Borderless Window**: Modern frameless design with rounded corners (Windows 11)
- **Native File Drop Support**: Full drag-and-drop support via Windows shell integration
- **Folder Batch Processing**: Support for batch upscaling of entire directories
- **INI Configuration**: Stores user settings locally in `upscayl-gui.ini`



### File Structure
```
upscayl-gui.exe
upscayl-gui.html          (HTML/CSS UI)
upscayl-gui.ini           (Settings - auto-created)
upscayl-bin.exe           (Engine executable)
models/                   (Model files - .bin format, optional)
```

### Requirements
- **.NET Framework 4.0+**
- **Windows Vista+**
- **upscayl-bin.exe**


### Batch Processing
When a directory is provided as input:
1. Output path is treated as a target directory
2. Output directory is created if it doesn't exist
3. All files in the input directory are processed
4. User receives feedback on directory creation


### Settings Format (INI)
```ini
[Engine]
path=upscayl-bin.exe
[Options]
modelScale=4
outScale=2
compress=
format=png
models=
modelName=remacri-4x
threads=
tile=0
gpu=
resize=
width=
tta=0
verbose=0
```
> [!NOTE]  
> Paths inside the exe folder are stored relatively; external paths remain absolute.

