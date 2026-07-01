Imports System.Diagnostics
Imports System.IO.Compression
Imports System.Security.Cryptography
Imports System.Text
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

Public Enum OmniMixBepInExStatus
    NotInstalled
    Managed
    NeedsUpdate
    Unmanaged
End Enum

Public Enum OmniMixModInstallStatus
    NotInstalled
    Installed
    NeedsUpdate
End Enum

Public Class OmniMixGameDeclaration
    Public Property Id As String = ""
    Public Property Name As String = ""
    Public Property ExeName As String = ""
    Public Property SignatureFiles As List(Of String) = New List(Of String)
    Public Property SupportedFrameworks As List(Of String) = New List(Of String)
    Public Property SupportedMods As List(Of String) = New List(Of String)
    Public Property WebsiteUrl As String = ""
End Class

Public Class OmniMixFrameworkDeclaration
    Public Property Id As String = ""
    Public Property Name As String = ""
    Public Property Version As String = ""
    Public Property ArchiveName As String = ""
    Public Property FilesToLink As List(Of String) = New List(Of String)
    Public Property DirsToLink As List(Of String) = New List(Of String)
    Public Property DirsToCreate As List(Of String) = New List(Of String)
End Class

Public Class OmniMixModDeclaration
    Public Property Id As String = ""
    Public Property Name As String = ""
    Public Property Version As String = ""
    Public Property ArchiveName As String = ""
    Public Property TargetFramework As String = ""
    Public Property FolderName As String = ""
    Public Property RootFilesToLink As List(Of String) = New List(Of String)
    Public Property RootDirsToLink As List(Of String) = New List(Of String)
    Public Property RootFilesNoBackup As List(Of String) = New List(Of String)
    Public Property CopyRootFiles As Boolean = False
    Public Property Mode As String = ""

    Public ReadOnly Property UsesFramework As Boolean
        Get
            Return Not String.IsNullOrWhiteSpace(TargetFramework)
        End Get
    End Property

    Public ReadOnly Property InstallsToGameRoot As Boolean
        Get
            Return RootFilesToLink.Count > 0 OrElse RootDirsToLink.Count > 0
        End Get
    End Property
End Class

Public Module OmniMixModDeploymentService

    Private Const BepInExArchiveName As String = "BepInEx_win_x64_5.4.23.5.zip"
    Private Const InstanceMarkerName As String = ".omnimix_instance_id"
    Private Const PortFileName As String = "omnimix_port.txt"
    Private ReadOnly PlainUtf8 As New UTF8Encoding(False)

    Private ReadOnly JsonSettings As New JsonSerializerSettings With {.Formatting = Formatting.Indented}

    Public Function GetGameCatalog() As List(Of OmniMixGameDeclaration)
        Return New List(Of OmniMixGameDeclaration) From {
            New OmniMixGameDeclaration With {
                .Id = "chill_with_you",
                .Name = "Chill With You",
                .ExeName = "Chill With You.exe",
                .SignatureFiles = New List(Of String) From {"Chill With You.exe", "Chill With You_Data"},
                .SupportedFrameworks = New List(Of String) From {"bepinex_5"},
                .SupportedMods = New List(Of String) From {"chill_patcher"},
                .WebsiteUrl = "https://store.steampowered.com/app/3548580"
            },
            New OmniMixGameDeclaration With {
                .Id = "forza_horizon_6",
                .Name = "Forza Horizon 6",
                .ExeName = "forzahorizon6.exe",
                .SignatureFiles = New List(Of String) From {"forzahorizon6.exe"},
                .SupportedFrameworks = New List(Of String),
                .SupportedMods = New List(Of String) From {"fh6_omni_bridge"},
                .WebsiteUrl = "https://forza.net"
            }
        }
    End Function

    Public Function GetFrameworkCatalog() As List(Of OmniMixFrameworkDeclaration)
        Return New List(Of OmniMixFrameworkDeclaration) From {
            New OmniMixFrameworkDeclaration With {
                .Id = "bepinex_5",
                .Name = "BepInEx",
                .Version = "5.4.23.5",
                .ArchiveName = BepInExArchiveName,
                .FilesToLink = New List(Of String) From {"winhttp.dll", "doorstop_config.ini"},
                .DirsToLink = New List(Of String) From {"BepInEx\core"},
                .DirsToCreate = New List(Of String) From {"BepInEx", "BepInEx\plugins", "BepInEx\patchers"}
            }
        }
    End Function

    Public Function GetModCatalog() As List(Of OmniMixModDeclaration)
        Return New List(Of OmniMixModDeclaration) From {
            New OmniMixModDeclaration With {
                .Id = "chill_patcher",
                .Name = "ChillPatcher",
                .Version = GetBundledModVersion("chill_patcher", "1.0.0"),
                .ArchiveName = "ChillPatcher.zip",
                .TargetFramework = "bepinex_5",
                .FolderName = "ChillPatcher",
                .Mode = "client"
            },
            New OmniMixModDeclaration With {
                .Id = "fh6_omni_bridge",
                .Name = "Forza Horizon 6 Omni Bridge",
                .Version = GetBundledModVersion("fh6_omni_bridge", "3.0.0"),
                .ArchiveName = "FH6OmniBridge.zip",
                .FolderName = "fh6-omnimix",
                .RootFilesToLink = New List(Of String) From {"version.dll", "OmniPcmShared.dll"},
                .RootFilesNoBackup = New List(Of String) From {"version.dll", "OmniPcmShared.dll"},
                .CopyRootFiles = True,
                .Mode = "server"
            }
        }
    End Function

    Private Function GetBundledModVersion(ModId As String, Fallback As String) As String
        If String.Equals(ModId, "fh6_omni_bridge", StringComparison.OrdinalIgnoreCase) Then
            Return "3.0.0"
        End If
        Try
            Dim VersionInfoPath = ResolveAssetPath("version_info.json")
            If String.IsNullOrWhiteSpace(VersionInfoPath) Then Return Fallback
            Dim Root = JObject.Parse(File.ReadAllText(VersionInfoPath, Encoding.UTF8))
            Dim ModVersions = TryCast(Root("mod_versions"), JObject)
            Dim VersionToken = If(ModVersions Is Nothing, Nothing, ModVersions(ModId))
            If VersionToken IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(VersionToken.ToString()) Then Return VersionToken.ToString()

            Dim LegacyField = If(String.Equals(ModId, "fh6_omni_bridge", StringComparison.OrdinalIgnoreCase), "fh6_bridge_version", "mod_version")
            VersionToken = Root(LegacyField)
            If VersionToken IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(VersionToken.ToString()) Then Return VersionToken.ToString()
        Catch
        End Try
        Return Fallback
    End Function

    Public Function GetGame(GameId As String) As OmniMixGameDeclaration
        Return GetGameCatalog().FirstOrDefault(Function(Game) String.Equals(Game.Id, GameId, StringComparison.OrdinalIgnoreCase))
    End Function

    Public Function GetMod(ModId As String) As OmniMixModDeclaration
        Return GetModCatalog().FirstOrDefault(Function(ModInfo) String.Equals(ModInfo.Id, ModId, StringComparison.OrdinalIgnoreCase))
    End Function

    Public Function GetPrimaryMod(Game As OmniMixGameDeclaration) As OmniMixModDeclaration
        If Game Is Nothing OrElse Game.SupportedMods.Count = 0 Then Return Nothing
        Return GetMod(Game.SupportedMods.First())
    End Function

    Public Function GetBepInExFramework() As OmniMixFrameworkDeclaration
        Return GetFrameworkCatalog().First(Function(Framework) Framework.Id = "bepinex_5")
    End Function

    Public ReadOnly Property ManagerDir As String
        Get
            Dim LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            If String.IsNullOrWhiteSpace(LocalAppData) Then LocalAppData = Path.GetTempPath()
            Return Path.Combine(LocalAppData, "OmniMixPlayer", "mod_manager")
        End Get
    End Property

    Private ReadOnly Property PathsDbPath As String
        Get
            Return Path.Combine(ManagerDir, "vb_game_paths.json")
        End Get
    End Property

    Private ReadOnly Property InstalledVersionsDbPath As String
        Get
            Return Path.Combine(ManagerDir, "installed_versions.json")
        End Get
    End Property

    Public Function LoadGamePath(GameId As String) As String
        Try
            Dim Paths = ReadGamePaths()
            If Paths.ContainsKey(GameId) Then Return Paths(GameId)
            If String.Equals(GameId, "chill_with_you", StringComparison.OrdinalIgnoreCase) AndAlso Paths.ContainsKey("legacy_chill_with_you") Then Return Paths("legacy_chill_with_you")
        Catch
        End Try
        Return ""
    End Function

    Public Sub SaveGamePath(GameId As String, GamePath As String)
        Dim Paths = ReadGamePaths()
        Paths(GameId) = If(GamePath, "")
        Directory.CreateDirectory(ManagerDir)
        File.WriteAllText(PathsDbPath, JsonConvert.SerializeObject(Paths, JsonSettings), Encoding.UTF8)
    End Sub

    Private Class OmniMixGameInstallLayout
        Public Property IsValid As Boolean
        Public Property Kind As String = "default"
        Public Property SelectedRoot As String = ""
        Public Property RuntimeDir As String = ""
        Public Property MediaRoot As String = ""
        Public Property ConfigDir As String = ""
        Public Property MarkerDir As String = ""
    End Class

    Private Function ResolveGameInstallLayout(GamePath As String, Game As OmniMixGameDeclaration) As OmniMixGameInstallLayout
        Dim Layout As New OmniMixGameInstallLayout With {
            .SelectedRoot = If(GamePath, ""),
            .RuntimeDir = If(GamePath, ""),
            .MediaRoot = If(GamePath, ""),
            .ConfigDir = If(GamePath, ""),
            .MarkerDir = If(GamePath, "")
        }
        If String.IsNullOrWhiteSpace(GamePath) OrElse Game Is Nothing Then Return Layout

        Try
            If Not Directory.Exists(GamePath) Then Return Layout
            If String.Equals(Game.Id, "forza_horizon_6", StringComparison.OrdinalIgnoreCase) Then
                Dim RootExePath = Path.Combine(GamePath, Game.ExeName)
                Dim ContentDir = Path.Combine(GamePath, "Content")
                Dim ContentExePath = Path.Combine(ContentDir, Game.ExeName)
                Dim RootMediaDir = ResolveExistingMediaDir(GamePath)
                Dim ParentDir = Directory.GetParent(GamePath)?.FullName
                Dim ParentMediaDir = ResolveExistingMediaDir(ParentDir)

                If File.Exists(ContentExePath) AndAlso Not String.IsNullOrWhiteSpace(RootMediaDir) Then
                    Layout.Kind = "xbox"
                    Layout.RuntimeDir = ContentDir
                    Layout.MediaRoot = GamePath
                    Layout.ConfigDir = If(File.Exists(Path.Combine(ContentDir, "MicrosoftGame.Config")), ContentDir, GamePath)
                    Layout.MarkerDir = ContentDir
                    Layout.IsValid = True
                    Return Layout
                End If

                If File.Exists(RootExePath) AndAlso
                   String.Equals(Path.GetFileName(GamePath.TrimEnd("\"c, "/"c)), "Content", StringComparison.OrdinalIgnoreCase) AndAlso
                   Not String.IsNullOrWhiteSpace(ParentDir) AndAlso
                   Not String.IsNullOrWhiteSpace(ParentMediaDir) Then
                    Layout.Kind = "xbox"
                    Layout.SelectedRoot = ParentDir
                    Layout.RuntimeDir = GamePath
                    Layout.MediaRoot = ParentDir
                    Layout.ConfigDir = If(File.Exists(Path.Combine(GamePath, "MicrosoftGame.Config")), GamePath, ParentDir)
                    Layout.MarkerDir = GamePath
                    Layout.IsValid = True
                    Return Layout
                End If

                If File.Exists(RootExePath) AndAlso Not String.IsNullOrWhiteSpace(RootMediaDir) Then
                    Layout.Kind = "steam"
                    Layout.RuntimeDir = GamePath
                    Layout.MediaRoot = GamePath
                    Layout.ConfigDir = GamePath
                    Layout.MarkerDir = GamePath
                    Layout.IsValid = True
                    Return Layout
                End If

                If File.Exists(RootExePath) Then
                    Layout.Kind = "steam"
                    Layout.RuntimeDir = GamePath
                    Layout.MediaRoot = GamePath
                    Layout.ConfigDir = GamePath
                    Layout.MarkerDir = GamePath
                    Layout.IsValid = True
                    Return Layout
                End If

                Return Layout
            End If

            For Each Signature In Game.SignatureFiles
                Dim Candidate = Path.Combine(GamePath, Signature)
                If Not File.Exists(Candidate) AndAlso Not Directory.Exists(Candidate) Then Return Layout
            Next
            Layout.IsValid = True
        Catch
        End Try
        Return Layout
    End Function

    Private Function ResolveExistingMediaDir(BaseDir As String) As String
        If String.IsNullOrWhiteSpace(BaseDir) Then Return ""
        Dim Lower = Path.Combine(BaseDir, "media")
        If Directory.Exists(Lower) Then Return Lower
        Dim Upper = Path.Combine(BaseDir, "Media")
        If Directory.Exists(Upper) Then Return Upper
        Return ""
    End Function

    Private Function GetMediaDirectoryName(MediaRoot As String) As String
        Dim MediaDir = ResolveExistingMediaDir(MediaRoot)
        If Not String.IsNullOrWhiteSpace(MediaDir) Then Return Path.GetFileName(MediaDir)
        Return "media"
    End Function

    Private Function ResolveRootModInstallDir(GamePath As String, ModInfo As OmniMixModDeclaration) As String
        If ModInfo Is Nothing Then Return GamePath
        Dim Game = GetGameCatalog().FirstOrDefault(Function(Item) Item.SupportedMods.Contains(ModInfo.Id))
        Dim Layout = ResolveGameInstallLayout(GamePath, Game)
        If Layout.IsValid Then Return Layout.RuntimeDir
        Return GamePath
    End Function

    Public Function ResolveGameMarkerDir(GamePath As String, ModInfo As OmniMixModDeclaration) As String
        If ModInfo Is Nothing Then Return GamePath
        Dim Game = GetGameCatalog().FirstOrDefault(Function(Item) Item.SupportedMods.Contains(ModInfo.Id))
        Dim Layout = ResolveGameInstallLayout(GamePath, Game)
        If Layout.IsValid Then Return Layout.MarkerDir
        Return GamePath
    End Function

    Private Function ResolveMediaRoot(GamePath As String) As String
        Dim Game = GetGame("forza_horizon_6")
        Dim Layout = ResolveGameInstallLayout(GamePath, Game)
        If Layout.IsValid Then Return Layout.MediaRoot
        Return GamePath
    End Function

    Public Function GetGameInstallLayoutDescription(GamePath As String, Game As OmniMixGameDeclaration) As String
        Dim Layout = ResolveGameInstallLayout(GamePath, Game)
        If Not Layout.IsValid Then Return ""
        If String.Equals(Layout.Kind, "xbox", StringComparison.OrdinalIgnoreCase) Then Return "Xbox 版结构"
        If String.Equals(Layout.Kind, "steam", StringComparison.OrdinalIgnoreCase) Then Return "Steam 版结构"
        Return "标准结构"
    End Function

    Public Function VerifyGameDirectory(GamePath As String, Game As OmniMixGameDeclaration) As Boolean
        Return ResolveGameInstallLayout(GamePath, Game).IsValid
    End Function

    Public Function CheckBepInExStatus(GamePath As String) As OmniMixBepInExStatus
        If String.IsNullOrWhiteSpace(GamePath) Then Return OmniMixBepInExStatus.NotInstalled
        Dim WinHttpPath = Path.Combine(GamePath, "winhttp.dll")
        Dim CoreDllPath = Path.Combine(GamePath, "BepInEx", "core", "BepInEx.dll")
        Dim MarkerPath = Path.Combine(GamePath, "BepInEx", ".omnimix_managed")
        If File.Exists(WinHttpPath) OrElse File.Exists(CoreDllPath) Then
            If Not File.Exists(MarkerPath) Then Return OmniMixBepInExStatus.Unmanaged
            Dim InstalledVersion = ReadTextFile(MarkerPath)
            If Not String.IsNullOrWhiteSpace(InstalledVersion) AndAlso
               Not String.Equals(InstalledVersion, GetBepInExFramework().Version, StringComparison.OrdinalIgnoreCase) Then
                Return OmniMixBepInExStatus.NeedsUpdate
            End If
            Return OmniMixBepInExStatus.Managed
        End If
        Return OmniMixBepInExStatus.NotInstalled
    End Function

    Public Function CheckModStatus(GamePath As String, ModInfo As OmniMixModDeclaration) As OmniMixModInstallStatus
        If String.IsNullOrWhiteSpace(GamePath) OrElse ModInfo Is Nothing Then Return OmniMixModInstallStatus.NotInstalled
        Dim InstallDir = ResolveRootModInstallDir(GamePath, ModInfo)
        Dim MarkerDir = ResolveGameMarkerDir(GamePath, ModInfo)
        If ModInfo.InstallsToGameRoot Then
            Dim MarkerPath = Path.Combine(MarkerDir, ".omnimix_mods", ModInfo.Id & ".managed")
            If Not File.Exists(MarkerPath) Then Return OmniMixModInstallStatus.NotInstalled
            For Each RelativeFile In ModInfo.RootFilesToLink
                If Not File.Exists(Path.Combine(InstallDir, RelativeFile)) Then Return OmniMixModInstallStatus.NotInstalled
            Next
            For Each RelativeDir In ModInfo.RootDirsToLink
                If Not Directory.Exists(Path.Combine(InstallDir, RelativeDir)) Then Return OmniMixModInstallStatus.NotInstalled
            Next
            Return If(NeedsVersionUpdate(GamePath, ModInfo), OmniMixModInstallStatus.NeedsUpdate, OmniMixModInstallStatus.Installed)
        End If

        If Directory.Exists(Path.Combine(GamePath, "BepInEx", "plugins", ModInfo.FolderName)) Then
            Return If(NeedsVersionUpdate(GamePath, ModInfo), OmniMixModInstallStatus.NeedsUpdate, OmniMixModInstallStatus.Installed)
        End If
        Return OmniMixModInstallStatus.NotInstalled
    End Function

    Public Function GetGameVersion(GamePath As String, Game As OmniMixGameDeclaration) As String
        If Game Is Nothing OrElse Not String.Equals(Game.Id, "forza_horizon_6", StringComparison.OrdinalIgnoreCase) Then Return "1.0.0"
        Try
            Dim Layout = ResolveGameInstallLayout(GamePath, Game)
            Dim ConfigDir = If(Layout.IsValid, Layout.ConfigDir, GamePath)
            Dim ConfigPath = Path.Combine(ConfigDir, "MicrosoftGame.Config")
            If Not File.Exists(ConfigPath) Then Return "0.0.0.0"
            Dim Match = Text.RegularExpressions.Regex.Match(File.ReadAllText(ConfigPath), "\bVersion=""([^""]+)""", Text.RegularExpressions.RegexOptions.IgnoreCase)
            If Match.Success Then Return Match.Groups(1).Value
        Catch
        End Try
        Return "0.0.0.0"
    End Function

    Public Function NeedsVersionUpdate(GamePath As String, ModInfo As OmniMixModDeclaration) As Boolean
        If ModInfo Is Nothing OrElse String.IsNullOrWhiteSpace(ModInfo.Version) Then Return False
        Dim InstalledVersion = ReadManagedMarker(ResolveGameMarkerDir(GamePath, ModInfo), ModInfo.Id)
        If String.IsNullOrWhiteSpace(InstalledVersion) Then InstalledVersion = GetInstalledVersion(ModInfo.Id)
        Return Not String.IsNullOrWhiteSpace(InstalledVersion) AndAlso
            Not String.Equals(InstalledVersion, ModInfo.Version, StringComparison.OrdinalIgnoreCase)
    End Function

    Public Function IsPackageAvailable(ArchiveName As String) As Boolean
        Return Not String.IsNullOrWhiteSpace(ResolveAssetPath(ArchiveName))
    End Function

    Public Function ResolveAssetPath(ArchiveName As String) As String
        If String.IsNullOrWhiteSpace(ArchiveName) Then Return ""

        Dim BaseDir = PathExeFolder
        Dim Candidates As New List(Of String) From {
            Path.Combine(BaseDir, "OmniMixAssets", ArchiveName),
            Path.Combine(BaseDir, "assets", ArchiveName),
            Path.Combine(BaseDir, "data", "flutter_assets", "assets", ArchiveName),
            Path.Combine(BaseDir, "wwwroot", "assets", "assets", ArchiveName),
            Path.Combine(BaseDir, "wwwroot", "assets", ArchiveName),
            Path.Combine(BaseDir, ArchiveName)
        }

        Try
            Candidates.Add(Path.GetFullPath(Path.Combine(BaseDir, "..", "data", "flutter_assets", "assets", ArchiveName)))
            Candidates.Add(Path.GetFullPath(Path.Combine(BaseDir, "..", "wwwroot", "assets", "assets", ArchiveName)))
            Candidates.Add(Path.GetFullPath(Path.Combine(BaseDir, "..", "..", "..", "..", "..", "gui_flutter", "assets", ArchiveName)))
            Candidates.Add(Path.GetFullPath(Path.Combine(BaseDir, "..", "..", "..", "..", "..", "..", "gui_flutter", "assets", ArchiveName)))
        Catch
        End Try

        For Each Candidate In Candidates
            Try
                If File.Exists(Candidate) Then Return Candidate
            Catch
            End Try
        Next
        Return ""
    End Function

    Public Function DeployBepInEx(GamePath As String, Logs As List(Of String)) As Boolean
        Dim Framework = GetBepInExFramework()
        Try
            AddLog(Logs, "Starting BepInEx deployment...")
            If String.IsNullOrWhiteSpace(GamePath) OrElse Not Directory.Exists(GamePath) Then
                AddLog(Logs, "ERROR invalid game directory.")
                Return False
            End If

            Dim ArchivePath = ResolveAssetPath(Framework.ArchiveName)
            If String.IsNullOrWhiteSpace(ArchivePath) Then
                AddLog(Logs, "ERROR missing asset: " & Framework.ArchiveName)
                Return False
            End If

            Dim ExtractPath = Path.Combine(ManagerDir, "bepinex_core")
            ResetDirectory(ExtractPath)
            AddLog(Logs, "Extracting loader to local cache...")
            ZipFile.ExtractToDirectory(ArchivePath, ExtractPath, True)

            For Each RelativeDir In Framework.DirsToCreate
                Directory.CreateDirectory(Path.Combine(GamePath, RelativeDir))
                AddLog(Logs, "  Ensured directory: " & RelativeDir)
            Next

            For Each RelativeDir In Framework.DirsToLink
                Dim LinkPath = Path.Combine(GamePath, RelativeDir)
                Dim TargetPath = Path.Combine(ExtractPath, RelativeDir)
                If Not Directory.Exists(TargetPath) Then
                    AddLog(Logs, "ERROR missing extracted directory: " & RelativeDir)
                    Return False
                End If
                If Not CreateDirectoryLinkOrCopy(LinkPath, TargetPath) Then
                    AddLog(Logs, "ERROR placing directory: " & RelativeDir)
                    Return False
                End If
                AddLog(Logs, "  Placed directory: " & RelativeDir)
            Next

            For Each RelativeFile In Framework.FilesToLink
                Dim LinkPath = Path.Combine(GamePath, RelativeFile)
                Dim TargetPath = Path.Combine(ExtractPath, RelativeFile)
                If Not File.Exists(TargetPath) Then
                    AddLog(Logs, "ERROR missing extracted file: " & RelativeFile)
                    Return False
                End If
                If Not CreateFileLinkOrCopy(LinkPath, TargetPath) Then
                    AddLog(Logs, "ERROR placing file: " & RelativeFile)
                    Return False
                End If
                AddLog(Logs, "  Placed file: " & RelativeFile)
            Next

            Dim MarkerPath = Path.Combine(GamePath, "BepInEx", ".omnimix_managed")
            Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath))
            File.WriteAllText(MarkerPath, Framework.Version, Encoding.UTF8)
            RecordInstalledVersion(Framework.Id, Framework.Version)
            AddLog(Logs, "BepInEx deployment completed.")
            Return True
        Catch Ex As Exception
            AddLog(Logs, "ERROR deploying BepInEx: " & Ex.Message)
            Return False
        End Try
    End Function

    Public Function UndeployBepInEx(GamePath As String, Logs As List(Of String)) As Boolean
        Dim Framework = GetBepInExFramework()
        Try
            AddLog(Logs, "Starting BepInEx undeployment...")
            For Each RelativeFile In Framework.FilesToLink
                DeletePathSafely(Path.Combine(GamePath, RelativeFile))
                AddLog(Logs, "  Removed file: " & RelativeFile)
            Next
            For Each RelativeDir In Framework.DirsToLink
                DeletePathSafely(Path.Combine(GamePath, RelativeDir))
                AddLog(Logs, "  Removed directory: " & RelativeDir)
            Next

            Dim MarkerPath = Path.Combine(GamePath, "BepInEx", ".omnimix_managed")
            If File.Exists(MarkerPath) Then File.Delete(MarkerPath)
            RemoveVersionRecord(Framework.Id)

            DeleteDirectoryIfEmpty(Path.Combine(GamePath, "BepInEx", "plugins"), Logs)
            DeleteDirectoryIfEmpty(Path.Combine(GamePath, "BepInEx", "patchers"), Logs)
            DeleteDirectoryIfEmpty(Path.Combine(GamePath, "BepInEx"), Logs)
            AddLog(Logs, "BepInEx undeployment completed.")
            Return True
        Catch Ex As Exception
            AddLog(Logs, "ERROR undeploying BepInEx: " & Ex.Message)
            Return False
        End Try
    End Function

    Public Function DeployMod(GamePath As String, ModInfo As OmniMixModDeclaration, BackendPort As Integer, Logs As List(Of String)) As String
        Try
            If ModInfo Is Nothing Then Return ""
            AddLog(Logs, "Starting " & ModInfo.Name & " deployment...")

            Dim ArchivePath = ResolveAssetPath(ModInfo.ArchiveName)
            If String.IsNullOrWhiteSpace(ArchivePath) Then
                AddLog(Logs, "ERROR missing asset: " & ModInfo.ArchiveName)
                Return ""
            End If

            Dim ExtractPath = If(ModInfo.InstallsToGameRoot,
                Path.Combine(ManagerDir, "root_mods", ModInfo.Id),
                Path.Combine(ManagerDir, "mods", ModInfo.FolderName))
            ResetDirectory(ExtractPath)
            ZipFile.ExtractToDirectory(ArchivePath, ExtractPath, True)
            AddLog(Logs, "Extracted package to local cache.")

            If ModInfo.InstallsToGameRoot Then
                If Not DeployRootModFiles(GamePath, ModInfo, ExtractPath, Logs) Then Return ""
            Else
                Dim LinkPath = Path.Combine(ResolveRootModInstallDir(GamePath, ModInfo), "BepInEx", "plugins", ModInfo.FolderName)
                If Not CreateDirectoryLinkOrCopy(LinkPath, ExtractPath) Then
                    AddLog(Logs, "ERROR placing plugin directory.")
                    Return ""
                End If
                AddLog(Logs, "  Placed plugin directory: " & ModInfo.FolderName)
            End If
            RecordInstalledVersion(ModInfo.Id, ModInfo.Version)

            Dim InstanceId = GetExpectedInstanceId(GamePath, ModInfo)
            Dim ExistingInstanceId = ReadInstanceId(GamePath, ModInfo)
            If Not String.IsNullOrWhiteSpace(ExistingInstanceId) AndAlso
               Not String.Equals(ExistingInstanceId, InstanceId, StringComparison.OrdinalIgnoreCase) Then
                AddLog(Logs, "  Rewriting stale instance marker: " & ExistingInstanceId & " -> " & InstanceId)
            End If
            WriteInstanceId(GamePath, InstanceId, ModInfo)
            If BackendPort > 0 Then WritePortFile(GamePath, BackendPort, ModInfo)
            AddLog(Logs, ModInfo.Name & " deployment completed. Instance: " & InstanceId)
            Return InstanceId
        Catch Ex As Exception
            AddLog(Logs, "ERROR deploying mod: " & Ex.Message)
            Return ""
        End Try
    End Function

    Public Function UndeployMod(GamePath As String, ModInfo As OmniMixModDeclaration, Logs As List(Of String)) As Boolean
        Try
            If ModInfo Is Nothing Then Return False
            AddLog(Logs, "Starting " & ModInfo.Name & " undeployment...")
            Dim InstallDir = ResolveRootModInstallDir(GamePath, ModInfo)
            Dim MarkerDir = ResolveGameMarkerDir(GamePath, ModInfo)

            ' If media UI was replaced, restore it as part of mod uninstallation
            If CheckMediaUiReplaced(GamePath) Then
                RestoreMediaUi(GamePath, Logs)
            End If

            If ModInfo.InstallsToGameRoot Then
                Dim MarkerPath = Path.Combine(MarkerDir, ".omnimix_mods", ModInfo.Id & ".managed")
                If Not File.Exists(MarkerPath) Then
                    AddLog(Logs, "ERROR this root mod is not managed by OmniMix.")
                    Return False
                End If
                RestoreAndRemoveRootModFiles(GamePath, ModInfo, Logs)
                If File.Exists(MarkerPath) Then File.Delete(MarkerPath)
                DeleteDirectoryIfEmpty(Path.Combine(MarkerDir, ".omnimix_mods"), Logs)
            Else
                DeletePathSafely(Path.Combine(InstallDir, "BepInEx", "plugins", ModInfo.FolderName))
                AddLog(Logs, "  Removed plugin directory: " & ModInfo.FolderName)
            End If

            DeleteInstanceId(GamePath, ModInfo)
            DeletePortFile(GamePath, ModInfo)
            RemoveVersionRecord(ModInfo.Id)
            AddLog(Logs, ModInfo.Name & " undeployment completed.")
            Return True
        Catch Ex As Exception
            AddLog(Logs, "ERROR undeploying mod: " & Ex.Message)
            Return False
        End Try
    End Function

    Public Function ReadInstanceId(GamePath As String, Optional ModInfo As OmniMixModDeclaration = Nothing) As String
        Try
            Dim MarkerDir = If(ModInfo Is Nothing, GamePath, ResolveGameMarkerDir(GamePath, ModInfo))
            Dim MarkerPath = Path.Combine(MarkerDir, InstanceMarkerName)
            If File.Exists(MarkerPath) Then Return CleanInstanceId(File.ReadAllText(MarkerPath, PlainUtf8))
        Catch
        End Try
        Return ""
    End Function

    Public Sub WritePortFile(GamePath As String, BackendPort As Integer, Optional ModInfo As OmniMixModDeclaration = Nothing)
        Try
            Dim MarkerDir = If(ModInfo Is Nothing, GamePath, ResolveGameMarkerDir(GamePath, ModInfo))
            File.WriteAllText(Path.Combine(MarkerDir, PortFileName), BackendPort.ToString(), PlainUtf8)
        Catch
        End Try
    End Sub

    Public Function GetExpectedInstanceId(GamePath As String, ModInfo As OmniMixModDeclaration) As String
        If String.IsNullOrWhiteSpace(GamePath) OrElse ModInfo Is Nothing Then Return ""
        Return GenerateInstanceId(GamePath, ModInfo.Id)
    End Function

    Public Function EnsureRuntimeBinding(GamePath As String, ModInfo As OmniMixModDeclaration, BackendPort As Integer, Logs As List(Of String)) As String
        If String.IsNullOrWhiteSpace(GamePath) OrElse ModInfo Is Nothing Then Return ""
        If CheckModStatus(GamePath, ModInfo) = OmniMixModInstallStatus.NotInstalled Then Return ""

        Dim ExpectedInstanceId = GetExpectedInstanceId(GamePath, ModInfo)
        Dim ExistingInstanceId = ReadInstanceId(GamePath, ModInfo)
        If Not String.Equals(ExistingInstanceId, ExpectedInstanceId, StringComparison.OrdinalIgnoreCase) Then
            WriteInstanceId(GamePath, ExpectedInstanceId, ModInfo)
            AddLog(Logs, "  Fixed instance marker: " & If(String.IsNullOrWhiteSpace(ExistingInstanceId), "(missing)", ExistingInstanceId) & " -> " & ExpectedInstanceId)
        End If
        If BackendPort > 0 Then
            WritePortFile(GamePath, BackendPort, ModInfo)
            AddLog(Logs, "  Updated game port file: " & BackendPort)
        End If
        Return ExpectedInstanceId
    End Function

    Public Sub DeletePortFile(GamePath As String, Optional ModInfo As OmniMixModDeclaration = Nothing)
        Try
            Dim MarkerDir = If(ModInfo Is Nothing, GamePath, ResolveGameMarkerDir(GamePath, ModInfo))
            Dim FilePath = Path.Combine(MarkerDir, PortFileName)
            If File.Exists(FilePath) Then File.Delete(FilePath)
        Catch
        End Try
    End Sub

    Private Function ReadGamePaths() As Dictionary(Of String, String)
        Try
            If File.Exists(PathsDbPath) Then
                Dim Result = JsonConvert.DeserializeObject(Of Dictionary(Of String, String))(File.ReadAllText(PathsDbPath, Encoding.UTF8))
                If Result IsNot Nothing Then Return Result
            End If
        Catch
        End Try
        Return New Dictionary(Of String, String)
    End Function

    Private Function ReadInstalledVersions() As Dictionary(Of String, String)
        Try
            If File.Exists(InstalledVersionsDbPath) Then
                Dim Result = JsonConvert.DeserializeObject(Of Dictionary(Of String, String))(File.ReadAllText(InstalledVersionsDbPath, Encoding.UTF8))
                If Result IsNot Nothing Then Return New Dictionary(Of String, String)(Result, StringComparer.OrdinalIgnoreCase)
            End If
        Catch
        End Try
        Return New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
    End Function

    Private Sub RecordInstalledVersion(Id As String, Version As String)
        Try
            Dim Versions = ReadInstalledVersions()
            Versions(Id) = Version
            Directory.CreateDirectory(ManagerDir)
            File.WriteAllText(InstalledVersionsDbPath, JsonConvert.SerializeObject(Versions, JsonSettings), PlainUtf8)
        Catch
        End Try
    End Sub

    Private Sub RemoveVersionRecord(Id As String)
        Try
            Dim Versions = ReadInstalledVersions()
            If Not Versions.Remove(Id) Then Return
            Directory.CreateDirectory(ManagerDir)
            File.WriteAllText(InstalledVersionsDbPath, JsonConvert.SerializeObject(Versions, JsonSettings), PlainUtf8)
        Catch
        End Try
    End Sub

    Private Function GetInstalledVersion(Id As String) As String
        Try
            Dim Versions = ReadInstalledVersions()
            If Versions.ContainsKey(Id) Then Return Versions(Id)
        Catch
        End Try
        Return ""
    End Function

    Private Function ReadManagedMarker(GamePath As String, ModId As String) As String
        Return ReadTextFile(Path.Combine(GamePath, ".omnimix_mods", ModId & ".managed"))
    End Function

    Private Function ReadTextFile(FilePath As String) As String
        Try
            If File.Exists(FilePath) Then Return File.ReadAllText(FilePath, PlainUtf8).Trim()
        Catch
        End Try
        Return ""
    End Function

    Private Sub ResetDirectory(DirectoryPath As String)
        If Directory.Exists(DirectoryPath) Then Directory.Delete(DirectoryPath, True)
        Directory.CreateDirectory(DirectoryPath)
    End Sub

    Private Function DeployRootModFiles(GamePath As String, ModInfo As OmniMixModDeclaration, ExtractPath As String, Logs As List(Of String)) As Boolean
        Dim InstallDir = ResolveRootModInstallDir(GamePath, ModInfo)
        Dim MarkerDir = ResolveGameMarkerDir(GamePath, ModInfo)
        If String.IsNullOrWhiteSpace(InstallDir) OrElse Not Directory.Exists(InstallDir) Then
            AddLog(Logs, "ERROR invalid root mod install directory.")
            Return False
        End If
        If Not String.Equals(InstallDir, GamePath, StringComparison.OrdinalIgnoreCase) Then
            AddLog(Logs, "  Detected nested runtime directory: " & InstallDir)
        End If

        Dim MarkerPath = Path.Combine(MarkerDir, ".omnimix_mods", ModInfo.Id & ".managed")
        Dim WasManaged = File.Exists(MarkerPath)
        Dim BackupDir = GetRootModBackupDir(GamePath, ModInfo)
        Directory.CreateDirectory(BackupDir)

        For Each RelativeFile In ModInfo.RootFilesToLink
            Dim TargetPath = Path.Combine(ExtractPath, RelativeFile)
            Dim LinkPath = Path.Combine(InstallDir, RelativeFile)
            Dim BackupPath = Path.Combine(BackupDir, RelativeFile)
            If Not File.Exists(TargetPath) Then
                AddLog(Logs, "ERROR missing packaged file: " & RelativeFile)
                Return False
            End If
            If Not WasManaged AndAlso File.Exists(LinkPath) AndAlso
               Not ModInfo.RootFilesNoBackup.Contains(RelativeFile) AndAlso
               Not File.Exists(BackupPath) Then
                Directory.CreateDirectory(Path.GetDirectoryName(BackupPath))
                File.Copy(LinkPath, BackupPath, True)
                AddLog(Logs, "  Backed up existing file: " & RelativeFile)
            End If
            If ModInfo.CopyRootFiles Then
                CreateFileCopy(LinkPath, TargetPath)
            ElseIf Not CreateFileLinkOrCopy(LinkPath, TargetPath) Then
                Return False
            End If
            AddLog(Logs, "  Placed root file: " & RelativeFile)
        Next

        For Each RelativeDir In ModInfo.RootDirsToLink
            Dim TargetPath = Path.Combine(ExtractPath, RelativeDir)
            Dim LinkPath = Path.Combine(InstallDir, RelativeDir)
            Dim BackupPath = Path.Combine(BackupDir, RelativeDir)
            If Not Directory.Exists(TargetPath) Then
                AddLog(Logs, "ERROR missing packaged directory: " & RelativeDir)
                Return False
            End If
            If Not WasManaged AndAlso Directory.Exists(LinkPath) AndAlso Not Directory.Exists(BackupPath) Then
                Directory.CreateDirectory(Path.GetDirectoryName(BackupPath))
                Directory.Move(LinkPath, BackupPath)
                AddLog(Logs, "  Backed up existing directory: " & RelativeDir)
            End If
            If Not CreateDirectoryLinkOrCopy(LinkPath, TargetPath) Then Return False
            AddLog(Logs, "  Placed root directory: " & RelativeDir)
        Next

        Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath))
        File.WriteAllText(MarkerPath, ModInfo.Version, Encoding.UTF8)
        Return True
    End Function

    Private Sub RestoreAndRemoveRootModFiles(GamePath As String, ModInfo As OmniMixModDeclaration, Logs As List(Of String))
        Dim InstallDir = ResolveRootModInstallDir(GamePath, ModInfo)
        Dim BackupDir = GetRootModBackupDir(GamePath, ModInfo)
        Dim LegacyBackupDir = Path.Combine(GamePath, ".omnimix_backup", ModInfo.Id)
        If Not Directory.Exists(BackupDir) AndAlso Directory.Exists(LegacyBackupDir) Then BackupDir = LegacyBackupDir
        For Each RelativeFile In ModInfo.RootFilesToLink
            Dim LinkPath = Path.Combine(InstallDir, RelativeFile)
            Dim BackupPath = Path.Combine(BackupDir, RelativeFile)
            DeletePathSafely(LinkPath)
            If File.Exists(BackupPath) Then
                Directory.CreateDirectory(Path.GetDirectoryName(LinkPath))
                File.Copy(BackupPath, LinkPath, True)
                File.Delete(BackupPath)
                AddLog(Logs, "  Restored backup file: " & RelativeFile)
            Else
                AddLog(Logs, "  Removed root file: " & RelativeFile)
            End If
        Next

        For Each RelativeDir In ModInfo.RootDirsToLink
            Dim LinkPath = Path.Combine(InstallDir, RelativeDir)
            Dim BackupPath = Path.Combine(BackupDir, RelativeDir)
            DeletePathSafely(LinkPath)
            If Directory.Exists(BackupPath) Then
                Directory.Move(BackupPath, LinkPath)
                AddLog(Logs, "  Restored backup directory: " & RelativeDir)
            Else
                AddLog(Logs, "  Removed root directory: " & RelativeDir)
            End If
        Next

        DeleteDirectoryIfEmpty(BackupDir, Logs)
        DeleteDirectoryIfEmpty(Path.Combine(GamePath, ".omnimix_backup", ModInfo.Id), Logs)
        DeleteDirectoryIfEmpty(Path.Combine(GamePath, ".omnimix_backup"), Logs)
    End Sub

    Private Function GetRootModBackupDir(GamePath As String, ModInfo As OmniMixModDeclaration) As String
        Dim Game = GetGameCatalog().FirstOrDefault(Function(Item) Item.SupportedMods.Contains(ModInfo.Id))
        Dim GameVersion = GetGameVersion(GamePath, Game)
        Return Path.Combine(GamePath, ".omnimix_backup", ModInfo.Id, "v" & SanitizePathSegment(GameVersion))
    End Function

    Private Function SanitizePathSegment(Value As String) As String
        Dim Result = If(String.IsNullOrWhiteSpace(Value), "unknown", Value.Trim())
        For Each InvalidCharacter In Path.GetInvalidFileNameChars()
            Result = Result.Replace(InvalidCharacter, "_"c)
        Next
        Return Result
    End Function

    Private Function CreateFileLinkOrCopy(LinkPath As String, TargetPath As String) As Boolean
        DeletePathSafely(LinkPath)
        Directory.CreateDirectory(Path.GetDirectoryName(LinkPath))
        If TryCreateSymlink(LinkPath, TargetPath, False) Then Return True
        File.Copy(TargetPath, LinkPath, True)
        Return True
    End Function

    Private Sub CreateFileCopy(LinkPath As String, TargetPath As String)
        DeletePathSafely(LinkPath)
        Directory.CreateDirectory(Path.GetDirectoryName(LinkPath))
        File.Copy(TargetPath, LinkPath, True)
    End Sub

    Private Function CreateDirectoryLinkOrCopy(LinkPath As String, TargetPath As String) As Boolean
        DeletePathSafely(LinkPath)
        Directory.CreateDirectory(Path.GetDirectoryName(LinkPath))
        If TryCreateSymlink(LinkPath, TargetPath, True) Then Return True
        CopyDirectory(TargetPath, LinkPath)
        Return True
    End Function

    Private Function TryCreateSymlink(LinkPath As String, TargetPath As String, IsDirectory As Boolean) As Boolean
        Try
            Dim Arguments = If(IsDirectory, "/c mklink /D ", "/c mklink ") &
                Quote(LinkPath) & " " & Quote(TargetPath)
            Using Proc = Process.Start(New ProcessStartInfo With {
                .FileName = "cmd.exe",
                .Arguments = Arguments,
                .UseShellExecute = False,
                .CreateNoWindow = True,
                .WindowStyle = ProcessWindowStyle.Hidden
            })
                Proc.WaitForExit()
                Return Proc.ExitCode = 0
            End Using
        Catch
            Return False
        End Try
    End Function

    Private Sub DeletePathSafely(TargetPath As String)
        Try
            If String.IsNullOrWhiteSpace(TargetPath) Then Return
            If File.Exists(TargetPath) Then
                File.SetAttributes(TargetPath, FileAttributes.Normal)
                File.Delete(TargetPath)
                Return
            End If
            If Directory.Exists(TargetPath) Then
                Dim Attrs = File.GetAttributes(TargetPath)
                If (Attrs And FileAttributes.ReparsePoint) = FileAttributes.ReparsePoint Then
                    Directory.Delete(TargetPath)
                Else
                    Directory.Delete(TargetPath, True)
                End If
            End If
        Catch
        End Try
    End Sub

    Private Sub CopyDirectory(SourceDir As String, DestinationDir As String)
        Directory.CreateDirectory(DestinationDir)
        For Each SourceFile In Directory.GetFiles(SourceDir)
            File.Copy(SourceFile, Path.Combine(DestinationDir, Path.GetFileName(SourceFile)), True)
        Next
        For Each SourceSubDir In Directory.GetDirectories(SourceDir)
            CopyDirectory(SourceSubDir, Path.Combine(DestinationDir, Path.GetFileName(SourceSubDir)))
        Next
    End Sub

    Private Sub DeleteDirectoryIfEmpty(DirectoryPath As String, Logs As List(Of String))
        Try
            If Directory.Exists(DirectoryPath) AndAlso Not Directory.EnumerateFileSystemEntries(DirectoryPath).Any() Then
                Directory.Delete(DirectoryPath)
                AddLog(Logs, "  Deleted empty directory: " & DirectoryPath)
            End If
        Catch
        End Try
    End Sub

    Private Sub WriteInstanceId(GamePath As String, InstanceId As String, ModInfo As OmniMixModDeclaration)
        Try
            File.WriteAllText(Path.Combine(ResolveGameMarkerDir(GamePath, ModInfo), InstanceMarkerName), CleanInstanceId(InstanceId), PlainUtf8)
        Catch
        End Try
    End Sub

    Private Sub DeleteInstanceId(GamePath As String, ModInfo As OmniMixModDeclaration)
        Try
            Dim MarkerPath = Path.Combine(ResolveGameMarkerDir(GamePath, ModInfo), InstanceMarkerName)
            If File.Exists(MarkerPath) Then File.Delete(MarkerPath)
        Catch
        End Try
    End Sub

    Private Function GenerateInstanceId(GamePath As String, ModId As String) As String
        If String.IsNullOrWhiteSpace(GamePath) Then Return ""
        Dim Prefix = If(String.IsNullOrWhiteSpace(ModId), "omnimix", ModId.Trim().ToLowerInvariant())
        Dim Seed = Prefix & "|" & Path.GetFullPath(If(GamePath, "")).TrimEnd("\"c).ToLowerInvariant()
        Using Sha = SHA256.Create()
            Dim Hash = Sha.ComputeHash(Encoding.UTF8.GetBytes(Seed))
            Dim Token = BitConverter.ToString(Hash).Replace("-", "").ToLowerInvariant().Substring(0, 12)
            Return Prefix & "_" & Token
        End Using
    End Function

    Private Function CleanInstanceId(Value As String) As String
        If String.IsNullOrWhiteSpace(Value) Then Return ""
        Dim Raw = Value.Trim().Trim(ChrW(&HFEFF), ChrW(0))
        Dim Builder As New StringBuilder(Raw.Length)
        For Each Character In Raw
            If (Character >= "a"c AndAlso Character <= "z"c) OrElse
               (Character >= "A"c AndAlso Character <= "Z"c) OrElse
               (Character >= "0"c AndAlso Character <= "9"c) OrElse
               Character = "_"c OrElse Character = "-"c OrElse Character = "."c Then
                Builder.Append(Character)
            End If
        Next
        Return Builder.ToString()
    End Function

    Private Function Quote(Value As String) As String
        Return """" & Value & """"
    End Function

    Private Sub AddLog(Logs As List(Of String), Message As String)
        If Logs Is Nothing Then Return
        Logs.Add("[" & Date.Now.ToString("HH:mm:ss") & "] " & Message)
    End Sub

    Public Function ResolveMediaGenPath() As String
        Dim Candidates = New List(Of String) From {
            Path.Combine(PathExeFolder, "chill-gen-media.exe"),
            Path.Combine(PathExeFolder, "OmniMixAssets", "chill-gen-media.exe"),
            Path.Combine(PathExeFolder, "assets", "chill-gen-media.exe")
        }
        For Each Candidate In Candidates
            Try
                If File.Exists(Candidate) Then Return Candidate
            Catch
            End Try
        Next
        Return ""
    End Function

    Private Function ResolveMediaGenConfigPath(MediaGenPath As String) As String
        Dim Candidates As New List(Of String)
        Try
            Dim MediaGenDir = Path.GetDirectoryName(MediaGenPath)
            If Not String.IsNullOrWhiteSpace(MediaGenDir) Then Candidates.Add(Path.Combine(MediaGenDir, "config.json"))
        Catch
        End Try
        Candidates.Add(Path.Combine(PathExeFolder, "config.json"))
        Candidates.Add(Path.Combine(PathExeFolder, "OmniMixAssets", "config.json"))
        Candidates.Add(Path.Combine(PathExeFolder, "assets", "config.json"))

        For Each Candidate In Candidates
            Try
                If File.Exists(Candidate) Then Return Candidate
            Catch
            End Try
        Next
        Return ""
    End Function

    Private Function PrepareMediaGenConfig(MediaGenPath As String, TempOutputDir As String, RadioDisplayName As String, RadioArtist As String, Logs As List(Of String)) As String
        Dim SourceConfigPath = ResolveMediaGenConfigPath(MediaGenPath)
        If String.IsNullOrWhiteSpace(SourceConfigPath) Then
            AddLog(Logs, "错误：未找到媒体生成器配置 config.json。")
            Return ""
        End If

        Try
            Dim Root = JObject.Parse(File.ReadAllText(SourceConfigPath, Encoding.UTF8))
            Dim RadioInfo = TryCast(Root("radioInfo"), JObject)
            If RadioInfo Is Nothing Then
                RadioInfo = New JObject()
                Root("radioInfo") = RadioInfo
            End If

            If Not String.IsNullOrWhiteSpace(RadioDisplayName) Then RadioInfo("displayName") = RadioDisplayName.Trim()
            If Not String.IsNullOrWhiteSpace(RadioArtist) Then RadioInfo("artist") = RadioArtist.Trim()

            Dim TempConfigPath = Path.Combine(TempOutputDir, "config.json")
            File.WriteAllText(TempConfigPath, Root.ToString(Formatting.Indented), PlainUtf8)
            AddLog(Logs, "已写入电台显示名：" & If(String.IsNullOrWhiteSpace(RadioDisplayName), RadioInfo("displayName")?.ToString(), RadioDisplayName.Trim()))
            Return TempConfigPath
        Catch Ex As Exception
            AddLog(Logs, "错误：生成临时媒体配置失败：" & Ex.Message)
            Return ""
        End Try
    End Function

    Public Function CheckMediaUiReplaced(GamePath As String) As Boolean
        If String.IsNullOrWhiteSpace(GamePath) Then Return False
        Try
            Dim MarkerPath = Path.Combine(ResolveMediaRoot(GamePath), ".omnimix_mods", "media_ui_replaced.managed")
            Return File.Exists(MarkerPath)
        Catch
            Return False
        End Try
    End Function

    Public Function DeployMediaUiReplacement(GamePath As String, PngPath As String, Logs As List(Of String), Optional RadioDisplayName As String = "", Optional RadioArtist As String = "") As Boolean
        Try
            AddLog(Logs, "开始执行电台 UI 替换...")
            If String.IsNullOrWhiteSpace(GamePath) OrElse Not Directory.Exists(GamePath) Then
                AddLog(Logs, "错误：无效的游戏目录。")
                Return False
            End If
            Dim MediaRoot = ResolveMediaRoot(GamePath)
            Dim MediaDirName = GetMediaDirectoryName(MediaRoot)
            If String.IsNullOrWhiteSpace(MediaRoot) OrElse Not Directory.Exists(MediaRoot) Then
                AddLog(Logs, "错误：无法识别游戏媒体目录。")
                Return False
            End If

            Dim MediaGenPath = ResolveMediaGenPath()
            If String.IsNullOrWhiteSpace(MediaGenPath) Then
                AddLog(Logs, "错误：未找到媒体生成器 chill-gen-media.exe。")
                Return False
            End If
            AddLog(Logs, "已找到媒体生成器：" & MediaGenPath)

            Dim TempOutputDir = Path.Combine(ManagerDir, "media_temp")
            If Directory.Exists(TempOutputDir) Then
                Directory.Delete(TempOutputDir, True)
            End If
            Directory.CreateDirectory(TempOutputDir)

            Dim ConfigPath = PrepareMediaGenConfig(MediaGenPath, TempOutputDir, RadioDisplayName, RadioArtist, Logs)
            If String.IsNullOrWhiteSpace(ConfigPath) Then Return False

            AddLog(Logs, "正在生成修改后的电台媒体资源...")
            Dim Arguments = $"-c {Quote(ConfigPath)} -g {Quote(MediaRoot)} -o {Quote(TempOutputDir)} -r {Quote(PngPath)}"
            Using Proc = New Process()
                Proc.StartInfo = New ProcessStartInfo With {
                    .FileName = MediaGenPath,
                    .Arguments = Arguments,
                    .UseShellExecute = False,
                    .RedirectStandardOutput = True,
                    .RedirectStandardError = True,
                    .CreateNoWindow = True
                }

                Proc.Start()

                Dim OutputReader = Proc.StandardOutput
                Dim ErrorReader = Proc.StandardError

                While Not Proc.HasExited
                    Dim Line = OutputReader.ReadLine()
                    If Line IsNot Nothing Then
                        AddLog(Logs, "生成器: " & Line.Trim())
                    End If
                    Dim ErrLine = ErrorReader.ReadLine()
                    If ErrLine IsNot Nothing Then
                        AddLog(Logs, "生成器 [错误]: " & ErrLine.Trim())
                    End If
                    System.Threading.Thread.Sleep(50)
                End While

                While Not OutputReader.EndOfStream
                    Dim Line = OutputReader.ReadLine()
                    If Line IsNot Nothing Then AddLog(Logs, "生成器: " & Line.Trim())
                End While
                While Not ErrorReader.EndOfStream
                    Dim Line = ErrorReader.ReadLine()
                    If Line IsNot Nothing Then AddLog(Logs, "生成器 [错误]: " & Line.Trim())
                End While

                Proc.WaitForExit()
                If Proc.ExitCode <> 0 Then
                    AddLog(Logs, "错误：媒体资源生成失败，退出码：" & Proc.ExitCode)
                    Return False
                End If
            End Using

            AddLog(Logs, "电台媒体资源生成成功。正在备份游戏原始文件...")
            Dim BackupDir = Path.Combine(MediaRoot, ".omnimix_backup", "media_original")
            Directory.CreateDirectory(BackupDir)

            Dim FilesToBackup As New List(Of String) From {
                Path.Combine(MediaDirName, "UI", "Textures", "Anthem.zip"),
                Path.Combine(MediaDirName, "UI", "Textures", "HiRes", "Anthem.zip")
            }
            Dim Locales = {"BR", "CN", "DE", "EN", "ES", "IT", "JP", "KO", "MX", "TW"}
            For Each Locale In Locales
                FilesToBackup.Add(Path.Combine(MediaDirName, "Audio", $"RadioInfo_{Locale}.xml"))
            Next

            For Each RelativePath In FilesToBackup
                Dim GameFile = Path.Combine(MediaRoot, RelativePath)
                Dim BackupFile = Path.Combine(BackupDir, RelativePath)

                If File.Exists(GameFile) Then
                    If Not File.Exists(BackupFile) Then
                        Directory.CreateDirectory(Path.GetDirectoryName(BackupFile))
                        File.Copy(GameFile, BackupFile, True)
                        AddLog(Logs, "  已备份：" & RelativePath)
                    End If
                End If
            Next

            AddLog(Logs, "正在应用修改后的媒体文件...")
            Dim TempMediaDir = Path.Combine(TempOutputDir, "media")
            If Not Directory.Exists(TempMediaDir) Then
                AddLog(Logs, "错误：生成结果中缺少 media 目录。")
                Return False
            End If

            CopyDirectory(TempMediaDir, Path.Combine(MediaRoot, MediaDirName))
            AddLog(Logs, "应用成功。")

            Dim MarkerPath = Path.Combine(MediaRoot, ".omnimix_mods", "media_ui_replaced.managed")
            Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath))
            File.WriteAllText(MarkerPath, "replaced", Encoding.UTF8)

            AddLog(Logs, "电台 UI 替换成功完成！请重新启动游戏以加载新界面。")
            Return True
        Catch Ex As Exception
            AddLog(Logs, "错误：执行电台 UI 替换时发生异常：" & Ex.Message)
            Return False
        End Try
    End Function

    Public Function RestoreMediaUi(GamePath As String, Logs As List(Of String)) As Boolean
        Try
            AddLog(Logs, "开始还原电台 UI 为原始文件...")
            If String.IsNullOrWhiteSpace(GamePath) OrElse Not Directory.Exists(GamePath) Then
                AddLog(Logs, "错误：无效的游戏目录。")
                Return False
            End If
            Dim MediaRoot = ResolveMediaRoot(GamePath)

            Dim BackupDir = Path.Combine(MediaRoot, ".omnimix_backup", "media_original")
            If Not Directory.Exists(BackupDir) Then
                AddLog(Logs, "未找到原始备份，将尝试直接清理生成的文件。")
            Else
                Dim BackupFiles = Directory.GetFiles(BackupDir, "*", SearchOption.AllDirectories)
                For Each BackupFile In BackupFiles
                    Dim Relative = BackupFile.Substring(BackupDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    Dim GameFile = Path.Combine(MediaRoot, Relative)

                    Directory.CreateDirectory(Path.GetDirectoryName(GameFile))
                    File.Copy(BackupFile, GameFile, True)
                    AddLog(Logs, "  已还原：" & Relative)
                Next

                Directory.Delete(BackupDir, True)
                AddLog(Logs, "原始备份目录已清理。")
            End If

            Dim MarkerPath = Path.Combine(MediaRoot, ".omnimix_mods", "media_ui_replaced.managed")
            If File.Exists(MarkerPath) Then File.Delete(MarkerPath)
            DeleteDirectoryIfEmpty(Path.Combine(MediaRoot, ".omnimix_mods"), Logs)

            AddLog(Logs, "电台 UI 还原成功完成！")
            Return True
        Catch Ex As Exception
            AddLog(Logs, "错误：还原电台 UI 时发生异常：" & Ex.Message)
            Return False
        End Try
    End Function

    Public Sub WriteFh6RaceStartPlaybackConfig(GamePath As String, ConfigValue As String)
        If String.IsNullOrWhiteSpace(GamePath) OrElse Not Directory.Exists(GamePath) Then Return
        Try
            Dim ConfigDir = Path.Combine(GamePath, "fh6-omnimix")
            If Not Directory.Exists(ConfigDir) Then Directory.CreateDirectory(ConfigDir)
            File.WriteAllText(Path.Combine(ConfigDir, "race_start_playback.txt"), ConfigValue, Encoding.UTF8)
            Logger.Info("已成功写入 FH6 比赛播放行为配置: " & ConfigValue)
        Catch Ex As Exception
            Logger.Warn(Ex, "写入 FH6 比赛播放行为配置异常")
        End Try
    End Sub

End Module
