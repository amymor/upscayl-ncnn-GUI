## Upscayl GUI

A Lightweight & Portable, dark-mode GUI wrapper for `upscayl-bin.exe`. Provides a modern interface with native Windows integration for drag-and-drop file/folder handling.

### 📷 Screenshot
<img width="976" height="881" alt="image" src="https://github.com/user-attachments/assets/ef50f0f0-d190-4d68-8647-98a2a4dbe03e" />

---
### ✨Features
- **Small Footprint** Just ~80KB
- **Portable INI Configuration**: Stores user settings locally in `upscayl-gui.ini`
- **Dark Mode UI**: Custom HTML/CSS interface
- **Modern Interface**: Modern design with rounded corners (Windows 11)
- **Native File Drop Support**: Full drag-and-drop support via Windows shell integration
- **Folder Batch Processing**: Support for batch upscaling of entire folders

---

### ⬇️ Download Upscayl GUI

[Download latest version here](https://github.com/amymor/upscayl-ncnn-GUI/releases/latest)

### ⬇️ Download Engine (`upscayl-bin.exe`)

### ⬇️ Download Models (for ease of use, put them in the “Models” folder)
[Download lightweight fast models here]()
[Download Upscayl models here](https://github.com/upscayl/upscayl/tree/main/resources/models)
[Want more models?](https://openmodeldb.info/)

---

### 🗂️ File Structure
```
models\                   (Model files - .bin format)
upscayl-bin.exe           (Engine executable)
upscayl-gui.exe           (GUI App)
upscayl-gui.html          (HTML/CSS UI)
upscayl-gui.ini           (Settings - auto-created)
```
<img width="511" height="200" alt="image" src="https://github.com/user-attachments/assets/a8489582-8393-44bc-8489-9bd5ef8f5869" />

---


### ⚙️ Settings Structure (`upscayl-gui.ini`)
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

---

### 📚 Batch Processing
When a folder is provided as input:
1. Output path is treated as a target folder
2. Output folder is created if it doesn't exist
3. All Supported files in the input folder are processed
