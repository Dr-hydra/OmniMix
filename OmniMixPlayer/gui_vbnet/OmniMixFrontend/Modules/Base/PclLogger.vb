Public Class PclLogger
    Inherits FileLogger

    Private Const FeedbackPrompt As String = "是否要反馈此问题？反馈时请附上日志文件。" & vbCrLf &
                                             "你可以前往 GitHub Issues，或加入 QQ 群 851586605 进行反馈。"

    Protected Shared Function FilterAccessToken(Raw As String, FilterChar As Char) As String
        If Raw Is Nothing Then Return Nothing
        Return Raw.RegexReplace("(?i)(access[_-]?token[""'\s:=]+)[^""'\s&]+", Function(m) m.Groups(1).Value & New String(FilterChar, 8))
    End Function

    Protected Shared Function FilterUserName(Raw As String, FilterChar As Char) As String
        If Raw Is Nothing Then Return Nothing
        Dim UserName = Environment.UserName
        If String.IsNullOrWhiteSpace(UserName) Then Return Raw
        Return Raw.Replace(UserName, New String(FilterChar, Math.Min(UserName.Length, 8)))
    End Function

    ''' <inheritdoc/>
    Public Overrides Function Format(Text As String, Level As LogLevel, FilePath As String, Ex As Exception) As String
        Text = MyBase.Format(Text, Level, FilePath, Ex)
        Text = FilterUserName(FilterAccessToken(Text, "*"c), "*"c)
        Return Text
    End Function

    ''' <inheritdoc/>
    Public Overrides Sub HandleBehavior(RawMessage As String, FormattedMessage As String, Behavior As LogBehavior, Ex As Exception)
        If IsProgramEnding Then Return
        MyBase.HandleBehavior(RawMessage, FormattedMessage, Behavior, Ex)
        Dim BriefText = If(Ex Is Nothing, RawMessage, If(RawMessage Is Nothing, "", $"{RawMessage}：") & Ex.GetDisplay(False))
        BriefText = FilterUserName(FilterAccessToken(BriefText, "*"c), "*"c)
        Dim DetailText = If(Ex Is Nothing, RawMessage, If(RawMessage Is Nothing, "", $"{RawMessage}：") & Ex.GetDisplay(True))
        DetailText = FilterUserName(FilterAccessToken(DetailText, "*"c), "*"c)
        Select Case Behavior
            Case LogBehavior.None
                '啥也不干
            Case LogBehavior.ToastIfDebug
                If BuildType = BuildTypes.Debug OrElse ModeDebug Then Hint("[调试模式] " & BriefText, HintType.Blue, False)
            Case LogBehavior.Toast
                Hint(BriefText, HintType.Red, False)
            Case LogBehavior.Alert
                MyMsgBox(DetailText, "错误", IsWarn:=True)
            Case LogBehavior.AlertThenFeedback
                If MyMsgBox(DetailText & vbCrLf & vbCrLf & FeedbackPrompt, "错误", "进行反馈", "暂不反馈", IsWarn:=True) = 1 Then Feedback(False, True)
            Case LogBehavior.AlertThenCrash
                Static FirstTrigger As Boolean = True
                If FirstTrigger Then
                    '首次触发
                    FirstTrigger = False
                    If MsgBox(DetailText & vbCrLf & vbCrLf & FeedbackPrompt, MsgBoxStyle.Critical + MsgBoxStyle.YesNo, "错误") = MsgBoxResult.Yes Then Feedback(False, True)
                Else
                    '多次触发，直接使程序崩溃（这通常代表着在其他线程循环触发严重异常）
                    Thread.Sleep(2000)
                End If
                FormMain.EndProgramForce(ProcessReturnValues.Exception)
        End Select
        '遥测
        If Behavior >= LogBehavior.Toast Then Telemetry("错误日志", "Exception", DetailText)
    End Sub

End Class

Public Class OmniMixGuiLogger
    Inherits PclLogger

    Private ReadOnly SessionId As String = Guid.NewGuid().ToString("N").Substring(0, 8)
    Private ReadOnly SyncRoot As New Object
    Private Writer As IO.StreamWriter

    Public Overrides Sub Init()
        Dim Folder = If(String.IsNullOrWhiteSpace(logFolder), IO.Path.Combine(PathUtils.CurrentFolder, "logs"), logFolder)
        Dim LogPath = IO.Path.Combine(Folder, "omnimix_gui.log")
        Try
            IO.Directory.CreateDirectory(Folder)
            SyncLock SyncRoot
                For Index = 4 To 1 Step -1
                    Dim Older = $"{LogPath}.{Index}"
                    Dim Newer = $"{LogPath}.{Index + 1}"
                    If IO.File.Exists(Newer) Then IO.File.Delete(Newer)
                    If IO.File.Exists(Older) Then IO.File.Move(Older, Newer)
                Next
                If IO.File.Exists($"{LogPath}.1") Then IO.File.Delete($"{LogPath}.1")
                If IO.File.Exists(LogPath) Then IO.File.Move(LogPath, $"{LogPath}.1")
                Writer = New IO.StreamWriter(PathUtils.ForApi(LogPath), append:=True) With {.AutoFlush = True}
            End SyncLock
        Catch ex As Exception
            System.Diagnostics.Debug.WriteLine($"GUI 日志初始化失败：{ex.Message}")
        End Try
    End Sub

    Public Overrides Function Format(Text As String, Level As LogLevel, FilePath As String, Ex As Exception) As String
        Dim Prefix = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{GetBackendLevelName(Level)}] [{SessionId}] GUI"
        Dim Source = PathUtils.GetLastPart(FilePath).BeforeFirst(".")
        If Not String.IsNullOrWhiteSpace(Source) Then Prefix &= $"[{Source}]"
        Prefix &= ": "

        Text = If(Text, "")
        Text = FilterUserName(FilterAccessToken(Text, "*"c), "*"c)
        Dim Lines = Text.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf).Split({vbLf}, System.StringSplitOptions.None)
        Return String.Join(vbCrLf, Lines.Select(Function(Line) Prefix & Line))
    End Function

    Public Overrides Sub Output(entry As LogEntry)
        System.Diagnostics.Debug.WriteLine(entry.message)
        SyncLock SyncRoot
            Writer?.WriteLine(entry.message)
        End SyncLock
    End Sub

    Private Shared Function GetBackendLevelName(Level As LogLevel) As String
        Select Case Level
            Case LogLevel.Trace
                Return "Trace"
            Case LogLevel.Info
                Return "Information"
            Case LogLevel.Warn
                Return "Warning"
            Case LogLevel.Error
                Return "Error"
            Case Else
                Return Level.ToString()
        End Select
    End Function

End Class
