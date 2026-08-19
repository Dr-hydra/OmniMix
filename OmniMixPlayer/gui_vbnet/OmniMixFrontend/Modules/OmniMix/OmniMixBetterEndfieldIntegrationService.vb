Imports System.Diagnostics
Imports Microsoft.Win32
Imports Newtonsoft.Json.Linq

Public Class OmniMixBetterEndfieldRegistration
    Public Property SchemaVersion As Integer
    Public Property Registered As Boolean
    Public Property BackendExe As String = ""
    Public Property ClientId As String = ""
    Public Property BackendVersion As String = ""
    Public Property Valid As Boolean
    Public Property Reason As String = ""
    Public Property ExitCode As Integer
    Public Property ErrorMessage As String = ""

    Public ReadOnly Property CommandSucceeded As Boolean
        Get
            Return ExitCode = 0 AndAlso SchemaVersion = 1
        End Get
    End Property
End Class

Public Module OmniMixBetterEndfieldIntegrationService

    Public Const GameId As String = "better_endfield"
    Private Const UiExeName As String = "BetterEndfield.exe"
    Private ReadOnly CommandTimeout As TimeSpan = TimeSpan.FromSeconds(20)

    Public Function IsValidInstallDirectory(InstallDirectory As String) As Boolean
        If String.IsNullOrWhiteSpace(InstallDirectory) Then Return False
        Try
            Dim FullPath = Path.GetFullPath(InstallDirectory)
            Return File.Exists(Path.Combine(FullPath, UiExeName)) AndAlso
                   File.Exists(Path.Combine(FullPath, "runtime", "BetterEndfield.Host.dll")) AndAlso
                   Directory.Exists(Path.Combine(FullPath, "modules"))
        Catch
            Return False
        End Try
    End Function

    Public Function FindInstallDirectories() As List(Of String)
        Dim Candidates As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        AddCandidate(Candidates, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "BetterEndfield"))
        AddCandidate(Candidates, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BetterEndfield"))
        AddCandidate(Candidates, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "BetterEndfield"))

        For Each Hive In {RegistryHive.CurrentUser, RegistryHive.LocalMachine}
            For Each View In {RegistryView.Registry64, RegistryView.Registry32}
                Try
                    Using BaseKey = RegistryKey.OpenBaseKey(Hive, View)
                        Using Uninstall = BaseKey.OpenSubKey("SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
                            If Uninstall Is Nothing Then Continue For
                            For Each Name In Uninstall.GetSubKeyNames()
                                Using Entry = Uninstall.OpenSubKey(Name)
                                    Dim DisplayName = TryCast(Entry?.GetValue("DisplayName"), String)
                                    If String.IsNullOrWhiteSpace(DisplayName) OrElse
                                       DisplayName.IndexOf("Better Endfield", StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
                                    AddCandidate(Candidates, TryCast(Entry.GetValue("InstallLocation"), String))
                                    Dim DisplayIcon = TryCast(Entry.GetValue("DisplayIcon"), String)
                                    If Not String.IsNullOrWhiteSpace(DisplayIcon) Then
                                        AddCandidate(Candidates, Path.GetDirectoryName(DisplayIcon.Trim().Trim(""""c)))
                                    End If
                                End Using
                            Next
                        End Using
                    End Using
                Catch
                End Try
            Next
        Next

        Return Candidates.Where(AddressOf IsValidInstallDirectory).OrderBy(Function(Item) Item, StringComparer.OrdinalIgnoreCase).ToList()
    End Function

    Public Async Function QueryAsync(InstallDirectory As String) As Task(Of OmniMixBetterEndfieldRegistration)
        Return Await RunCommandAsync(InstallDirectory, {"--query-omnimix-registration"})
    End Function

    Public Async Function RegisterAsync(InstallDirectory As String, BackendExe As String) As Task(Of OmniMixBetterEndfieldRegistration)
        If String.IsNullOrWhiteSpace(BackendExe) Then
            Return Failure("backend_not_found", "未找到 OmniMixPlayer.Backend.exe。")
        End If
        Return Await RunCommandAsync(InstallDirectory, {"--register-omnimix-backend", Path.GetFullPath(BackendExe), "--silent"})
    End Function

    Public Async Function UnregisterAsync(InstallDirectory As String) As Task(Of OmniMixBetterEndfieldRegistration)
        Return Await RunCommandAsync(InstallDirectory, {"--unregister-omnimix", "--silent"})
    End Function

    Private Async Function RunCommandAsync(InstallDirectory As String, Arguments As IEnumerable(Of String)) As Task(Of OmniMixBetterEndfieldRegistration)
        If Not IsValidInstallDirectory(InstallDirectory) Then
            Return Failure("invalid_installation", "所选目录不是有效的 Better Endfield 安装目录。")
        End If

        Try
            Dim StartInfo As New ProcessStartInfo With {
                .FileName = Path.Combine(Path.GetFullPath(InstallDirectory), UiExeName),
                .WorkingDirectory = Path.GetFullPath(InstallDirectory),
                .UseShellExecute = False,
                .CreateNoWindow = True,
                .WindowStyle = ProcessWindowStyle.Hidden,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True,
                .StandardOutputEncoding = Encoding.UTF8,
                .StandardErrorEncoding = Encoding.UTF8
            }
            For Each Argument In Arguments
                StartInfo.ArgumentList.Add(Argument)
            Next

            Using CliProcess As New Process With {.StartInfo = StartInfo}
                If Not CliProcess.Start() Then Return Failure("process_start_failed", "无法启动 BetterEndfield.exe。")
                Dim OutputTask = CliProcess.StandardOutput.ReadToEndAsync()
                Dim ErrorTask = CliProcess.StandardError.ReadToEndAsync()
                Using TimeoutSource As New CancellationTokenSource(CommandTimeout)
                    Try
                        Await CliProcess.WaitForExitAsync(TimeoutSource.Token)
                    Catch Ex As OperationCanceledException
                        Try
                            CliProcess.Kill(True)
                        Catch
                        End Try
                        Return Failure("process_timeout", "Better Endfield 命令在 20 秒内未完成。")
                    End Try
                End Using
                Dim Output = (Await OutputTask).Trim()
                Dim ErrorText = (Await ErrorTask).Trim()
                Return ParseResult(Output, ErrorText, CliProcess.ExitCode)
            End Using
        Catch Ex As Exception
            Return Failure("process_failed", Ex.Message)
        End Try
    End Function

    Private Function ParseResult(Output As String, ErrorText As String, ExitCode As Integer) As OmniMixBetterEndfieldRegistration
        Dim Result As New OmniMixBetterEndfieldRegistration With {.ExitCode = ExitCode}
        Try
            Dim Root = JObject.Parse(Output)
            Result.SchemaVersion = Root.Value(Of Integer?)("schemaVersion").GetValueOrDefault()
            Result.Registered = Root.Value(Of Boolean?)("registered").GetValueOrDefault()
            Result.BackendExe = If(Root.Value(Of String)("backendExe"), "")
            Result.ClientId = If(Root.Value(Of String)("clientId"), "")
            Result.BackendVersion = If(Root.Value(Of String)("backendVersion"), "")
            Result.Valid = Root.Value(Of Boolean?)("valid").GetValueOrDefault()
            Result.Reason = If(Root.Value(Of String)("reason"), If(Root.Value(Of String)("error"), ""))
            Result.ErrorMessage = If(Root.Value(Of String)("message"), ErrorText)
        Catch Ex As Exception
            Result.Reason = "invalid_json"
            Result.ErrorMessage = If(String.IsNullOrWhiteSpace(Output), If(ErrorText, Ex.Message), Output)
        End Try
        If Result.SchemaVersion <> 1 AndAlso String.IsNullOrWhiteSpace(Result.Reason) Then Result.Reason = "unsupported_schema"
        Return Result
    End Function

    Private Function Failure(Reason As String, Message As String) As OmniMixBetterEndfieldRegistration
        Return New OmniMixBetterEndfieldRegistration With {
            .SchemaVersion = 1,
            .ExitCode = -1,
            .Reason = Reason,
            .ErrorMessage = Message
        }
    End Function

    Private Sub AddCandidate(Candidates As HashSet(Of String), Candidate As String)
        If String.IsNullOrWhiteSpace(Candidate) Then Return
        Try
            Candidates.Add(Path.GetFullPath(Environment.ExpandEnvironmentVariables(Candidate.Trim())))
        Catch
        End Try
    End Sub

End Module
