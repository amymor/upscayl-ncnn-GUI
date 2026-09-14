' =============================================================================
'  upscayl-gui.vb - dark-mode GUI wrapper for upscayl-bin.exe
'  VB.NET 2012 / .NET Framework 4.x compatible
' =============================================================================

Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Globalization
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Security.Permissions
Imports System.Text
Imports System.Windows.Forms
Imports Microsoft.Win32

' ---- Object exposed to the HTML/JavaScript UI via WebBrowser.ObjectForScripting
<ComVisible(True)>
<PermissionSet(SecurityAction.Demand, Name:="FullTrust")>
Public Class Bridge
    Private ReadOnly _f As MainForm

    Public Sub New(f As MainForm)
        _f = f
    End Sub

    Public Function PickFile(kind As String) As String
        Return _f.PickFile(kind)
    End Function

    Public Function PickFolder(kind As String, seed As String) As String
        Return _f.PickFolder(kind, seed)
    End Function

    Public Function FileExists(p As String) As Boolean
        If String.IsNullOrWhiteSpace(p) Then Return False
        Dim resolved As String = ResolvePath(p)
        Return File.Exists(resolved)
    End Function
    
    Public Function DirExists(p As String) As Boolean
        If String.IsNullOrWhiteSpace(p) Then Return False
        Dim resolved As String = ResolvePath(p)
        Return Directory.Exists(resolved)
    End Function

    Public Function ListModels(modelsPath As String, enginePath As String) As String
        Return _f.ListModels(modelsPath, enginePath)
    End Function

    Public Function RunJob(engine As String, input As String, output As String, modelScale As String, outScale As String, resize As String, width As String, compress As String, tile As String, models As String, modelName As String, gpu As String, threads As String, tta As Boolean, fmt As String, verbose As Boolean) As Boolean
        Return _f.RunJob(engine, input, output, modelScale, outScale, resize, width, compress, tile, models, modelName, gpu, threads, tta, fmt, verbose)
    End Function

    Public Sub CancelRun()
        _f.CancelRun()
    End Sub

    Public Sub OpenInShell(p As String)
        _f.OpenInShell(p)
    End Sub

    Public Sub CopyToClipboard(text As String)
        _f.CopyToClipboard(text)
    End Sub

    Public Sub SaveSettings(text As String)
        _f.SaveSettings(text)
    End Sub

    Public Sub DragWindow()
        _f.DragFromTitlebar()
    End Sub

    Public Sub MinimizeWindow()
        _f.MinimizeFromTitlebar()
    End Sub

    Public Sub CloseWindow()
        _f.CloseFromTitlebar()
    End Sub

    Public Shared Function ResolvePath(p As String) As String
        If String.IsNullOrWhiteSpace(p) Then Return ""
        p = p.Trim().Trim(""""c)
        If Path.IsPathRooted(p) Then Return p
        Try
            Return Path.GetFullPath(Path.Combine(Application.StartupPath, p))
        Catch
            Return p
        End Try
    End Function
End Class

' ---- Main window: hosts the HTML UI ------------------------------------------
Public Class MainForm
    Inherits Form
    Implements IMessageFilter

    Private WithEvents WebBrowser1 As WebBrowser
    Private _proc As Process
    Private _running As Boolean
    Private _canceled As Boolean
    Private _pageReady As Boolean
    Private _pendingDroppedPath As String = ""
    Private _startupArgsApplied As Boolean = False
    Private _enumCallback As EnumChildWindowsCallback

    Private Delegate Function EnumChildWindowsCallback(hWnd As IntPtr, lParam As IntPtr) As Boolean

    Private Const WM_NCLBUTTONDOWN As Integer = &HA1
    Private Const HTCAPTION As Integer = 2

    Private Const WM_DROPFILES As Integer = &H233
    Private Const WM_COPYGLOBAL_DATA As Integer = &H49
    Private Const GA_ROOTOWNER As Integer = 3

    <DllImport("user32.dll")>
    Private Shared Function ReleaseCapture() As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function SendMessage(hWnd As IntPtr, msg As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
    End Function

    ' ---- Native file-drop support ------------------------------------------------
    <DllImport("shell32.dll")>
    Private Shared Sub DragAcceptFiles(hWnd As IntPtr, <MarshalAs(UnmanagedType.Bool)> fAccept As Boolean)
    End Sub

    <DllImport("shell32.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function DragQueryFile(hDrop As IntPtr, iFile As UInteger, lpszFile As StringBuilder, cch As UInteger) As UInteger
    End Function

    <DllImport("shell32.dll")>
    Private Shared Function DragFinish(hDrop As IntPtr) As Integer
    End Function

    <DllImport("user32.dll")>
    Private Shared Function EnumChildWindows(hWnd As IntPtr, lpEnumFunc As EnumChildWindowsCallback, lParam As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetAncestor(hWnd As IntPtr, gaFlags As UInteger) As IntPtr
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function ChangeWindowMessageFilterEx(hwnd As IntPtr, message As UInteger, action As UInteger, changeFilterInfo As IntPtr) As Boolean
    End Function

    ' ---- Vista IFileDialog interop (native Explorer-style folder picker) ----
    Private Const FOS_PICKFOLDERS As UInteger = &H20UI
    Private Const FOS_FORCEFILESYSTEM As UInteger = &H40UI
    Private Const FOS_PATHMUSTEXIST As UInteger = &H800UI
    Private Const SIGDN_FILESYSPATH As UInteger = &H80058000UI
    Private Shared ReadOnly IID_IShellItem As New Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")

    <ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IShellItem
        <PreserveSig> Function BindToHandler(pbc As IntPtr, ByRef bhid As Guid, ByRef riid As Guid, ByRef ppv As IntPtr) As Integer
        <PreserveSig> Function GetParent(ByRef ppsi As IShellItem) As Integer
        <PreserveSig> Function GetDisplayName(sigdnName As UInteger, ByRef ppszName As IntPtr) As Integer
        <PreserveSig> Function GetAttributes(sfgaoMask As UInteger, ByRef psfgaoAttribs As UInteger) As Integer
        <PreserveSig> Function Compare(psi As IShellItem, hint As UInteger, ByRef piOrder As Integer) As Integer
    End Interface

    <ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IFileDialog
        <PreserveSig> Function Show(hwndOwner As IntPtr) As Integer
        <PreserveSig> Function SetFileTypes(cFileTypes As UInteger, rgFilterSpec As IntPtr) As Integer
        <PreserveSig> Function SetFileTypeIndex(iFileType As UInteger) As Integer
        <PreserveSig> Function GetFileTypeIndex(ByRef piFileType As UInteger) As Integer
        <PreserveSig> Function Advise(pfde As IntPtr, ByRef pdwCookie As UInteger) As Integer
        <PreserveSig> Function Unadvise(dwCookie As UInteger) As Integer
        <PreserveSig> Function SetOptions(fos As UInteger) As Integer
        <PreserveSig> Function GetOptions(ByRef pfos As UInteger) As Integer
        <PreserveSig> Function SetDefaultFolder(psi As IShellItem) As Integer
        <PreserveSig> Function SetFolder(psi As IShellItem) As Integer
        <PreserveSig> Function GetFolder(ByRef ppsi As IShellItem) As Integer
        <PreserveSig> Function GetCurrentSelection(ByRef ppsi As IShellItem) As Integer
        <PreserveSig> Function SetFileName(<MarshalAs(UnmanagedType.LPWStr)> pszName As String) As Integer
        <PreserveSig> Function GetFileName(<MarshalAs(UnmanagedType.LPWStr)> ByRef pszName As String) As Integer
        <PreserveSig> Function SetTitle(<MarshalAs(UnmanagedType.LPWStr)> pszTitle As String) As Integer
        <PreserveSig> Function SetOkButtonLabel(<MarshalAs(UnmanagedType.LPWStr)> pszText As String) As Integer
        <PreserveSig> Function SetFileNameLabel(<MarshalAs(UnmanagedType.LPWStr)> pszLabel As String) As Integer
        <PreserveSig> Function GetResult(ByRef ppsi As IShellItem) As Integer
    End Interface

    <DllImport("shell32.dll", CharSet:=CharSet.Unicode, PreserveSig:=False)>
    Private Shared Sub SHCreateItemFromParsingName(<MarshalAs(UnmanagedType.LPWStr)> pszPath As String, pbc As IntPtr, ByRef riid As Guid, <MarshalAs(UnmanagedType.Interface)> ByRef ppv As IShellItem)
    End Sub

    ' ---- DWM rounded corners (Windows 11) ----
    <DllImport("dwmapi.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Shared Function DwmSetWindowAttribute(hwnd As IntPtr, attr As Integer, ByRef val As Integer, size As Integer) As Integer
    End Function

    Public Sub New()
        Text = "UPSCAYL GUI"
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)
        FormBorderStyle = FormBorderStyle.None
        StartPosition = FormStartPosition.CenterScreen
        ClientSize = New Size(900, 820)
        BackColor = Color.FromArgb(7, 10, 18)
        Font = New Font("Segoe UI", 10.0F)

        ' Request rounded corners on Windows 11 (harmless no-op on Windows 10)
        Dim cornerPref As Integer = 2  ' DWMWCP_ROUND
        DwmSetWindowAttribute(Me.Handle, 33, cornerPref, 4)  ' 33 = DWMWA_WINDOW_CORNER_PREFERENCE

        WebBrowser1 = New WebBrowser()
        WebBrowser1.Dock = DockStyle.Fill

        ' Important:
        ' Let IE receive the drop, then VB intercepts it in Navigating / DocumentCompleted.
        WebBrowser1.AllowWebBrowserDrop = True
        ' WebBrowser1.AllowDrop = False
        Me.AllowDrop = True

        WebBrowser1.IsWebBrowserContextMenuEnabled = True
        WebBrowser1.ScriptErrorsSuppressed = True
        WebBrowser1.ScrollBarsEnabled = False
        WebBrowser1.ObjectForScripting = New Bridge(Me)
        Controls.Add(WebBrowser1)
    End Sub

    Private Sub MainForm_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        Application.AddMessageFilter(Me)
        RegisterDropTargetsForBrowser()

        Dim htmlFile As String = Path.Combine(Application.StartupPath, "upscayl-gui.html")
        If Not File.Exists(htmlFile) Then
            MessageBox.Show("upscayl-gui.html was not found next to upscayl-gui.exe.", "GUI missing", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Close()
            Return
        End If
        WebBrowser1.Navigate(htmlFile)
    End Sub

    Private Sub MainForm_FormClosed(sender As Object, e As FormClosedEventArgs) Handles MyBase.FormClosed
        Application.RemoveMessageFilter(Me)
    End Sub

    Private Sub MainForm_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        InvokeScript("saveSettings")
        If Not _running Then Return
        If MessageBox.Show(Me, "A job is still running. Stop it and exit?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.Yes Then
            CancelRun()
        Else
            e.Cancel = True
        End If
    End Sub

    ' ---- Native / WinForms drag-drop --------------------------------------------

    Private Sub MainForm_DragEnter(sender As Object, e As DragEventArgs) Handles MyBase.DragEnter
        SetFileDropEffect(e)
    End Sub

    Private Sub MainForm_DragDrop(sender As Object, e As DragEventArgs) Handles MyBase.DragDrop
        HandleWinFormsFileDrop(e)
    End Sub

    Private Sub SetFileDropEffect(e As DragEventArgs)
        If e Is Nothing OrElse e.Data Is Nothing Then
            Return
        End If

        If e.Data.GetDataPresent(DataFormats.FileDrop) Then
            e.Effect = DragDropEffects.Copy
        Else
            e.Effect = DragDropEffects.None
        End If
    End Sub

    Private Sub HandleWinFormsFileDrop(e As DragEventArgs)
        If e Is Nothing OrElse e.Data Is Nothing Then Return

        Dim files As String() = TryCast(e.Data.GetData(DataFormats.FileDrop), String())
        If files IsNot Nothing AndAlso files.Length > 0 Then
            DeliverDropPath(files(0))
        End If
    End Sub

    Public Function PreFilterMessage(ByRef m As Message) As Boolean Implements IMessageFilter.PreFilterMessage
        If m.Msg = WM_DROPFILES Then
            Dim processIt As Boolean = False

            Try
                If m.HWnd = Me.Handle OrElse m.HWnd = WebBrowser1.Handle Then
                    processIt = True
                Else
                    Dim root As IntPtr = GetAncestor(m.HWnd, CUInt(GA_ROOTOWNER))
                    processIt = (root = Me.Handle)
                End If
            Catch
                processIt = True
            End Try

            If processIt Then
                HandleShellFileDrop(m.WParam)
                Return True
            End If
        End If

        Return False
    End Function

    Private Sub HandleShellFileDrop(hDrop As IntPtr)
        Try
            Dim sb As New StringBuilder(32768)
            Dim chars As UInteger = DragQueryFile(hDrop, CUInt(0), sb, CUInt(sb.Capacity))
            If chars > 0 Then
                Dim p As String = sb.ToString()
                If Not String.IsNullOrWhiteSpace(p) Then
                    DeliverDropPath(p)
                End If
            End If
        Finally
            DragFinish(hDrop)
        End Try
    End Sub

    Private Sub DeliverDropPath(p As String)
        If String.IsNullOrWhiteSpace(p) Then Return
        p = p.Trim().Trim(""""c)

        If Not _pageReady Then
            _pendingDroppedPath = p
        Else
            InvokeScript("onFileDropped", p)
        End If
    End Sub

    Private Sub RegisterDropTargetsForBrowser()
        Try
            If Me.IsHandleCreated Then
                DragAcceptFiles(Me.Handle, True)
                AllowDropMessagesForWindow(Me.Handle)
            End If

            If WebBrowser1.IsHandleCreated Then
                DragAcceptFiles(WebBrowser1.Handle, True)
                AllowDropMessagesForWindow(WebBrowser1.Handle)

                _enumCallback = New EnumChildWindowsCallback(AddressOf EnumChildAcceptFiles)
                EnumChildWindows(WebBrowser1.Handle, _enumCallback, IntPtr.Zero)
            End If
        Catch
        End Try
    End Sub

    Private Function EnumChildAcceptFiles(hWnd As IntPtr, lParam As IntPtr) As Boolean
        Try
            DragAcceptFiles(hWnd, True)
            AllowDropMessagesForWindow(hWnd)
        Catch
        End Try
        Return True
    End Function

    Private Sub AllowDropMessagesForWindow(hwnd As IntPtr)
        Try
            If hwnd = IntPtr.Zero Then Return
            ChangeWindowMessageFilterEx(hwnd, CUInt(WM_DROPFILES), CUInt(1), IntPtr.Zero)
            ChangeWindowMessageFilterEx(hwnd, CUInt(WM_COPYGLOBAL_DATA), CUInt(1), IntPtr.Zero)
        Catch
        End Try
    End Sub

    ' ---- WebBrowser events -------------------------------------------------------

    Private Sub WebBrowser1_Navigating(sender As Object, e As WebBrowserNavigatingEventArgs) Handles WebBrowser1.Navigating
        Dim localPath As String = GetFileUrlLocalPath(e.Url)

        If localPath.Length > 0 Then
            If IsOurHtmlPath(localPath) Then Return

            If File.Exists(localPath) OrElse Directory.Exists(localPath) Then
                e.Cancel = True
                DeliverDropPath(localPath)
                Return
            End If
        End If

        If _pageReady Then
            e.Cancel = True
        End If
    End Sub

    Private Sub WebBrowser1_DocumentCompleted(sender As Object, e As WebBrowserDocumentCompletedEventArgs) Handles WebBrowser1.DocumentCompleted
        If e.Url Is Nothing OrElse WebBrowser1.Url Is Nothing Then Return

        Dim localPath As String = GetFileUrlLocalPath(e.Url)

        ' Safety fallback: if the browser somehow navigated to a dropped file,
        ' save that path, restore the GUI, and apply the path after reload.
        If localPath.Length > 0 AndAlso Not IsOurHtmlPath(localPath) Then
            _pendingDroppedPath = localPath
            _pageReady = False
            WebBrowser1.Navigate(Path.Combine(Application.StartupPath, "upscayl-gui.html"))
            Return
        End If

        If e.Url.LocalPath <> WebBrowser1.Url.LocalPath Then Return

        _pageReady = True
        RegisterDropTargetsForBrowser()

        Dim exeDefault As String = "upscayl-bin.exe"
        Dim exeFound As Boolean = File.Exists(Bridge.ResolvePath(exeDefault))
        InvokeScript("onHostReady", exeDefault, exeFound)

        Dim iniText As String = LoadSettingsText()
        If iniText.Length > 0 Then InvokeScript("applySettings", iniText)

        If Not String.IsNullOrEmpty(_pendingDroppedPath) Then
            Dim p As String = _pendingDroppedPath
            _pendingDroppedPath = ""
            InvokeScript("onFileDropped", p)
        ElseIf Not _startupArgsApplied Then
            _startupArgsApplied = True
            ApplyCommandLineArgs()
        End If
    End Sub

    Private Function GetFileUrlLocalPath(url As Uri) As String
        Try
            If url IsNot Nothing AndAlso url.IsFile Then
                Return url.LocalPath
            End If
        Catch
        End Try
        Return ""
    End Function

    Private Function IsOurHtmlPath(localPath As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(localPath) Then Return False
            Dim fullLocal As String = Path.GetFullPath(localPath)
            Dim fullHtml As String = Path.GetFullPath(Path.Combine(Application.StartupPath, "upscayl-gui.html"))
            Return String.Compare(fullLocal, fullHtml, StringComparison.OrdinalIgnoreCase) = 0
        Catch
            Return False
        End Try
    End Function

    Private Sub ApplyCommandLineArgs()
        Try
            Dim ca As String() = Environment.GetCommandLineArgs()
            For i As Integer = 1 To ca.Length - 1
                Dim a As String = ToAbsolutePath(ca(i).Trim().Trim(""""c))
                If a.Length > 0 AndAlso (File.Exists(a) OrElse Directory.Exists(a)) Then
                    InvokeScript("onFileDropped", a)
                    Exit For
                End If
            Next
        Catch
        End Try
    End Sub

    ' ---- title-bar window actions ----------------------------------------------

    Public Sub DragFromTitlebar()
        ReleaseCapture()
        SendMessage(Handle, WM_NCLBUTTONDOWN, New IntPtr(HTCAPTION), IntPtr.Zero)
    End Sub

    Public Sub MinimizeFromTitlebar()
        WindowState = FormWindowState.Minimized
    End Sub

    Public Sub CloseFromTitlebar()
        Close()
    End Sub

    ' ---- clipboard -------------------------------------------------------------

    Public Sub CopyToClipboard(text As String)
        If String.IsNullOrEmpty(text) Then Return
        For attempt As Integer = 1 To 3
            Try
                Clipboard.SetText(text)
                Return
            Catch
                Threading.Thread.Sleep(60)
            End Try
        Next
    End Sub

    ' ---- job control -----------------------------------------------------------

    Public Function RunJob(engine As String, input As String, output As String, modelScale As String, outScale As String, resize As String, width As String, compress As String, tile As String, models As String, modelName As String, gpu As String, threads As String, tta As Boolean, fmt As String, verbose As Boolean) As Boolean
        engine = If(engine, "")
        input = If(input, "")
        output = If(output, "")
        modelScale = If(modelScale, "")
        outScale = If(outScale, "")
        resize = If(resize, "")
        width = If(width, "")
        compress = If(compress, "")
        tile = If(tile, "")
        models = If(models, "")
        modelName = If(modelName, "")
        gpu = If(gpu, "")
        threads = If(threads, "")
        fmt = If(fmt, "")

        If _running Then
            InvokeScript("setStatus", "Already running", "err")
            Return False
        End If

        If input.Trim().Length = 0 OrElse output.Trim().Length = 0 Then
            InvokeScript("setStatus", "Input and output are required", "err")
            InvokeScript("appendLog", "Input and output are required.", "err")
            Return False
        End If

        Dim engineResolved As String = Bridge.ResolvePath(engine)
        If engineResolved.Length = 0 OrElse Not File.Exists(engineResolved) Then
            InvokeScript("setStatus", "Engine not found", "err")
            InvokeScript("appendLog", "Engine not found: " & engine, "err")
            Return False
        End If
        engine = engineResolved

        ' --- NEW LOGIC: Handle Folder Input/Output ---
        Dim inputResolved As String = Bridge.ResolvePath(input)
        Dim outputResolved As String = Bridge.ResolvePath(output)
        Dim isInputDir As Boolean = Directory.Exists(inputResolved)
        Dim isOutputDir As Boolean = Directory.Exists(outputResolved)
        
        ' If input is a directory, output MUST be treated as a directory target
        If isInputDir Then
             ' Ensure output path ends with separator to force directory interpretation if ambiguous
             If Not outputResolved.EndsWith("\") AndAlso Not outputResolved.EndsWith("/") Then
                 ' Check if user provided a filename extension by mistake, if so, strip it? 
                 ' Better: just treat the parent of the provided output as the target dir if it looks like a file
                 ' But per request: "if there is no such folder exist, then create it".
                 ' So we assume 'output' string is the Desired Folder Path.
             End If
             
             ' Create Output Directory if it doesn't exist
             If Not Directory.Exists(outputResolved) Then
                 Try
                     Directory.CreateDirectory(outputResolved)
                     InvokeScript("appendLog", "Created output folder: " & outputResolved, "dim")
                 Catch ex As Exception
                     InvokeScript("setStatus", "Cannot create output folder", "err")
                     InvokeScript("appendLog", "Error creating folder: " & ex.Message, "err")
                     Return False
                 End Try
             End If
        End If

        Try
            Dim sb As New StringBuilder("-i " & Quote(input) & " -o " & Quote(output))
            If modelScale.Trim().Length > 0 Then sb.Append(" -z ").Append(modelScale.Trim())
            If outScale.Trim().Length > 0 Then sb.Append(" -s ").Append(outScale.Trim())
            If resize.Trim().Length > 0 Then sb.Append(" -r ").Append(Quote(resize.Trim()))
            If width.Trim().Length > 0 Then sb.Append(" -w ").Append(Quote(width.Trim()))
            If compress.Trim().Length > 0 Then sb.Append(" -c ").Append(compress.Trim())
            If tile.Trim().Length > 0 Then sb.Append(" -t ").Append(tile.Trim())

            Dim mPath As String = models.Trim()
            If mPath.Length > 0 AndAlso (String.Compare(mPath, "models", True) <> 0 OrElse mPath.IndexOf(":"c) >= 0 OrElse mPath.StartsWith("\\")) Then
                sb.Append(" -m ").Append(Quote(mPath))
            End If

            If modelName.Trim().Length > 0 Then sb.Append(" -n ").Append(modelName.Trim())
            If gpu.Trim().Length > 0 AndAlso gpu.Trim().ToLowerInvariant() <> "auto" Then sb.Append(" -g ").Append(gpu.Trim())
            If threads.Trim().Length > 0 AndAlso threads.Trim() <> "1:2:2" Then sb.Append(" -j ").Append(threads.Trim())
            If tta Then sb.Append(" -x")
            If fmt.Trim().Length > 0 Then sb.Append(" -f ").Append(fmt.Trim())
            If verbose Then sb.Append(" -v")

            Dim psi As New ProcessStartInfo()
            psi.FileName = engine
            psi.Arguments = sb.ToString()

            Try
                Dim wd As String = Path.GetDirectoryName(Path.GetFullPath(engine))
                If wd IsNot Nothing AndAlso wd.Length > 0 Then psi.WorkingDirectory = wd
            Catch
            End Try

            psi.UseShellExecute = False
            psi.CreateNoWindow = True
            psi.RedirectStandardOutput = True
            psi.RedirectStandardError = True
            psi.StandardOutputEncoding = Encoding.UTF8
            psi.StandardErrorEncoding = Encoding.UTF8

            InvokeScript("appendLog", "> " & Quote(engine) & " " & psi.Arguments, "cmd")
            InvokeScript("setProgress", "0")
            InvokeScript("setStatus", "Running...", "run")
            InvokeScript("setBusy", True)

            _canceled = False
            _proc = New Process()
            _proc.StartInfo = psi
            _proc.EnableRaisingEvents = True
            AddHandler _proc.OutputDataReceived, AddressOf Proc_Output
            AddHandler _proc.ErrorDataReceived, AddressOf Proc_Output
            AddHandler _proc.Exited, AddressOf Proc_Exited
            _proc.Start()
            _proc.BeginOutputReadLine()
            _proc.BeginErrorReadLine()
            _running = True
            Return True
        Catch ex As Exception
            _running = False
            Try
                If _proc IsNot Nothing Then _proc.Dispose()
            Catch
            End Try
            _proc = Nothing
            InvokeScript("setBusy", False)
            InvokeScript("setStatus", "Error - see log", "err")
            InvokeScript("appendLog", "VB error: " & ex.ToString(), "err")
            Return False
        End Try
    End Function

    Public Sub CancelRun()
        _canceled = True
        Try
            If _proc IsNot Nothing AndAlso Not _proc.HasExited Then _proc.Kill()
        Catch
        End Try
    End Sub

    Private Sub Proc_Output(sender As Object, e As DataReceivedEventArgs)
        If e.Data Is Nothing Then Return
        Dim line As String = e.Data
        SafeUI(Sub()
                   Dim t As String = line.Trim()
                   Dim pct As Double
                   If t.Length > 0 AndAlso t.EndsWith("%") AndAlso Double.TryParse(t.TrimEnd("%"c).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, pct) AndAlso pct >= 0 AndAlso pct <= 100 Then
                       InvokeScript("setProgress", Math.Round(pct, 2).ToString(CultureInfo.InvariantCulture))
                   ElseIf t.Length > 0 Then
                       InvokeScript("appendLog", line)
                   End If
               End Sub)
    End Sub

    Private Sub Proc_Exited(sender As Object, e As EventArgs)
        Dim p As Process = DirectCast(sender, Process)
        Dim code As Integer
        Try
            code = p.ExitCode
        Catch
            code = -1
        End Try

        SafeUI(Sub()
                   _running = False
                   If _proc Is p Then _proc = Nothing
                   InvokeScript("setBusy", False)

                   If _canceled Then
                       InvokeScript("setStatus", "Canceled", "cancel")
                   ElseIf code = 0 Then
                       InvokeScript("setProgress", "100")
                       InvokeScript("setStatus", "Done", "ok")
                   Else
                       InvokeScript("setStatus", "Failed (exit code " & code.ToString(CultureInfo.InvariantCulture) & ")", "err")
                   End If

                   InvokeScript("appendLog", "=== exit code " & code.ToString(CultureInfo.InvariantCulture) & " ===", "dim")

                   Try
                       p.Dispose()
                   Catch
                   End Try
               End Sub)
    End Sub

    ' ---- model discovery -------------------------------------------------------

    Public Function ListModels(modelsPath As String, enginePath As String) As String
        Dim dir As String = ResolveModelsDir(modelsPath, enginePath)
        Dim names As New List(Of String)()

        Try
            If Directory.Exists(dir) Then
                names.AddRange(Directory.GetFiles(dir, "*.bin"))
                For i As Integer = 0 To names.Count - 1
                    names(i) = Path.GetFileNameWithoutExtension(names(i))
                Next
            End If
        Catch
        End Try

        names.Sort(StringComparer.OrdinalIgnoreCase)
        Return String.Join("|", names.ToArray())
    End Function

    Private Shared Function ResolveModelsDir(modelsPath As String, enginePath As String) As String
        Dim p As String = ""
        If modelsPath IsNot Nothing Then p = modelsPath.Trim().Trim(""""c)
        If p.Length = 0 Then p = "models"
        If Path.IsPathRooted(p) Then Return p

        Dim baseDir As String = Application.StartupPath
        Dim e As String = ""
        If enginePath IsNot Nothing Then e = enginePath.Trim().Trim(""""c)

        If e.Length > 0 Then
            Dim resolvedEngine As String = Bridge.ResolvePath(e)
            If File.Exists(resolvedEngine) Then
                Dim d As String = Path.GetDirectoryName(Path.GetFullPath(resolvedEngine))
                If d IsNot Nothing AndAlso Directory.Exists(d) Then baseDir = d
            End If
        End If

        Try
            Return Path.GetFullPath(Path.Combine(baseDir, p))
        Catch
            Return p
        End Try
    End Function

    ' ---- settings ini -----------------------------------------------------------

    Private Shared Function IniPath() As String
        Return Path.Combine(Application.StartupPath, "upscayl-gui.ini")
    End Function

    Public Sub SaveSettings(text As String)
        Try
            If text Is Nothing Then Return
            Dim lines As String() = text.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf).Split(vbLf)
            Dim sb As New StringBuilder()
            Dim inEngine As Boolean = False

            For Each ln As String In lines
                Dim t As String = ln.Trim()
                If t.StartsWith("[") Then
                    inEngine = (String.Compare(t, "[engine]", StringComparison.OrdinalIgnoreCase) = 0)
                    sb.AppendLine(ln)
                ElseIf inEngine AndAlso t.ToLowerInvariant().StartsWith("path=") Then
                    sb.Append("path=").AppendLine(ToRelativePath(t.Substring(5)))
                Else
                    sb.AppendLine(ln)
                End If
            Next

            File.WriteAllText(IniPath(), sb.ToString(), New UTF8Encoding(False))
        Catch
        End Try
    End Sub

    Private Function LoadSettingsText() As String
        Try
            Dim f As String = IniPath()
            If Not File.Exists(f) Then Return ""
            Return File.ReadAllText(f)
        Catch
            Return ""
        End Try
    End Function

    ' Paths inside the exe folder are stored relatively, others stay absolute.
    Private Shared Function ToRelativePath(p As String) As String
        Try
            If String.IsNullOrWhiteSpace(p) Then Return ""
            
            ' FIX 3: Use ResolvePath to anchor strictly to StartupPath, ignoring CurrentDirectory
            Dim fullP As String = Bridge.ResolvePath(p)
            Dim fullB As String = Application.StartupPath
            
            If Not fullB.EndsWith("\") Then fullB &= "\"
            If fullP.Length > fullB.Length AndAlso String.Compare(fullP.Substring(0, fullB.Length), fullB, True) = 0 Then
                Return fullP.Substring(fullB.Length)
            End If
            Return fullP
        Catch
            Return p
        End Try
    End Function

    Private Shared Function ToAbsolutePath(p As String) As String
        Try
            If String.IsNullOrWhiteSpace(p) Then Return ""
            If Path.IsPathRooted(p) Then Return p
            Return Path.GetFullPath(Path.Combine(Application.StartupPath, p))
        Catch
            Return p
        End Try
    End Function

    ' ---- pickers / shell -------------------------------------------------------

    Public Function PickFile(kind As String) As String
        Using dlg As New OpenFileDialog()
            If kind = "engine" Then
                dlg.Title = "Select upscayl-bin.exe"
                dlg.Filter = "upscayl-bin.exe|upscayl-bin.exe|Executables|*.exe|All files|*.*"
            ElseIf kind = "output" Then
                dlg.Title = "Select output image"
                dlg.Filter = "Images|*.png;*.jpg;*.jpeg;*.webp|All files|*.*"
            Else
                dlg.Title = "Select input image"
                dlg.Filter = "Images|*.jpg;*.jpeg;*.png;*.webp|All files|*.*"
            End If

            If dlg.ShowDialog(Me) = DialogResult.OK Then Return dlg.FileName
        End Using
        Return ""
    End Function

    Public Function PickFolder(kind As String, seed As String) As String
        Dim dialogTitle As String = "Select the input folder"
        If kind = "output" Then
            dialogTitle = "Select the output folder"
        ElseIf kind = "models" Then
            dialogTitle = "Select the models folder"
        End If

        Dim picked As String = ShowVistaFolderDialog(dialogTitle, SeedToDir(seed))
        If picked IsNot Nothing Then Return picked
        Return PickFolderLegacy(dialogTitle)
    End Function

    Private Shared Function SeedToDir(seed As String) As String
        Try
            If seed IsNot Nothing Then
                Dim s As String = seed.Trim().Trim(""""c)
                If s.Length > 0 Then
                    If Not Path.IsPathRooted(s) Then s = Path.Combine(Application.StartupPath, s)
                    If Directory.Exists(s) Then Return s
                    If File.Exists(s) Then
                        Dim d As String = Path.GetDirectoryName(s)
                        If d IsNot Nothing AndAlso Directory.Exists(d) Then Return d
                    End If
                End If
            End If
        Catch
        End Try
        Return Nothing
    End Function

    Private Function ShowVistaFolderDialog(dialogTitle As String, initialDir As String) As String
        Dim dlg As IFileDialog = Nothing

        Try
            Dim dlgType As Type = Type.GetTypeFromCLSID(New Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7"))
            Dim obj As Object = Activator.CreateInstance(dlgType)
            dlg = DirectCast(obj, IFileDialog)

            Dim opts As UInteger
            dlg.GetOptions(opts)
            dlg.SetOptions(opts Or FOS_PICKFOLDERS Or FOS_FORCEFILESYSTEM Or FOS_PATHMUSTEXIST)
            dlg.SetTitle(dialogTitle)
            dlg.SetOkButtonLabel("Select Folder")

            If initialDir IsNot Nothing AndAlso Directory.Exists(initialDir) Then
                Try
                    Dim si As IShellItem = Nothing
                    SHCreateItemFromParsingName(initialDir, IntPtr.Zero, IID_IShellItem, si)
                    If si IsNot Nothing Then
                        dlg.SetFolder(si)
                        Marshal.ReleaseComObject(si)
                    End If
                Catch
                End Try
            End If

            If dlg.Show(Handle) = 0 Then
                Dim res As IShellItem = Nothing
                If dlg.GetResult(res) = 0 AndAlso res IsNot Nothing Then
                    Dim picked As String = ""
                    Dim namePtr As IntPtr
                    If res.GetDisplayName(SIGDN_FILESYSPATH, namePtr) = 0 AndAlso namePtr <> IntPtr.Zero Then
                        picked = Marshal.PtrToStringUni(namePtr)
                        Marshal.FreeCoTaskMem(namePtr)
                    End If
                    Marshal.ReleaseComObject(res)
                    Return picked
                End If
            End If

            Return ""
        Catch
            Return Nothing
        Finally
            If dlg IsNot Nothing Then
                Try
                    Marshal.ReleaseComObject(dlg)
                Catch
                End Try
            End If
        End Try
    End Function

    Private Function PickFolderLegacy(dialogTitle As String) As String
        Using dlg As New OpenFileDialog()
            dlg.Title = dialogTitle
            dlg.CheckFileExists = False
            dlg.ValidateNames = False
            dlg.FileName = "Folder Selection."

            If dlg.ShowDialog(Me) = DialogResult.OK Then
                Try
                    Dim d As String = Path.GetDirectoryName(dlg.FileName)
                    If d IsNot Nothing AndAlso Directory.Exists(d) Then Return d
                Catch
                End Try
                Return dlg.FileName
            End If
        End Using
        Return ""
    End Function

    Public Sub OpenInShell(p As String)
        Try
            If String.IsNullOrWhiteSpace(p) Then Return

            Dim resolved As String = Bridge.ResolvePath(p)

            If Directory.Exists(resolved) Then
                Process.Start("explorer.exe", Quote(resolved))
            ElseIf File.Exists(resolved) Then
                Process.Start("explorer.exe", "/select," & Quote(resolved))
            Else
                Dim d As String = Path.GetDirectoryName(resolved)
                If d IsNot Nothing AndAlso Directory.Exists(d) Then Process.Start("explorer.exe", Quote(d))
            End If
        Catch
        End Try
    End Sub

    ' ---- helpers ---------------------------------------------------------------

    Private Shared Function Quote(s As String) As String
        Return """" & s & """"
    End Function

    Private Sub SafeUI(a As Action)
        If IsDisposed OrElse Not IsHandleCreated Then Return
        If InvokeRequired Then BeginInvoke(a) Else a()
    End Sub

    Private Sub InvokeScript(name As String, ParamArray args() As Object)
        SafeUI(Sub()
                   Try
                       If WebBrowser1.Document IsNot Nothing Then WebBrowser1.Document.InvokeScript(name, args)
                   Catch
                   End Try
               End Sub)
    End Sub
End Class

' ---- Entry point ---------------------------------------------------------------
Friend Module Program
    <STAThread>
    Public Sub Main()
        ' FIX 3: Force working directory to the executable's folder.
        ' When launching via %1 (drag-drop onto exe), Windows sets CurrentDirectory 
        ' to the dropped file's folder. This breaks relative path resolution.
        Try
            Environment.CurrentDirectory = Application.StartupPath
        Catch
        End Try

        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Application.Run(New MainForm())
    End Sub
End Module
