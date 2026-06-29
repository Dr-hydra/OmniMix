Imports System.Globalization
Imports System.Text.Json

Public Class PageOmniMixLeft
    Implements IDispatcherUnhandledException

    Private CurrentPage As FormMain.PageType = FormMain.PageType.Launch
    Private CurrentRight As PageOmniMixRight = Nothing
    Private CurrentBaseUrl As String = ""
    Private ActiveInstanceId As String = ""
    Private ActiveShuffle As Boolean = False
    Private ActiveRepeatMode As String = "none"
    Private ActivePosition As Double = 0
    Private ActiveDuration As Double = 0
    Private ActiveLatency As Double = 0.1
    Private ActiveIsPlaying As Boolean = False
    Private ActivePlaybackMode As PlaybackMode = PlaybackMode.Sequence
    Private IsOnline As Boolean = False
    Private CanControlActiveInstance As Boolean = False
    Private IsUpdatingPlaybackUi As Boolean = False
    Private CurrentLibrarySourceId As String = ""
    Private ReadOnly LibrarySourceItems As New Dictionary(Of String, MyListItem)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly PlayerAutoRefreshTimer As New System.Windows.Threading.DispatcherTimer With {.Interval = TimeSpan.FromSeconds(1)}
    Private ReadOnly SeekDebounceTimer As New System.Windows.Threading.DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(250)}
    Private Shared ReadOnly CoverHttpClient As New System.Net.Http.HttpClient()
    Private IsRefreshingPlayer As Boolean = False
    Private CurrentCoverSource As String = ""
    Private CoverLoadSerial As Integer = 0
    Private PendingSeekPosition As Double = -1
    Private IsSendingSeek As Boolean = False

    Private Enum PlaybackMode
        Sequence
        Shuffle
        RepeatOne
    End Enum

    Private Const EmptyTrackText As String = "没有曲目正在播放"
    Private Const IconPrev As String = "M704 256v512L320 512l384-256zM224 256h96v512h-96z"
    Private Const IconNext As String = "M320 256v512l384-256-384-256zM704 256h96v512h-96z"
    Private Const IconPlay As String = "M352 224v672l480-336-480-336z"
    Private Const IconPause As String = "M320 224h160v672H320zM576 224h160v672H576z"
    Private Const IconSequence As String = "M128 256h496v96H128zM128 464h496v96H128zM128 672h496v96H128zM688 384l192 128-192 128z"
    Private Const IconShuffle As String = "M704 192h192v192h-96v-64h-72c-79 0-124 44-173 118l-38 58-54-82 34-51c57-85 119-171 231-171zM128 304h142c75 0 129 40 181 108l-53 80c-45-60-79-108-128-108H128V304zM704 704h96v-64h96v192H704v-64h-24c-112 0-174-86-231-171l-34-51 54-82 38 58c49 74 94 118 173 118h24v64zM128 640h142c49 0 83-48 128-108l53 80c-52 68-106 108-181 108H128v-80z"
    Private Const IconRepeatOne As String = "M256 288h448l-96-96 64-64 208 208-208 208-64-64 96-96H256c-53 0-96 43-96 96v64H64v-64c0-106 86-192 192-192zM768 736H320l96 96-64 64-208-208 208-208 64 64-96 96h448c53 0 96-43 96-96v-64h96v64c0 106-86 192-192 192zM500 384h72v240h-72V472l-48 28-32-52z"

    Public Sub Configure(Page As FormMain.PageType, RightPage As PageOmniMixRight)
        CurrentPage = Page
        CurrentRight = RightPage
        SetupPlaybackIcons()

        Width = If(Page = FormMain.PageType.Launch, 300, 152)
        PanPlayer.Visibility = If(Page = FormMain.PageType.Launch, Visibility.Visible, Visibility.Collapsed)
        PanNavScroll.Visibility = If(Page = FormMain.PageType.Launch, Visibility.Collapsed, Visibility.Visible)
        PanLibraryNav.Visibility = If(Page = FormMain.PageType.Download, Visibility.Visible, Visibility.Collapsed)
        PanModuleNav.Visibility = If(Page = FormMain.PageType.Link, Visibility.Visible, Visibility.Collapsed)
        PanSettingsNav.Visibility = If(Page = FormMain.PageType.Setup, Visibility.Visible, Visibility.Collapsed)
        PanAboutNav.Visibility = If(Page = FormMain.PageType.Other, Visibility.Visible, Visibility.Collapsed)

        If Page = FormMain.PageType.Launch Then
            RefreshPlayerAsync()
        ElseIf Page = FormMain.PageType.Download Then
            RefreshLibrarySourcesAsync()
        ElseIf Page = FormMain.PageType.Link Then
            ItemModuleLaunchpad.SetChecked(True, False, False)
            CurrentRight?.SetModulesPane("launchpad")
        ElseIf Page = FormMain.PageType.Setup Then
            ItemSettingsConfig.SetChecked(True, False, False)
            CurrentRight?.SetSettingsPane("config")
        End If
        UpdatePlayerAutoRefreshTimer()
    End Sub

    Public Sub SetBackendStatus(Online As Boolean, BaseUrl As String)
        IsOnline = Online
        CurrentBaseUrl = If(BaseUrl, "")
        If CurrentPage = FormMain.PageType.Launch Then RefreshPlayerAsync()
        If CurrentPage = FormMain.PageType.Download Then RefreshLibrarySourcesAsync()
        UpdatePlayerAutoRefreshTimer()
    End Sub

    Private Sub PageOmniMixLeft_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        RemoveHandler PlayerAutoRefreshTimer.Tick, AddressOf PlayerAutoRefreshTimer_Tick
        AddHandler PlayerAutoRefreshTimer.Tick, AddressOf PlayerAutoRefreshTimer_Tick
        RemoveHandler SeekDebounceTimer.Tick, AddressOf SeekDebounceTimer_Tick
        AddHandler SeekDebounceTimer.Tick, AddressOf SeekDebounceTimer_Tick
        UpdatePlayerAutoRefreshTimer()
    End Sub

    Private Sub PageOmniMixLeft_Unloaded(sender As Object, e As RoutedEventArgs) Handles Me.Unloaded
        PlayerAutoRefreshTimer.Stop()
        SeekDebounceTimer.Stop()
    End Sub

    Private Sub UpdatePlayerAutoRefreshTimer()
        If CurrentPage = FormMain.PageType.Launch AndAlso IsLoaded AndAlso IsOnline AndAlso Not String.IsNullOrWhiteSpace(CurrentBaseUrl) Then
            If Not PlayerAutoRefreshTimer.IsEnabled Then PlayerAutoRefreshTimer.Start()
        Else
            If PlayerAutoRefreshTimer.IsEnabled Then PlayerAutoRefreshTimer.Stop()
        End If
    End Sub

    Private Sub PlayerAutoRefreshTimer_Tick(sender As Object, e As EventArgs)
        RefreshPlayerAsync(False, False)
    End Sub

    Private Async Sub RefreshPlayerAsync(Optional LoadPlaylist As Boolean = True, Optional ShowErrorHint As Boolean = True)
        If CurrentPage <> FormMain.PageType.Launch Then Return
        If IsRefreshingPlayer Then Return
        IsRefreshingPlayer = True
        Try
            If Not IsOnline OrElse String.IsNullOrWhiteSpace(CurrentBaseUrl) Then
                ActiveInstanceId = ""
                CanControlActiveInstance = False
                RenderPlayback(Nothing)
                Return
            End If

            Dim Instances = Await OmniMixApiClient.GetInstancesAsync(CurrentBaseUrl)
            OmniMixDesktopPlayerService.ReconcileWithInstances(CurrentBaseUrl, Instances)
            Dim Config = Await OmniMixApiClient.GetConfigAsync(CurrentBaseUrl)
            Dim Playlist As OmniMixPlaylistData = Nothing
            If LoadPlaylist Then
                Try
                    Playlist = Await OmniMixApiClient.GetPlaylistAsync(CurrentBaseUrl)
                Catch
                    Playlist = Nothing
                End Try
            End If
            Dim ActiveInstance = PickActiveInstance(Instances, ConfigString(Config, "active_instance", ""))
            ActiveInstanceId = If(ActiveInstance Is Nothing, "", ActiveInstance.Id)
            RenderPlayback(ActiveInstance, Playlist)
        Catch Ex As Exception
            ActiveInstanceId = ""
            CanControlActiveInstance = False
            LabLeftPlaybackTrack.Text = EmptyTrackText
            LabLeftPlaybackMeta.Visibility = Visibility.Collapsed
            If ShowErrorHint Then Hint("播放状态读取失败：" & Ex.Message, HintType.Red)
        Finally
            IsRefreshingPlayer = False
        End Try
    End Sub

    Private Sub RenderPlayback(ActiveInstance As OmniMixPlaybackInstanceInfo, Optional Playlist As OmniMixPlaylistData = Nothing)
        IsUpdatingPlaybackUi = True
        Try
            If ActiveInstance Is Nothing Then
                CanControlActiveInstance = False
                ActiveIsPlaying = False
                LabLeftPlaybackTrack.Text = EmptyTrackText
                LabLeftPlaybackMeta.Text = ""
                LabLeftPlaybackMeta.Visibility = Visibility.Collapsed
                SliderLeftVolume.Value = 100
                LabLeftPlaybackVolume.Text = "100%"
                SliderLeftLatency.Value = 100
                LabLeftPlaybackLatency.Text = "0.10s"
                ActiveShuffle = False
                ActiveRepeatMode = "none"
                ActivePlaybackMode = PlaybackMode.Sequence
                ActivePosition = 0
                ActiveDuration = 0
                ActiveLatency = 0.1
                ClearCoverImage()
            Else
                CanControlActiveInstance = ActiveInstance.IsServerManaged
                ActiveIsPlaying = ActiveInstance.IsPlaying
                Dim Track = ActiveInstance.CurrentTrack
                If Track Is Nothing Then
                    LabLeftPlaybackTrack.Text = EmptyTrackText
                    LabLeftPlaybackMeta.Text = ""
                    LabLeftPlaybackMeta.Visibility = Visibility.Collapsed
                    ClearCoverImage()
                Else
                    LabLeftPlaybackTrack.Text = NonEmpty(Track.Title, Track.Uuid)
                    Dim MetaParts As New List(Of String)
                    If Not String.IsNullOrWhiteSpace(Track.Artist) Then MetaParts.Add(Track.Artist)
                    If Not String.IsNullOrWhiteSpace(Track.ModuleId) Then MetaParts.Add(Track.ModuleId)
                    LabLeftPlaybackMeta.Text = String.Join(" · ", MetaParts)
                    LabLeftPlaybackMeta.Visibility = If(MetaParts.Count = 0, Visibility.Collapsed, Visibility.Visible)
                    SetCoverImage(ResolveTrackCover(Track, Playlist))
                End If

                ActiveShuffle = ActiveInstance.Shuffle
                ActiveRepeatMode = If(String.IsNullOrWhiteSpace(ActiveInstance.RepeatMode), "none", ActiveInstance.RepeatMode)
                ActivePlaybackMode = ResolvePlaybackMode()
                ActivePosition = Math.Max(0, ActiveInstance.Position)
                ActiveDuration = If(Track Is Nothing, 0, Math.Max(0, Track.Duration))
                ActiveLatency = Math.Max(0.03, Math.Min(1, ActiveInstance.TargetLatency))
                Dim VolumeValue = CInt(Math.Round(Math.Max(0, Math.Min(1, ActiveInstance.Volume)) * 100))
                SliderLeftVolume.Value = VolumeValue
                LabLeftPlaybackVolume.Text = VolumeValue & "%"
                SliderLeftLatency.Value = CInt(Math.Round(ActiveLatency * 1000))
                LabLeftPlaybackLatency.Text = ActiveLatency.ToString("0.00", CultureInfo.InvariantCulture) & "s"
            End If

            RefreshProgressUi()
            BtnLeftPlaybackToggle.Logo = If(ActiveIsPlaying, IconPause, IconPlay)
            BtnLeftPlaybackPrev.Opacity = If(CanControlActiveInstance, 1, 0.55)
            BtnLeftPlaybackToggle.Opacity = If(CanControlActiveInstance, 1, 0.55)
            BtnLeftPlaybackNext.Opacity = If(CanControlActiveInstance, 1, 0.55)
            BtnLeftPlaybackMode.Logo = PlaybackModeIcon(ActivePlaybackMode)
            BtnLeftPlaybackMode.Opacity = If(CanControlActiveInstance, 1, 0.55)
            BtnLeftPlaybackMode.ToolTip = "播放模式：" & PlaybackModeText(ActivePlaybackMode)
            BtnLeftPlaybackMode.IsEnabled = True
            BtnLeftPlaybackPrev.IsEnabled = True
            BtnLeftPlaybackToggle.IsEnabled = True
            BtnLeftPlaybackNext.IsEnabled = True
            SliderLeftVolume.IsEnabled = ActiveInstance IsNot Nothing
            SliderLeftLatency.IsEnabled = CanControlActiveInstance
        Finally
            IsUpdatingPlaybackUi = False
        End Try
    End Sub

    Private Sub SetupPlaybackIcons()
        BtnLeftPlaybackPrev.Logo = IconPrev
        BtnLeftPlaybackToggle.Logo = IconPlay
        BtnLeftPlaybackNext.Logo = IconNext
        BtnLeftPlaybackMode.Logo = IconSequence
        SliderLeftProgress.GetHintText = Function(Value As Integer) FormatDuration(Value)
    End Sub

    Private Function ResolvePlaybackMode() As PlaybackMode
        If ActiveShuffle Then Return PlaybackMode.Shuffle
        If String.Equals(ActiveRepeatMode, "one", StringComparison.OrdinalIgnoreCase) Then Return PlaybackMode.RepeatOne
        Return PlaybackMode.Sequence
    End Function

    Private Shared Function NextPlaybackMode(Mode As PlaybackMode) As PlaybackMode
        Select Case Mode
            Case PlaybackMode.Sequence
                Return PlaybackMode.Shuffle
            Case PlaybackMode.Shuffle
                Return PlaybackMode.RepeatOne
            Case Else
                Return PlaybackMode.Sequence
        End Select
    End Function

    Private Shared Function PlaybackModeIcon(Mode As PlaybackMode) As String
        Select Case Mode
            Case PlaybackMode.Shuffle
                Return IconShuffle
            Case PlaybackMode.RepeatOne
                Return IconRepeatOne
            Case Else
                Return IconSequence
        End Select
    End Function

    Private Shared Function PlaybackModeText(Mode As PlaybackMode) As String
        Select Case Mode
            Case PlaybackMode.Shuffle
                Return "随机"
            Case PlaybackMode.RepeatOne
                Return "单曲循环"
            Case Else
                Return "顺序播放"
        End Select
    End Function

    Private Sub RefreshProgressUi()
        Dim HasDuration = ActiveDuration > 0
        Dim IsDraggingProgress = ReferenceEquals(SliderLeftProgress, DragControl)
        SliderLeftProgress.MaxValue = If(HasDuration, Math.Max(1, CInt(Math.Ceiling(ActiveDuration))), 100)
        If Not IsDraggingProgress Then
            SliderLeftProgress.Value = If(HasDuration, CInt(Math.Max(0, Math.Min(SliderLeftProgress.MaxValue, Math.Round(ActivePosition)))), 0)
        End If
        SliderLeftProgress.IsEnabled = CanControlActiveInstance AndAlso HasDuration
        If Not IsDraggingProgress Then LabLeftPlaybackElapsed.Text = If(HasDuration, FormatDuration(ActivePosition), "")
        LabLeftPlaybackTotal.Text = If(HasDuration, FormatDuration(ActiveDuration), "")
        LabLeftPlaybackElapsed.Visibility = If(HasDuration, Visibility.Visible, Visibility.Collapsed)
        LabLeftPlaybackTotal.Visibility = If(HasDuration, Visibility.Visible, Visibility.Collapsed)
    End Sub

    Private Function ResolveTrackCover(Track As OmniMixTrackInfo, Playlist As OmniMixPlaylistData) As String
        If Track Is Nothing Then Return ""
        If Not String.IsNullOrWhiteSpace(Track.Uuid) AndAlso Not String.IsNullOrWhiteSpace(CurrentBaseUrl) Then
            Return CurrentBaseUrl.TrimEnd("/"c) & "/api/track/cover?uuid=" & Uri.EscapeDataString(Track.Uuid)
        End If
        For Each Candidate In {Track.CoverPath, Track.CoverUrl, Track.ImageUrl}
            If Not String.IsNullOrWhiteSpace(Candidate) Then Return Candidate
        Next
        If Playlist?.Albums IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(Track.AlbumId) Then
            Dim Album = Playlist.Albums.FirstOrDefault(Function(Item) String.Equals(Item.Id, Track.AlbumId, StringComparison.OrdinalIgnoreCase))
            If Album IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(Album.CoverPath) Then Return Album.CoverPath
        End If
        Return ""
    End Function

    Private Async Sub SetCoverImage(Source As String)
        If String.IsNullOrWhiteSpace(Source) Then
            ClearCoverImage()
            Return
        End If

        Try
            Dim NormalizedSource = Source.Trim()
            If String.Equals(CurrentCoverSource, NormalizedSource, StringComparison.Ordinal) AndAlso ImgLeftPlaybackCover.Source IsNot Nothing Then Return

            Dim SourceUri = ResolveImageUri(NormalizedSource)
            If SourceUri Is Nothing Then
                ClearCoverImage()
                Return
            End If

            Dim LoadSerial = Threading.Interlocked.Increment(CoverLoadSerial)
            CurrentCoverSource = NormalizedSource

            Dim Bitmap = Await LoadCoverBitmapAsync(SourceUri)
            If LoadSerial <> CoverLoadSerial OrElse Not String.Equals(CurrentCoverSource, NormalizedSource, StringComparison.Ordinal) Then Return

            ImgLeftPlaybackCover.Source = Bitmap
            ImgLeftPlaybackCover.Visibility = Visibility.Visible
            PathLeftPlaybackCoverPlaceholder.Visibility = Visibility.Collapsed
        Catch ex As Exception
            Logger.Warn(ex, $"加载播放封面失败（{Source}）")
            ClearCoverImage()
        End Try
    End Sub

    Private Shared Async Function LoadCoverBitmapAsync(SourceUri As Uri) As Task(Of BitmapSource)
        If SourceUri.Scheme = Uri.UriSchemeHttp OrElse SourceUri.Scheme = Uri.UriSchemeHttps Then
            Dim Bytes = Await CoverHttpClient.GetByteArrayAsync(SourceUri)
            Return CreateFrozenBitmap(Bytes)
        End If

        If SourceUri.IsFile Then
            Dim Bytes = Await Task.Run(Function() File.ReadAllBytes(SourceUri.LocalPath))
            Return CreateFrozenBitmap(Bytes)
        End If

        If String.Equals(SourceUri.Scheme, "pack", StringComparison.OrdinalIgnoreCase) Then
            Dim Resource = System.Windows.Application.GetResourceStream(SourceUri)
            If Resource Is Nothing Then Throw New FileNotFoundException($"未找到图片资源：{SourceUri}")
            Using Resource.Stream
                Using Buffer As New MemoryStream()
                    Resource.Stream.CopyTo(Buffer)
                    Return CreateFrozenBitmap(Buffer.ToArray())
                End Using
            End Using
        End If

        If String.Equals(SourceUri.Scheme, "data", StringComparison.OrdinalIgnoreCase) Then
            Return CreateFrozenBitmap(ReadDataUriBytes(SourceUri))
        End If

        Throw New NotSupportedException($"不支持从 {SourceUri.Scheme} URI 加载播放封面。")
    End Function

    Private Shared Function CreateFrozenBitmap(Bytes As Byte()) As BitmapSource
        If Bytes Is Nothing OrElse Bytes.Length = 0 Then Throw New InvalidDataException("图片数据为空。")
        Using Stream As New MemoryStream(Bytes)
            Dim Decoder = BitmapDecoder.Create(Stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad)
            If Decoder.Frames.Count = 0 Then Throw New InvalidDataException("图片不包含可解码的帧。")
            Dim Bitmap As New WriteableBitmap(Decoder.Frames(0))
            Bitmap.Freeze()
            Return Bitmap
        End Using
    End Function

    Private Shared Function ReadDataUriBytes(SourceUri As Uri) As Byte()
        Dim Source = SourceUri.OriginalString
        Dim Separator = Source.IndexOf(","c)
        If Separator < 0 Then Throw New FormatException("无效的 data URI。")

        Dim Metadata = Source.Substring(0, Separator)
        Dim Payload = Uri.UnescapeDataString(Source.Substring(Separator + 1))
        If Metadata.EndsWith(";base64", StringComparison.OrdinalIgnoreCase) Then
            Return Convert.FromBase64String(Payload)
        End If
        Return Encoding.UTF8.GetBytes(Payload)
    End Function

    Private Function ResolveImageUri(Source As String) As Uri
        Dim Trimmed = Source.Trim()
        Dim LocalPath = If(IO.Path.IsPathRooted(Trimmed), Trimmed, IO.Path.Combine(PathExeFolder, Trimmed))
        If File.Exists(LocalPath) Then Return New Uri(LocalPath, UriKind.Absolute)

        Dim AbsoluteUri As Uri = Nothing
        If Uri.TryCreate(Trimmed, UriKind.Absolute, AbsoluteUri) Then Return AbsoluteUri
        If Trimmed.StartsWith("/", StringComparison.Ordinal) AndAlso Not String.IsNullOrWhiteSpace(CurrentBaseUrl) Then
            Return New Uri(CurrentBaseUrl.TrimEnd("/"c) & Trimmed, UriKind.Absolute)
        End If

        If Not String.IsNullOrWhiteSpace(CurrentBaseUrl) Then Return New Uri(CurrentBaseUrl.TrimEnd("/"c) & "/" & Trimmed.TrimStart("/"c), UriKind.Absolute)
        Return Nothing
    End Function

    Private Sub ClearCoverImage()
        Threading.Interlocked.Increment(CoverLoadSerial)
        CurrentCoverSource = ""
        ImgLeftPlaybackCover.Source = Nothing
        ImgLeftPlaybackCover.Visibility = Visibility.Collapsed
        PathLeftPlaybackCoverPlaceholder.Visibility = Visibility.Visible
    End Sub

    Public Sub DispatcherUnhandledException(sender As Object, e As System.Windows.Threading.DispatcherUnhandledExceptionEventArgs) Implements IDispatcherUnhandledException.DispatcherUnhandledException
        If Not IsWicStreamReadFailure(e.Exception) Then Return
        Logger.Warn(e.Exception, "播放封面异步解码失败，已回退到默认封面")
        ClearCoverImage()
        e.Handled = True
    End Sub

    Private Shared Function IsWicStreamReadFailure(Ex As Exception) As Boolean
        Const WicErrStreamRead As Integer = -2003292302 ' 0x88982F72
        Dim Current = Ex
        While Current IsNot Nothing
            If Current.HResult = WicErrStreamRead Then Return True
            Dim Trace = Current.StackTrace
            If Not String.IsNullOrEmpty(Trace) AndAlso
               (Trace.Contains("System.Windows.Media.Imaging.BitmapImage.OnDownloadCompleted") OrElse
                Trace.Contains("System.Windows.Media.Imaging.LateBoundBitmapDecoder.DownloadCallback")) Then
                Return True
            End If
            Current = Current.InnerException
        End While
        Return False
    End Function

    Private Shared Function PickActiveInstance(Instances As List(Of OmniMixPlaybackInstanceInfo), Optional PreferredId As String = "") As OmniMixPlaybackInstanceInfo
        If Instances Is Nothing OrElse Instances.Count = 0 Then Return Nothing
        If Not String.IsNullOrWhiteSpace(PreferredId) Then
            Dim Preferred = Instances.FirstOrDefault(Function(Instance) Instance.Attached AndAlso String.Equals(Instance.Id, PreferredId, StringComparison.OrdinalIgnoreCase))
            If Preferred IsNot Nothing Then Return Preferred
        End If
        Dim Current = Instances.FirstOrDefault(Function(Instance) Instance.Attached AndAlso Instance.IsServerManaged)
        If Current IsNot Nothing Then Return Current
        Current = Instances.FirstOrDefault(Function(Instance) Instance.IsServerManaged)
        If Current IsNot Nothing Then Return Current
        Current = Instances.FirstOrDefault(Function(Instance) Instance.Attached)
        If Current IsNot Nothing Then Return Current
        Return Instances.First()
    End Function

    Private Shared Function NonEmpty(ParamArray Values As String()) As String
        For Each Value In Values
            If Not String.IsNullOrWhiteSpace(Value) Then Return Value
        Next
        Return "--"
    End Function

    Private Shared Function FormatDuration(Duration As Double) As String
        If Duration <= 0 Then Return ""
        Dim TotalSeconds = CInt(Math.Round(Duration))
        Return $"{TotalSeconds \ 60}:{(TotalSeconds Mod 60).ToString("00")}"
    End Function

    Private Async Function SendPlaybackCommandAsync(Command As String) As Task
        If String.IsNullOrWhiteSpace(CurrentBaseUrl) OrElse String.IsNullOrWhiteSpace(ActiveInstanceId) Then Return
        Try
            Await OmniMixApiClient.SendInstanceCommandAsync(CurrentBaseUrl, ActiveInstanceId, Command)
            RefreshPlayerAsync()
        Catch Ex As Exception
            Hint("播放控制失败：" & Ex.Message, HintType.Red)
        End Try
    End Function

    Public Async Sub HandleMediaCommand(Command As String)
        Await SendPlaybackCommandAsync(Command)
    End Sub

    Private Async Sub BtnLeftPlaybackPrev_Click(sender As Object, e As EventArgs) Handles BtnLeftPlaybackPrev.Click
        Await SendPlaybackCommandAsync("prev")
    End Sub

    Private Async Sub BtnLeftPlaybackToggle_Click(sender As Object, e As EventArgs) Handles BtnLeftPlaybackToggle.Click
        Await SendPlaybackCommandAsync("toggle")
    End Sub

    Private Async Sub BtnLeftPlaybackNext_Click(sender As Object, e As EventArgs) Handles BtnLeftPlaybackNext.Click
        Await SendPlaybackCommandAsync("next")
    End Sub

    Private Async Sub BtnLeftPlaybackMode_Click(sender As Object, e As EventArgs) Handles BtnLeftPlaybackMode.Click
        If String.IsNullOrWhiteSpace(CurrentBaseUrl) OrElse String.IsNullOrWhiteSpace(ActiveInstanceId) Then Return
        Dim TargetMode = NextPlaybackMode(ActivePlaybackMode)
        Try
            Dim TaskRepeat As System.Threading.Tasks.Task = System.Threading.Tasks.Task.CompletedTask
            Dim TaskShuffle As System.Threading.Tasks.Task = System.Threading.Tasks.Task.CompletedTask
            Select Case TargetMode
                Case PlaybackMode.Shuffle
                    TaskRepeat = OmniMixApiClient.SetInstanceRepeatModeAsync(CurrentBaseUrl, ActiveInstanceId, "all")
                    TaskShuffle = OmniMixApiClient.SetInstanceShuffleAsync(CurrentBaseUrl, ActiveInstanceId, True)
                Case PlaybackMode.RepeatOne
                    TaskShuffle = OmniMixApiClient.SetInstanceShuffleAsync(CurrentBaseUrl, ActiveInstanceId, False)
                    TaskRepeat = OmniMixApiClient.SetInstanceRepeatModeAsync(CurrentBaseUrl, ActiveInstanceId, "one")
                Case Else
                    TaskShuffle = OmniMixApiClient.SetInstanceShuffleAsync(CurrentBaseUrl, ActiveInstanceId, False)
                    TaskRepeat = OmniMixApiClient.SetInstanceRepeatModeAsync(CurrentBaseUrl, ActiveInstanceId, "all")
            End Select
            Await System.Threading.Tasks.Task.WhenAll(TaskRepeat, TaskShuffle)
            Hint("已切换为" & PlaybackModeText(TargetMode) & "。", HintType.Green)
            RefreshPlayerAsync()
        Catch Ex As Exception
            Hint("播放模式切换失败：" & Ex.Message, HintType.Red)
        End Try
    End Sub

    Private Sub SliderLeftProgress_Change(sender As Object, user As Boolean) Handles SliderLeftProgress.Change
        If IsUpdatingPlaybackUi Then Return
        If Not CanControlActiveInstance OrElse ActiveDuration <= 0 Then Return
        If String.IsNullOrWhiteSpace(CurrentBaseUrl) OrElse String.IsNullOrWhiteSpace(ActiveInstanceId) Then Return

        ActivePosition = Math.Max(0, Math.Min(ActiveDuration, SliderLeftProgress.Value))
        LabLeftPlaybackElapsed.Text = FormatDuration(ActivePosition)
        PendingSeekPosition = ActivePosition
        SeekDebounceTimer.Stop()
        SeekDebounceTimer.Start()
    End Sub

    Private Async Sub SeekDebounceTimer_Tick(sender As Object, e As EventArgs)
        SeekDebounceTimer.Stop()
        If ReferenceEquals(SliderLeftProgress, DragControl) Then
            SeekDebounceTimer.Start()
            Return
        End If
        If IsSendingSeek Then
            SeekDebounceTimer.Start()
            Return
        End If
        If PendingSeekPosition < 0 Then Return
        If Not CanControlActiveInstance OrElse String.IsNullOrWhiteSpace(CurrentBaseUrl) OrElse String.IsNullOrWhiteSpace(ActiveInstanceId) Then Return

        Dim TargetPosition = PendingSeekPosition
        PendingSeekPosition = -1
        IsSendingSeek = True
        Try
            Await OmniMixApiClient.SeekInstanceAsync(CurrentBaseUrl, ActiveInstanceId, TargetPosition)
        Catch Ex As Exception
            Hint("进度调整失败：" & Ex.Message, HintType.Red)
        Finally
            IsSendingSeek = False
            If PendingSeekPosition >= 0 Then SeekDebounceTimer.Start()
        End Try
    End Sub

    Private Async Sub SliderLeftVolume_Change(sender As Object, user As Boolean) Handles SliderLeftVolume.Change
        If IsUpdatingPlaybackUi Then Return
        If Not CanControlActiveInstance OrElse String.IsNullOrWhiteSpace(CurrentBaseUrl) OrElse String.IsNullOrWhiteSpace(ActiveInstanceId) Then Return

        Dim Volume = SliderLeftVolume.Value / 100.0
        LabLeftPlaybackVolume.Text = SliderLeftVolume.Value & "%"
        Try
            Await OmniMixApiClient.SetInstanceVolumeAsync(CurrentBaseUrl, ActiveInstanceId, Volume)
        Catch Ex As Exception
            Hint("音量保存失败：" & Ex.Message, HintType.Red)
        End Try
    End Sub

    Private Async Sub SliderLeftLatency_Change(sender As Object, user As Boolean) Handles SliderLeftLatency.Change
        If IsUpdatingPlaybackUi Then Return
        If Not CanControlActiveInstance OrElse String.IsNullOrWhiteSpace(CurrentBaseUrl) OrElse String.IsNullOrWhiteSpace(ActiveInstanceId) Then Return

        Dim Latency = Math.Max(0.03, Math.Min(1, SliderLeftLatency.Value / 1000.0))
        LabLeftPlaybackLatency.Text = Latency.ToString("0.00", CultureInfo.InvariantCulture) & "s"
        Try
            Await OmniMixApiClient.SetInstanceLatencyAsync(CurrentBaseUrl, ActiveInstanceId, Latency)
        Catch Ex As Exception
            Hint("缓冲延迟保存失败：" & Ex.Message, HintType.Red)
        End Try
    End Sub

    Private Shared Function TryParseNumber(Text As String, ByRef Value As Double) As Boolean
        If Double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, Value) Then Return True
        Return Double.TryParse(Text, NumberStyles.Float, CultureInfo.CurrentCulture, Value)
    End Function

    Private Shared Function ConfigString(Config As Dictionary(Of String, JsonElement), Key As String, Fallback As String) As String
        If Config Is Nothing OrElse Not Config.ContainsKey(Key) Then Return Fallback
        Dim Value = Config(Key)
        Select Case Value.ValueKind
            Case JsonValueKind.String
                Return NonEmpty(Value.GetString(), Fallback)
            Case JsonValueKind.Number, JsonValueKind.True, JsonValueKind.False
                Return Value.ToString()
            Case Else
                Return Fallback
        End Select
    End Function

    Private Async Sub RefreshLibrarySourcesAsync()
        If CurrentPage <> FormMain.PageType.Download Then Return

        PanLibraryNav.Children.Clear()
        LibrarySourceItems.Clear()

        If Not IsOnline OrElse String.IsNullOrWhiteSpace(CurrentBaseUrl) Then
            AddLibrarySourcePlaceholder("等待连接")
            CurrentLibrarySourceId = ""
            CurrentRight?.SetLibraryPane("")
            Return
        End If

        Try
            Dim Playlist = Await OmniMixApiClient.GetPlaylistAsync(CurrentBaseUrl)
            Dim Modules As List(Of OmniMixModuleInfo) = Nothing
            Try
                Modules = Await OmniMixApiClient.GetModulesAsync(CurrentBaseUrl)
            Catch
                Modules = New List(Of OmniMixModuleInfo)
            End Try

            Dim SourceIds As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each SongInfo In If(Playlist.Songs, New List(Of OmniMixSongInfo))
                If Not String.IsNullOrWhiteSpace(SongInfo.ModuleId) Then SourceIds.Add(SongInfo.ModuleId)
            Next
            For Each AlbumInfo In If(Playlist.Albums, New List(Of OmniMixAlbumInfo))
                If Not String.IsNullOrWhiteSpace(AlbumInfo.ModuleId) Then SourceIds.Add(AlbumInfo.ModuleId)
            Next
            For Each TagInfo In If(Playlist.Tags, New List(Of OmniMixTagInfo))
                If Not String.IsNullOrWhiteSpace(TagInfo.ModuleId) Then SourceIds.Add(TagInfo.ModuleId)
            Next

            If SourceIds.Count = 0 Then
                AddLibrarySourcePlaceholder("暂无来源")
                CurrentLibrarySourceId = ""
                CurrentRight?.SetLibraryPane("")
                Return
            End If

            Dim OrderedSources = SourceIds.
                Select(Function(Id) New With {.Id = Id, .Name = ResolveLibrarySourceName(Id, Modules)}).
                OrderBy(Function(Source) Source.Name).
                ToList()
            Dim SelectedId = If(SourceIds.Contains(CurrentLibrarySourceId), CurrentLibrarySourceId, OrderedSources.First().Id)

            For Each Source In OrderedSources
                Dim Item As New MyListItem With {
                    .Title = Source.Name,
                    .Info = Source.Id,
                    .Type = MyListItem.CheckType.RadioBox,
                    .Tag = Source.Id,
                    .MinPaddingRight = 35,
                    .Height = 36,
                    .VerticalAlignment = VerticalAlignment.Top,
                    .Checked = String.Equals(Source.Id, SelectedId, StringComparison.OrdinalIgnoreCase),
                    .IsScaleAnimationEnabled = False
                }
                AddHandler Item.Check, AddressOf LibraryNav_Check
                LibrarySourceItems(Source.Id) = Item
                PanLibraryNav.Children.Add(Item)
            Next

            CurrentLibrarySourceId = SelectedId
            CurrentRight?.SetLibraryPane(SelectedId)
        Catch Ex As Exception
            AddLibrarySourcePlaceholder("读取失败")
            Hint("曲库来源读取失败：" & Ex.Message, HintType.Red)
        End Try
    End Sub

    Private Sub AddLibrarySourcePlaceholder(Title As String)
        PanLibraryNav.Children.Add(New MyListItem With {
            .Title = Title,
            .Type = MyListItem.CheckType.Clickable,
            .MinPaddingRight = 35,
            .Height = 36,
            .VerticalAlignment = VerticalAlignment.Top,
            .IsScaleAnimationEnabled = False
        })
    End Sub

    Private Shared Function ResolveLibrarySourceName(ModuleId As String, Modules As List(Of OmniMixModuleInfo)) As String
        Dim ModuleInfo = If(Modules, New List(Of OmniMixModuleInfo)).
            FirstOrDefault(Function(Item) String.Equals(Item.Id, ModuleId, StringComparison.OrdinalIgnoreCase))
        If ModuleInfo IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(ModuleInfo.Name) Then Return ModuleInfo.Name

        Select Case If(ModuleId, "").Trim().ToLowerInvariant()
            Case "netease", "chillpatcher.module.netease"
                Return "网易云音乐"
            Case "qqmusic", "qq_music", "chillpatcher.module.qqmusic"
                Return "QQ音乐"
            Case "spotify", "chillpatcher.module.spotify"
                Return "Spotify"
            Case "localfolder", "local_folder", "chillpatcher.module.localfolder"
                Return "本地文件夹"
            Case "bilibili", "chillpatcher.module.bilibili"
                Return "哔哩哔哩"
            Case Else
                Return NonEmpty(ModuleId, "未知来源")
        End Select
    End Function

    Private Sub LibraryNav_Check(sender As Object, e As RouteEventArgs)
        Dim Item = TryCast(sender, FrameworkElement)
        If CurrentRight Is Nothing OrElse Item Is Nothing OrElse Item.Tag Is Nothing Then Return
        CurrentLibrarySourceId = Item.Tag.ToString()
        CurrentRight.SetLibraryPane(CurrentLibrarySourceId)
    End Sub

    Private Sub ModuleNav_Check(sender As FrameworkElement, e As RouteEventArgs) Handles ItemModuleLaunchpad.Check, ItemModuleMod.Check, ItemModuleGameIntegration.Check
        If CurrentRight Is Nothing OrElse sender.Tag Is Nothing Then Return
        CurrentRight.SetModulesPane(sender.Tag.ToString())
    End Sub

    Private Sub SettingsNav_Check(sender As FrameworkElement, e As RouteEventArgs) Handles ItemSettingsConfig.Check, ItemSettingsPersonalization.Check, ItemSettingsMaintenance.Check, ItemSettingsInstances.Check, ItemSettingsEqualizer.Check, ItemSettingsArchives.Check
        If CurrentRight Is Nothing OrElse sender.Tag Is Nothing Then Return
        CurrentRight.SetSettingsPane(sender.Tag.ToString())
    End Sub

End Class
