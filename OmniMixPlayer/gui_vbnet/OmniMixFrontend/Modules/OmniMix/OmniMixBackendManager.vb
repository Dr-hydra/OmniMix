Imports System.Diagnostics
Imports System.IO

Public Module OmniMixBackendManager

    Private Const BackendExeName As String = "OmniMixPlayer.Backend.exe"
    Public Async Function EnsureStartedAsync() As Task(Of OmniMixBackendStatus)
        Dim BackendPath = FindBackendExe()
        If Not String.IsNullOrWhiteSpace(BackendPath) Then
            Try
                Await OmniMixPlatformService.UpdateServiceBinaryPathAsync(BackendPath)
            Catch ex As Exception
                Logger.Warn(ex, "同步 OmniMix 后端服务路径失败，将继续直接发现或启动后端")
            End Try
        End If
        Dim Status = Await OmniMixApiClient.DiscoverAsync()
        If Status.IsOnline Then
            Await SyncCacheSettingsAsync(Status.BaseUrl)
            Status.Message = "已发现正在运行的 OmniMix 后端。"
            Return Status
        End If

StartBundledBackend:
        If String.IsNullOrWhiteSpace(BackendPath) Then
            Return New OmniMixBackendStatus With {
                .IsOnline = False,
                .Message = "未找到 OmniMixPlayer.Backend.exe，无法自动启动后端。"
            }
        End If

        Try
            Dim GuiDir = PathExeFolder.TrimEnd("\"c, "/"c)
            Dim StartInfo = New ProcessStartInfo With {
                .FileName = BackendPath,
                .Arguments = $"--port-file-dir=""{GuiDir}""",
                .WorkingDirectory = Path.GetDirectoryName(BackendPath),
                .UseShellExecute = False,
                .CreateNoWindow = True,
                .WindowStyle = ProcessWindowStyle.Hidden
            }
            StartInfo.EnvironmentVariables("OMNIMIX_CACHE_ROOT") = GetOmniMixCacheRoot().TrimEnd("\"c, "/"c)
            StartInfo.EnvironmentVariables("OMNIMIX_CACHE_MAX_BYTES") = Settings.Get(Of Long)("OmniMixCacheMaxBytes").ToString(Globalization.CultureInfo.InvariantCulture)
            StartProcess(StartInfo)
        Catch ex As Exception
            Return New OmniMixBackendStatus With {
                .IsOnline = False,
                .BackendPath = BackendPath,
                .Message = "启动 OmniMix 后端失败：" & ex.Message
            }
        End Try

        For i = 0 To 39
            Await Task.Delay(500)

            Status = Await OmniMixApiClient.DiscoverAsync()
            If Status.IsOnline Then
                Await SyncCacheSettingsAsync(Status.BaseUrl)
                Status.BackendPath = BackendPath
                Status.StartedBackend = True
                Status.Message = "已启动并连接 OmniMix 后端。"
                Return Status
            End If

            If i > 2 AndAlso i Mod 4 = 3 AndAlso Not IsBackendProcessRunning() Then Exit For
        Next

        Return New OmniMixBackendStatus With {
            .IsOnline = False,
            .BackendPath = BackendPath,
            .Message = "已尝试启动 OmniMix 后端，但 /api/health 未在等待时间内就绪。"
        }
    End Function

    Private Async Function SyncCacheSettingsAsync(BaseUrl As String) As Task
        If String.IsNullOrWhiteSpace(BaseUrl) Then Return
        Try
            Dim Updates As New Dictionary(Of String, Object) From {
                {"cache_root", GetOmniMixCacheRoot().TrimEnd("\"c, "/"c)},
                {"cache_max_bytes", Settings.Get(Of Long)("OmniMixCacheMaxBytes")}
            }
            For Each Pair In GetDjSettingsUpdates()
                Updates(Pair.Key) = Pair.Value
            Next
            Await OmniMixApiClient.PutConfigRawAsync(BaseUrl, Updates)
            Await OmniMixApiClient.SaveConfigAsync(BaseUrl)
        Catch Ex As Exception
            Logger.Warn(Ex, "同步 OmniMix 全局缓存配置失败")
        End Try
    End Function

    Public Async Function SyncDjSettingsAsync(BaseUrl As String) As Task
        If String.IsNullOrWhiteSpace(BaseUrl) Then Return
        Await OmniMixApiClient.PutConfigRawAsync(BaseUrl, GetDjSettingsUpdates())
        Await OmniMixApiClient.SaveConfigAsync(BaseUrl)
    End Function

    Private Function GetDjSettingsUpdates() As Dictionary(Of String, Object)
        Dim Scope = Settings.Get(Of String)("OmniMixFh6DjScope")
        If String.Equals(Scope, "desktop_instances", StringComparison.OrdinalIgnoreCase) Then
            Scope = "desktop_instances"
        Else
            Scope = "fh6_instances"
        End If

        Dim Content = Settings.Get(Of String)("OmniMixFh6DjContent")
        Select Case If(Content, "").Trim().ToLowerInvariant()
            Case "smart", "chatter", "transition_in", "transition_out"
                Content = Content.Trim().ToLowerInvariant()
            Case Else
                Content = "smart"
        End Select

        Dim Frequency = Settings.Get(Of Integer)("OmniMixFh6DjFrequency")
        If Frequency <> 1 AndAlso Frequency <> 2 AndAlso Frequency <> 3 AndAlso Frequency <> 5 Then Frequency = 1

        Return New Dictionary(Of String, Object) From {
            {"fh6_dj_enabled", Settings.Get(Of Boolean)("OmniMixFh6DjEnabled")},
            {"fh6_dj_host", Math.Clamp(Settings.Get(Of Integer)("OmniMixFh6DjHost"), 1, 9)},
            {"fh6_dj_scope", Scope},
            {"fh6_dj_content", Content},
            {"fh6_dj_frequency", Frequency},
            {"fh6_game_root", OmniMixModDeploymentService.LoadGamePath("forza_horizon_6")}
        }
    End Function

    Public Function FindBackendExe() As String
        Dim LocalBackendPath = GetLocalBackendExe()
        If Not String.IsNullOrWhiteSpace(LocalBackendPath) Then Return LocalBackendPath

        Dim ConfiguredPath = GetConfiguredBackendPath()
        If Not String.IsNullOrWhiteSpace(ConfiguredPath) Then
            Try
                Dim FullConfiguredPath = Path.GetFullPath(ConfiguredPath)
                If File.Exists(FullConfiguredPath) Then Return FullConfiguredPath
            Catch
            End Try
        End If

        Return FindDefaultBackendExe()
    End Function

    Public Function FindDefaultBackendExe() As String
        For Each Candidate In GetBackendExeCandidates()
            Try
                Dim FullPath = Path.GetFullPath(Candidate)
                If File.Exists(FullPath) Then Return FullPath
            Catch
            End Try
        Next
        Return Nothing
    End Function

    Private Function GetLocalBackendExe() As String
        Try
            Dim Candidate = Path.GetFullPath(Path.Combine(PathExeFolder, BackendExeName))
            If File.Exists(Candidate) Then Return Candidate
        Catch
        End Try
        Return Nothing
    End Function

    Public Function GetConfiguredBackendPath() As String
        Try
            Return Settings.Get(Of String)("OmniMixBackendPath")
        Catch
            Return ""
        End Try
    End Function

    Public Sub SetConfiguredBackendPath(BackendPath As String)
        Settings.Set("OmniMixBackendPath", If(BackendPath, "").Trim())
    End Sub

    Private Function GetBackendExeCandidates() As IEnumerable(Of String)
        Dim BaseDir = PathExeFolder
        Return New List(Of String) From {
            Path.Combine(BaseDir, BackendExeName),
            Path.Combine(BaseDir, "Backend", BackendExeName),
            Path.Combine(BaseDir, "OmniMixPlayer.Backend", BackendExeName),
            Path.Combine(BaseDir, "..", "Backend", BackendExeName),
            Path.Combine(BaseDir, "..", "OmniMixPlayer.Backend", BackendExeName),
            Path.Combine(BaseDir, "..", "bin", "Backend", "win-x64", BackendExeName),
            Path.Combine(BaseDir, "..", "..", "..", "..", "..", "bin", "Backend", "win-x64", BackendExeName)
        }
    End Function

    Private Function IsBackendProcessRunning() As Boolean
        Try
            Return Process.GetProcessesByName("OmniMixPlayer.Backend").Any()
        Catch
            Return True
        End Try
    End Function

End Module
