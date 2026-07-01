Imports Windows.Media
Imports Windows.Media.Core
Imports Windows.Media.Playback
Imports Windows.Storage.Streams

Public Module OmniMixSmtcService

    Private ReadOnly SyncRoot As New Object()
    Private CurrentPlayer As MediaPlayer = Nothing
    Private CurrentControls As SystemMediaTransportControls = Nothing
    Private CurrentCommandHandler As Action(Of String) = Nothing
    Private IsInitialized As Boolean = False
    Private IsInitializing As Boolean = False
    Private LastTitle As String = ""
    Private LastArtist As String = ""
    Private LastStatus As MediaPlaybackStatus = MediaPlaybackStatus.Closed

    Public Async Sub Initialize(CommandHandler As Action(Of String))
        SyncLock SyncRoot
            If IsInitialized OrElse IsInitializing Then Return
            IsInitializing = True
            CurrentCommandHandler = CommandHandler
        End SyncLock

        Try
            Dim Player As New MediaPlayer With {
                .AutoPlay = False,
                .IsLoopingEnabled = True,
                .Volume = 0
            }
            Player.CommandManager.IsEnabled = False

            Dim Controls = Player.SystemMediaTransportControls
            Controls.IsEnabled = True
            Controls.IsPlayEnabled = True
            Controls.IsPauseEnabled = True
            Controls.IsStopEnabled = True
            Controls.IsNextEnabled = True
            Controls.IsPreviousEnabled = True
            Controls.PlaybackStatus = MediaPlaybackStatus.Paused

            Dim Updater = Controls.DisplayUpdater
            Updater.Type = MediaPlaybackType.Music
            Updater.MusicProperties.Title = "OmniMix"
            Updater.MusicProperties.Artist = "OmniMix Player"
            Updater.Update()

            AddHandler Controls.ButtonPressed, AddressOf Controls_ButtonPressed

            Player.Source = MediaSource.CreateFromStream(Await CreateSilentWavStreamAsync(), "audio/wav")
            Player.Play()

            SyncLock SyncRoot
                CurrentPlayer = Player
                CurrentControls = Controls
                IsInitialized = True
                IsInitializing = False
            End SyncLock

            Logger.Info("已通过 MediaPlayer 初始化系统媒体传输控件 (SMTC)")
        Catch Ex As Exception
            SyncLock SyncRoot
                IsInitializing = False
                IsInitialized = False
                CurrentPlayer = Nothing
                CurrentControls = Nothing
            End SyncLock
            Logger.Warn(Ex, "初始化系统媒体传输控件 (SMTC) 失败")
        End Try
    End Sub

    Public Sub Shutdown()
        Dim Player As MediaPlayer = Nothing
        Dim Controls As SystemMediaTransportControls = Nothing

        SyncLock SyncRoot
            Player = CurrentPlayer
            Controls = CurrentControls
            CurrentPlayer = Nothing
            CurrentControls = Nothing
            CurrentCommandHandler = Nothing
            IsInitialized = False
            IsInitializing = False
            LastTitle = ""
            LastArtist = ""
            LastStatus = MediaPlaybackStatus.Closed
        End SyncLock

        Try
            If Controls IsNot Nothing Then
                RemoveHandler Controls.ButtonPressed, AddressOf Controls_ButtonPressed
                Controls.PlaybackStatus = MediaPlaybackStatus.Closed
                Controls.IsEnabled = False
            End If
        Catch Ex As Exception
            Logger.Warn(Ex, "关闭系统媒体传输控件 (SMTC) 状态失败")
        End Try

        Try
            If Player IsNot Nothing Then
                Player.Pause()
                Player.Source = Nothing
                Player.Dispose()
            End If
        Catch Ex As Exception
            Logger.Warn(Ex, "释放系统媒体传输控件 (SMTC) 播放器失败")
        End Try
    End Sub

    Public Sub UpdatePlayback(ActiveInstance As OmniMixPlaybackInstanceInfo)
        Dim Controls = CurrentControls
        If Controls Is Nothing Then Return

        Try
            Dim Track = ActiveInstance?.CurrentTrack
            Dim Title = If(Track Is Nothing, "OmniMix", NonEmpty(Track.Title, Track.Uuid, "OmniMix"))
            Dim Artist = If(Track Is Nothing, "OmniMix Player", NonEmpty(Track.Artist, Track.ModuleId, "OmniMix Player"))
            Dim Status = ResolvePlaybackStatus(ActiveInstance)

            If Not String.Equals(Title, LastTitle, StringComparison.Ordinal) OrElse
               Not String.Equals(Artist, LastArtist, StringComparison.Ordinal) Then
                Dim Updater = Controls.DisplayUpdater
                Updater.Type = MediaPlaybackType.Music
                Updater.MusicProperties.Title = Title
                Updater.MusicProperties.Artist = Artist
                Updater.Update()
                LastTitle = Title
                LastArtist = Artist
            End If

            If Status <> LastStatus Then
                Controls.PlaybackStatus = Status
                LastStatus = Status
            End If
        Catch Ex As Exception
            Logger.Warn(Ex, "更新系统媒体传输控件 (SMTC) 状态失败")
        End Try
    End Sub

    Private Sub Controls_ButtonPressed(Sender As SystemMediaTransportControls, Args As SystemMediaTransportControlsButtonPressedEventArgs)
        Dim Command = ResolveCommand(Args.Button)
        If String.IsNullOrWhiteSpace(Command) Then Return

        Logger.Info("收到系统媒体传输控件 (SMTC) 按钮：" & Args.Button.ToString() & " -> " & Command)
        Dim Handler As Action(Of String) = Nothing
        SyncLock SyncRoot
            Handler = CurrentCommandHandler
        End SyncLock
        If Handler Is Nothing Then Return

        Try
            Application.Current.Dispatcher.BeginInvoke(
                Sub()
                    Try
                        Handler(Command)
                    Catch Ex As Exception
                        Logger.Warn(Ex, "处理系统媒体传输控件 (SMTC) 命令失败")
                    End Try
                End Sub)
        Catch Ex As Exception
            Logger.Warn(Ex, "分发系统媒体传输控件 (SMTC) 命令失败")
        End Try
    End Sub

    Private Function ResolveCommand(Button As SystemMediaTransportControlsButton) As String
        Select Case Button
            Case SystemMediaTransportControlsButton.Play
                Return "resume"
            Case SystemMediaTransportControlsButton.Pause
                Return "pause"
            Case SystemMediaTransportControlsButton.Stop
                Return "stop"
            Case SystemMediaTransportControlsButton.Next
                Return "next"
            Case SystemMediaTransportControlsButton.Previous
                Return "prev"
            Case Else
                Return ""
        End Select
    End Function

    Private Function ResolvePlaybackStatus(ActiveInstance As OmniMixPlaybackInstanceInfo) As MediaPlaybackStatus
        If ActiveInstance Is Nothing OrElse ActiveInstance.CurrentTrack Is Nothing Then Return MediaPlaybackStatus.Paused
        Return If(ActiveInstance.IsPlaying, MediaPlaybackStatus.Playing, MediaPlaybackStatus.Paused)
    End Function

    Private Async Function CreateSilentWavStreamAsync() As Task(Of IRandomAccessStream)
        Dim Stream As New InMemoryRandomAccessStream()
        Dim Writer As New DataWriter(Stream)
        Writer.WriteBytes(CreateSilentWavBytes())
        Await Writer.StoreAsync()
        Await Writer.FlushAsync()
        Writer.DetachStream()
        Stream.Seek(0)
        Return Stream
    End Function

    Private Function CreateSilentWavBytes() As Byte()
        Const SampleRate As Integer = 8000
        Const Channels As Short = 1
        Const BitsPerSample As Short = 16
        Const DurationSeconds As Integer = 1
        Dim DataSize = SampleRate * Channels * (BitsPerSample \ 8) * DurationSeconds
        Dim Result(44 + DataSize - 1) As Byte

        WriteAscii(Result, 0, "RIFF")
        WriteInt32(Result, 4, 36 + DataSize)
        WriteAscii(Result, 8, "WAVE")
        WriteAscii(Result, 12, "fmt ")
        WriteInt32(Result, 16, 16)
        WriteInt16(Result, 20, 1)
        WriteInt16(Result, 22, Channels)
        WriteInt32(Result, 24, SampleRate)
        WriteInt32(Result, 28, SampleRate * Channels * (BitsPerSample \ 8))
        WriteInt16(Result, 32, CShort(Channels * (BitsPerSample \ 8)))
        WriteInt16(Result, 34, BitsPerSample)
        WriteAscii(Result, 36, "data")
        WriteInt32(Result, 40, DataSize)
        Return Result
    End Function

    Private Sub WriteAscii(Buffer As Byte(), Offset As Integer, Value As String)
        Dim Bytes = Text.Encoding.ASCII.GetBytes(Value)
        Array.Copy(Bytes, 0, Buffer, Offset, Bytes.Length)
    End Sub

    Private Sub WriteInt16(Buffer As Byte(), Offset As Integer, Value As Short)
        Dim Bytes = BitConverter.GetBytes(Value)
        Array.Copy(Bytes, 0, Buffer, Offset, Bytes.Length)
    End Sub

    Private Sub WriteInt32(Buffer As Byte(), Offset As Integer, Value As Integer)
        Dim Bytes = BitConverter.GetBytes(Value)
        Array.Copy(Bytes, 0, Buffer, Offset, Bytes.Length)
    End Sub

    Private Function NonEmpty(ParamArray Values As String()) As String
        For Each Value In Values
            If Not String.IsNullOrWhiteSpace(Value) Then Return Value
        Next
        Return ""
    End Function

End Module
