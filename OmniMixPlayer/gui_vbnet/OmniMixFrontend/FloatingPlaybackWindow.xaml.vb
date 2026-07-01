Imports System.Windows.Threading
Imports System.Text.Json
Imports System.IO
Imports System.Text
Imports System.Windows.Media
Imports System.Windows.Media.Imaging

Public Class FloatingPlaybackWindow

    Private Shared ReadOnly CoverHttpClient As New System.Net.Http.HttpClient()
    Private ReadOnly RefreshTimer As DispatcherTimer
    Private IsRefreshing As Boolean
    Private LastBaseUrl As String = ""
    Private ActiveInstanceId As String = ""
    Private CurrentCoverSource As String = ""
    Private CoverLoadSerial As Integer = 0
    Private CurrentLyricUuid As String = ""
    Private CurrentLyricLines As New List(Of OmniMixLyricLineInfo)
    Private ReadOnly LyricCache As New Dictionary(Of String, List(Of OmniMixLyricLineInfo))(StringComparer.OrdinalIgnoreCase)
    Private Const IconPrev As String = "M704 256v512L320 512l384-256zM224 256h96v512h-96z"
    Private Const IconNext As String = "M320 256v512l384-256-384-256zM704 256h96v512h-96z"
    Private Const IconPlay As String = "M352 224v672l480-336-480-336z"
    Private Const IconPause As String = "M320 224h160v672H320zM576 224h160v672H576z"

    Private ActiveDuration As Double = 0
    Private ActivePosition As Double = 0
    Private WithEvents SeekDebounceTimer As DispatcherTimer
    Private PendingSeekPosition As Double = -1
    Private IsSendingSeek As Boolean = False

    Public Sub New()
        InitializeComponent()
        SetupCommandButtonIcons()
        RefreshTimer = New DispatcherTimer With {.Interval = TimeSpan.FromSeconds(1)}
        AddHandler RefreshTimer.Tick, AddressOf RefreshTimer_Tick

        SeekDebounceTimer = New DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(450)}
        AddHandler SeekDebounceTimer.Tick, AddressOf SeekDebounceTimer_Tick
    End Sub

    Private Sub FloatingPlaybackWindow_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        If Left <= 0 AndAlso Top <= 0 Then
            Left = Math.Max(0, SystemParameters.WorkArea.Right - Width - 24)
            Top = Math.Max(0, SystemParameters.WorkArea.Bottom - Height - 32)
        End If
        ApplyAppearance()
        EnsureTopmost()
        RefreshTimer.Start()
        RefreshTimer_Tick(Nothing, EventArgs.Empty)
    End Sub

    Private Sub FloatingPlaybackWindow_Closed(sender As Object, e As EventArgs) Handles Me.Closed
        RefreshTimer.Stop()
    End Sub

    Private Sub FloatingPlaybackWindow_MouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs) Handles PanRoot.MouseLeftButtonDown
        If e.ButtonState <> MouseButtonState.Pressed Then Return
        If IsDragBlockedSource(TryCast(e.OriginalSource, DependencyObject)) Then Return
        Try
            DragMove()
        Catch
        End Try
    End Sub

    Private Shared Function IsDragBlockedSource(Source As DependencyObject) As Boolean
        Dim Current = Source
        While Current IsNot Nothing
            If TypeOf Current Is MyIconButton OrElse TypeOf Current Is Button Then Return True
            Current = VisualTreeHelper.GetParent(Current)
        End While
        Return False
    End Function



    Private Async Sub BtnPrev_Click(sender As Object, e As EventArgs) Handles BtnPrev.Click
        Await SendPlaybackCommandAsync("prev")
    End Sub

    Private Async Sub BtnToggle_Click(sender As Object, e As EventArgs) Handles BtnToggle.Click
        Await SendPlaybackCommandAsync("toggle")
    End Sub

    Private Async Sub BtnNext_Click(sender As Object, e As EventArgs) Handles BtnNext.Click
        Await SendPlaybackCommandAsync("next")
    End Sub

    Public Sub ApplyAppearance()
        Try
            ApplyLayoutStyle()
            
            ' 1. 计算 UI 配色主题 (Color1 到 Color8)
            Dim UiThemeId = Settings.Get(Of Integer)("OmniMixFloatingWindowTheme")
            Dim UiHue As Integer = 210
            Dim UiSat As Integer = 85
            Dim UiLightAdjust As Integer = 0
            
            Select Case UiThemeId
                Case 1
                    UiHue = 175
                    UiSat = 72
                    UiLightAdjust = 1
                Case 2
                    UiHue = 122
                    UiSat = 72
                    UiLightAdjust = 0
                Case 3
                    UiHue = 48
                    UiSat = 90
                    UiLightAdjust = 3
                Case 4
                    UiHue = 28
                    UiSat = 62
                    UiLightAdjust = -1
                Case 5
                    UiHue = 215
                    UiSat = 18
                    UiLightAdjust = -18
                Case 6
                    UiHue = 330
                    UiSat = 72
                    UiLightAdjust = 0
                Case 7
                    UiHue = 272
                    UiSat = 78
                    UiLightAdjust = -1
                Case 8
                    UiHue = 43
                    UiSat = 76
                    UiLightAdjust = 0
                Case 9
                    UiHue = 24
                    UiSat = 86
                    UiLightAdjust = 0
                Case 10
                    UiHue = 355
                    UiSat = 78
                    UiLightAdjust = -1
                Case 11
                    UiHue = 198
                    UiSat = 92
                    UiLightAdjust = -2
                Case 12, 13
                    UiHue = 292
                    UiSat = 82
                    UiLightAdjust = 0
                Case 14
                    UiHue = Settings.Get(Of Integer)("OmniMixFloatingWindowThemeHue")
                    UiSat = Settings.Get(Of Integer)("OmniMixFloatingWindowThemeSat")
                    UiLightAdjust = Settings.Get(Of Integer)("OmniMixFloatingWindowThemeLight") - 20
                Case Else
                    UiHue = 210
                    UiSat = 85
                    UiLightAdjust = 0
            End Select

            Dim Color1 As Color
            Dim Color2 As Color
            Dim Color3 As Color
            Dim Color4 As Color
            Dim Color5 As Color
            Dim Color6 As Color
            Dim Color7 As Color
            Dim Color8 As Color

            If UiThemeId = 15 Then
                Color1 = Color.FromRgb(200, 200, 200)
                Color2 = Color.FromRgb(255, 255, 255)
                Color3 = Color.FromRgb(240, 240, 240)
                Color4 = Color.FromRgb(220, 220, 220)
                Color5 = Color.FromRgb(245, 245, 245)
                Color6 = Color.FromRgb(250, 250, 250)
                Color7 = Color.FromRgb(252, 252, 252)
                Color8 = Color.FromRgb(255, 255, 255)
            Else
                Color1 = New MyColor().FromHSL2(UiHue, UiSat * 0.2, 25 + UiLightAdjust * 0.3)
                Color2 = New MyColor().FromHSL2(UiHue, UiSat, 45 + UiLightAdjust)
                Color3 = New MyColor().FromHSL2(UiHue, UiSat, 55 + UiLightAdjust)
                Color4 = New MyColor().FromHSL2(UiHue, UiSat, 65 + UiLightAdjust)
                Color5 = New MyColor().FromHSL2(UiHue, UiSat, 80 + UiLightAdjust * 0.4)
                Color6 = New MyColor().FromHSL2(UiHue, UiSat, 91 + UiLightAdjust * 0.1)
                Color7 = New MyColor().FromHSL2(UiHue, UiSat, 95)
                Color8 = New MyColor().FromHSL2(UiHue, UiSat, 97)
            End If

            Resources("ColorBrush1") = New SolidColorBrush(Color1)
            Resources("ColorBrush2") = New SolidColorBrush(Color2)
            Resources("ColorBrush3") = New SolidColorBrush(Color3)
            Resources("ColorBrush4") = New SolidColorBrush(Color4)
            Resources("ColorBrush5") = New SolidColorBrush(Color5)
            Resources("ColorBrush6") = New SolidColorBrush(Color6)
            Resources("ColorBrush7") = New SolidColorBrush(Color7)
            Resources("ColorBrush8") = New SolidColorBrush(Color8)
            Resources("ColorBrushBg0") = New SolidColorBrush(Color2)

            Resources("ColorObject1") = CType(Color1, Color)
            Resources("ColorObject2") = CType(Color2, Color)
            Resources("ColorObject3") = CType(Color3, Color)
            Resources("ColorObject4") = CType(Color4, Color)
            Resources("ColorObject5") = CType(Color5, Color)
            Resources("ColorObject6") = CType(Color6, Color)
            Resources("ColorObject7") = CType(Color7, Color)
            Resources("ColorObject8") = CType(Color8, Color)

            ' 2. 计算背景配色主题 (ThemeColor)
            Dim BgThemeId = Settings.Get(Of Integer)("OmniMixFloatingWindowBackgroundTheme")
            Dim BgHue As Integer = 210
            Dim BgSat As Integer = 85
            Dim BgLightAdjust As Integer = 0
            
            Select Case BgThemeId
                Case 1
                    BgHue = 175
                    BgSat = 72
                    BgLightAdjust = 1
                Case 2
                    BgHue = 122
                    BgSat = 72
                    BgLightAdjust = 0
                Case 3
                    BgHue = 48
                    BgSat = 90
                    BgLightAdjust = 3
                Case 4
                    BgHue = 28
                    BgSat = 62
                    BgLightAdjust = -1
                Case 5
                    BgHue = 215
                    BgSat = 18
                    BgLightAdjust = -18
                Case 6
                    BgHue = 330
                    BgSat = 72
                    BgLightAdjust = 0
                Case 7
                    BgHue = 272
                    BgSat = 78
                    BgLightAdjust = -1
                Case 8
                    BgHue = 43
                    BgSat = 76
                    BgLightAdjust = 0
                Case 9
                    BgHue = 24
                    BgSat = 86
                    BgLightAdjust = 0
                Case 10
                    BgHue = 355
                    BgSat = 78
                    BgLightAdjust = -1
                Case 11
                    BgHue = 198
                    BgSat = 92
                    BgLightAdjust = -2
                Case 12, 13
                    BgHue = 292
                    BgSat = 82
                    BgLightAdjust = 0
                Case 14
                    BgHue = Settings.Get(Of Integer)("OmniMixFloatingWindowBackgroundThemeHue")
                    BgSat = Settings.Get(Of Integer)("OmniMixFloatingWindowBackgroundThemeSat")
                    BgLightAdjust = Settings.Get(Of Integer)("OmniMixFloatingWindowBackgroundThemeLight") - 20
                Case Else
                    BgHue = 210
                    BgSat = 85
                    BgLightAdjust = 0
            End Select
            Dim ThemeColor = CType(New MyColor().FromHSL2(BgHue, BgSat, 45 + BgLightAdjust), Color)

            ' 3. 应用背景和边框不透明度
            Dim WindowOpacityPercent = Math.Max(30, Math.Min(100, Settings.Get(Of Integer)("OmniMixFloatingWindowOpacity")))
            Dim BackgroundOpacityPercent = Math.Max(0, Math.Min(100, Settings.Get(Of Integer)("OmniMixFloatingWindowBackgroundOpacity")))
            Opacity = WindowOpacityPercent / 100.0
            Dim Alpha = CByte(Math.Max(0, Math.Min(255, CInt(BackgroundOpacityPercent / 100.0 * 255))))
            Dim BackgroundColor = Color.FromArgb(Alpha, ThemeColor.R, ThemeColor.G, ThemeColor.B)
            PanRoot.Background = New SolidColorBrush(BackgroundColor)
            ' 边框微调使用 UI 配色主题
            PanRoot.BorderBrush = New SolidColorBrush(Color.FromArgb(CByte(Math.Min(210, Alpha)), Color2.R, Color2.G, Color2.B))

            ' 4. 应用前景色（包括控制按钮的动画等）
            PanCover.Background = New SolidColorBrush(Color.FromArgb(CByte(Math.Max(30, Alpha \ 4)), Color2.R, Color2.G, Color2.B))

            ' 5. 将文字前景色绑定为 UI 颜色专属 HSL 配色
            LabTitle.Foreground = New SolidColorBrush(Color2)
            LabLyricCurrent.Foreground = New SolidColorBrush(Color2)
            LabMeta.Foreground = New SolidColorBrush(Color.FromArgb(210, Color2.R, Color2.G, Color2.B))
            LabLyricNext.Foreground = New SolidColorBrush(Color.FromArgb(160, Color2.R, Color2.G, Color2.B))
            LabState.Foreground = New SolidColorBrush(Color.FromArgb(190, Color2.R, Color2.G, Color2.B))
            LabTime.Foreground = New SolidColorBrush(Color.FromArgb(190, Color2.R, Color2.G, Color2.B))
        Catch Ex As Exception
            Logger.Warn(Ex, "刷新 OmniMix 悬浮窗外观失败")
        End Try
    End Sub

    Private Sub ApplyLayoutStyle()
        Dim Style = Settings.Get(Of Integer)("OmniMixFloatingWindowStyle")
        Dim Scale = GetFloatingWindowScale()
        PanRoot.LayoutTransform = New ScaleTransform(Scale, Scale)
        If Style = 1 Then
            MinWidth = 260 * Scale
            MinHeight = 360 * Scale
            Width = 280 * Scale
            Height = 374 * Scale
            PanRoot.Padding = New Thickness(14)

            PanLayout.ColumnDefinitions(0).Width = New GridLength(1, GridUnitType.Star)
            PanLayout.ColumnDefinitions(1).Width = New GridLength(0)
            PanLayout.ColumnDefinitions(2).Width = GridLength.Auto
            PanLayout.RowDefinitions(0).Height = GridLength.Auto
            PanLayout.RowDefinitions(1).Height = GridLength.Auto
            PanLayout.RowDefinitions(2).Height = GridLength.Auto
            PanLayout.RowDefinitions(3).Height = GridLength.Auto

            Grid.SetRow(PanCover, 0)
            Grid.SetColumn(PanCover, 0)
            Grid.SetColumnSpan(PanCover, 3)
            PanCover.Width = 96
            PanCover.Height = 96
            PanCover.HorizontalAlignment = HorizontalAlignment.Center
            PanCover.VerticalAlignment = VerticalAlignment.Top
            PanCover.Margin = New Thickness(0, 4, 0, 0)

            Grid.SetRow(PanInfo, 1)
            Grid.SetColumn(PanInfo, 0)
            Grid.SetColumnSpan(PanInfo, 3)
            Grid.SetRowSpan(PanInfo, 1)
            PanInfo.Margin = New Thickness(0, 12, 0, 0)
            LabTitle.TextAlignment = TextAlignment.Center
            LabMeta.TextAlignment = TextAlignment.Center
            LabLyricCurrent.TextAlignment = TextAlignment.Center
            LabLyricNext.TextAlignment = TextAlignment.Center
            LabLyricCurrent.MaxHeight = 38
            LabLyricNext.MaxHeight = 34

            Grid.SetRow(PanProgress, 2)
            Grid.SetColumn(PanProgress, 0)
            Grid.SetColumnSpan(PanProgress, 3)
            PanProgress.VerticalAlignment = VerticalAlignment.Center
            PanProgress.Margin = New Thickness(0, 12, 0, 0)

            Grid.SetRow(PanCommandButtons, 3)
            Grid.SetColumn(PanCommandButtons, 0)
            Grid.SetColumnSpan(PanCommandButtons, 3)
            PanCommandButtons.HorizontalAlignment = HorizontalAlignment.Center
            PanCommandButtons.VerticalAlignment = VerticalAlignment.Center
            PanCommandButtons.Margin = New Thickness(0, 12, 0, 10)
            SetCommandButtonSizes(34, 42, 12)
        Else
            MinWidth = 390 * Scale
            MinHeight = 210 * Scale
            Width = 430 * Scale
            Height = 224 * Scale
            PanRoot.Padding = New Thickness(12)

            PanLayout.ColumnDefinitions(0).Width = New GridLength(116)
            PanLayout.ColumnDefinitions(1).Width = New GridLength(1, GridUnitType.Star)
            PanLayout.ColumnDefinitions(2).Width = GridLength.Auto
            PanLayout.RowDefinitions(0).Height = GridLength.Auto
            PanLayout.RowDefinitions(1).Height = New GridLength(6)
            PanLayout.RowDefinitions(2).Height = GridLength.Auto
            PanLayout.RowDefinitions(3).Height = New GridLength(0)

            Grid.SetRow(PanCover, 0)
            Grid.SetColumn(PanCover, 0)
            Grid.SetColumnSpan(PanCover, 1)
            PanCover.Width = 92
            PanCover.Height = 92
            PanCover.HorizontalAlignment = HorizontalAlignment.Left
            PanCover.VerticalAlignment = VerticalAlignment.Top
            PanCover.Margin = New Thickness(0)

            Grid.SetRow(PanInfo, 0)
            Grid.SetColumn(PanInfo, 1)
            Grid.SetColumnSpan(PanInfo, 1)
            Grid.SetRowSpan(PanInfo, 1)
            PanInfo.Margin = New Thickness(0)
            LabTitle.TextAlignment = TextAlignment.Left
            LabMeta.TextAlignment = TextAlignment.Left
            LabLyricCurrent.TextAlignment = TextAlignment.Left
            LabLyricNext.TextAlignment = TextAlignment.Left
            LabLyricCurrent.MaxHeight = 38
            LabLyricNext.MaxHeight = 18

            Grid.SetRow(PanProgress, 2)
            Grid.SetColumn(PanProgress, 1)
            Grid.SetColumnSpan(PanProgress, 1)
            PanProgress.VerticalAlignment = VerticalAlignment.Center
            PanProgress.Margin = New Thickness(0, 6, 0, 0)

            Grid.SetRow(PanCommandButtons, 2)
            Grid.SetColumn(PanCommandButtons, 0)
            Grid.SetColumnSpan(PanCommandButtons, 1)
            PanCommandButtons.HorizontalAlignment = HorizontalAlignment.Center
            PanCommandButtons.VerticalAlignment = VerticalAlignment.Center
            PanCommandButtons.Margin = New Thickness(0, 8, 0, 8)
            SetCommandButtonSizes(28, 36, 7)
        End If
        KeepInsideWorkArea()
    End Sub

    Private Sub SetupCommandButtonIcons()
        BtnPrev.Logo = IconPrev
        BtnToggle.Logo = IconPlay
        BtnNext.Logo = IconNext
    End Sub

    Private Sub SetCommandButtonSizes(SideSize As Double, ToggleSize As Double, Gap As Double)
        BtnPrev.Width = SideSize
        BtnPrev.Height = SideSize
        BtnPrev.Margin = New Thickness(0, 0, Gap, 0)
        BtnToggle.Width = ToggleSize
        BtnToggle.Height = ToggleSize
        BtnToggle.Margin = New Thickness(0, 0, Gap, 0)
        BtnNext.Width = SideSize
        BtnNext.Height = SideSize
        BtnNext.Margin = New Thickness(0)
    End Sub

    Private Shared Function GetFloatingWindowScale() As Double
        Dim Percent = Math.Max(70, Math.Min(140, Settings.Get(Of Integer)("OmniMixFloatingWindowScale")))
        Return Percent / 100.0
    End Function

    Private Sub KeepInsideWorkArea()
        If Not IsLoaded Then Return
        Dim Area = SystemParameters.WorkArea
        If Left + Width > Area.Right Then Left = Math.Max(Area.Left, Area.Right - Width - 24)
        If Top + Height > Area.Bottom Then Top = Math.Max(Area.Top, Area.Bottom - Height - 32)
        If Left < Area.Left Then Left = Area.Left
        If Top < Area.Top Then Top = Area.Top
    End Sub

    Public Sub EnsureTopmost()
        Topmost = False
        Topmost = True
    End Sub

    Private Async Sub RefreshTimer_Tick(sender As Object, e As EventArgs)
        If IsRefreshing Then Return
        IsRefreshing = True
        Try
            Await RefreshPlaybackAsync()
        Catch Ex As Exception
            RenderStatus("播放状态读取失败", Ex.Message, "--", "--", 0)
        Finally
            IsRefreshing = False
        End Try
    End Sub

    Private Async Function RefreshPlaybackAsync() As Task
        Dim BaseUrl = LastBaseUrl
        If String.IsNullOrWhiteSpace(BaseUrl) Then
            Dim Status = Await OmniMixApiClient.DiscoverAsync()
            If Status Is Nothing OrElse Not Status.IsOnline OrElse String.IsNullOrWhiteSpace(Status.BaseUrl) Then
                LastBaseUrl = ""
                RenderStatus("等待播放实例...", "OmniMix 后端未连接", "--", "--", 0)
                ClearLyrics("歌词等待中...")
                Return
            End If
            BaseUrl = Status.BaseUrl
            LastBaseUrl = BaseUrl
        End If

        Dim Instances As List(Of OmniMixPlaybackInstanceInfo)
        Dim Config As Dictionary(Of String, JsonElement)
        Try
            Instances = Await OmniMixApiClient.GetInstancesAsync(BaseUrl)
            Config = Await OmniMixApiClient.GetConfigAsync(BaseUrl)
        Catch
            LastBaseUrl = ""
            Throw
        End Try

        Dim ActiveInstance = PickActiveInstance(Instances, ConfigString(Config, "active_instance", ""))
        If ActiveInstance Is Nothing Then
            ActiveInstanceId = ""
            ActiveDuration = 0
            ActivePosition = 0
            UpdateCommandButtons(False, False)
            RenderStatus("没有曲目正在播放", "等待游戏或桌面播放实例连接", "--", "--", 0)
            ClearCoverImage()
            ClearLyrics("暂无歌词")
            Return
        End If
        ActiveInstanceId = ActiveInstance.Id

        Dim Track = ActiveInstance.CurrentTrack
        Dim Title = "暂无曲目"
        Dim MetaParts As New List(Of String)
        Dim Duration As Double = 0
        If Track IsNot Nothing Then
            Title = NonEmpty(Track.Title, Track.Uuid)
            If String.IsNullOrWhiteSpace(Title) Then Title = If(ActiveInstance.IsPlaying, "正在播放", "暂无曲目")
            If Not String.IsNullOrWhiteSpace(Track.Artist) Then MetaParts.Add(Track.Artist)
            If Not String.IsNullOrWhiteSpace(Track.ModuleId) Then MetaParts.Add("来源 " & Track.ModuleId)
            Duration = Track.Duration
        End If
        If MetaParts.Count = 0 Then MetaParts.Add("实例 " & NonEmpty(ActiveInstance.Id, ActiveInstance.ClientId))

        Dim State = If(ActiveInstance.IsPlaying, "播放中", "已暂停")
        If Not ActiveInstance.Attached Then State = "离线"

        Dim IsDraggingProgress = ReferenceEquals(SliderProgress, DragControl)
        Dim PositionToShow = If(IsDraggingProgress, ActivePosition, ActiveInstance.Position)
        If Not IsDraggingProgress Then
            ActivePosition = ActiveInstance.Position
            ActiveDuration = Duration
        End If

        Dim TimeText = FormatDuration(PositionToShow) & If(Duration > 0, " / " & FormatDuration(Duration), "")
        Dim Progress = If(Duration > 0, PositionToShow / Duration, 0)
        UpdateCommandButtons(True, ActiveInstance.IsPlaying)
        RenderStatus(Title, String.Join(" · ", MetaParts), State, TimeText, Progress)
        Await EnsureLyricsForTrackAsync(BaseUrl, Track)
        RenderLyrics(PositionToShow)
        SetCoverImage(ResolveTrackCover(Track, BaseUrl))
    End Function

    Private Async Function EnsureLyricsForTrackAsync(BaseUrl As String, Track As OmniMixTrackInfo) As Task
        Dim Uuid = If(Track?.Uuid, "")
        If String.IsNullOrWhiteSpace(Uuid) Then
            ClearLyrics("暂无歌词")
            Return
        End If
        If String.Equals(CurrentLyricUuid, Uuid, StringComparison.OrdinalIgnoreCase) Then Return

        CurrentLyricUuid = Uuid
        Dim CachedLines As List(Of OmniMixLyricLineInfo) = Nothing
        If LyricCache.TryGetValue(Uuid, CachedLines) Then
            CurrentLyricLines = CachedLines
            Return
        End If

        LabLyricCurrent.Text = "歌词加载中..."
        LabLyricNext.Text = " "
        Try
            Dim Lyric = Await OmniMixApiClient.GetTrackLyricAsync(BaseUrl, Uuid)
            Dim Lines = OmniMixLyricHelper.ParseCombinedLyrics(Lyric?.Lrc, Lyric?.Tlyric, Lyric?.Rlyric)
            LyricCache(Uuid) = Lines
            If String.Equals(CurrentLyricUuid, Uuid, StringComparison.OrdinalIgnoreCase) Then
                CurrentLyricLines = Lines
            End If
        Catch Ex As Exception
            Logger.Warn(Ex, $"悬浮窗加载歌词失败：{Uuid}")
            LyricCache(Uuid) = New List(Of OmniMixLyricLineInfo)
            If String.Equals(CurrentLyricUuid, Uuid, StringComparison.OrdinalIgnoreCase) Then
                CurrentLyricLines = LyricCache(Uuid)
            End If
        End Try
    End Function

    Private Sub RenderLyrics(Position As Double)
        If CurrentLyricLines Is Nothing OrElse CurrentLyricLines.Count = 0 Then
            LabLyricCurrent.Text = "暂无歌词"
            LabLyricNext.Text = " "
            Return
        End If

        Dim Index = OmniMixLyricHelper.GetCurrentLineIndex(CurrentLyricLines, Position)
        If Index < 0 Then
            LabLyricCurrent.Text = OmniMixLyricHelper.FormatLyricLine(CurrentLyricLines(0), True)
            LabLyricNext.Text = If(CurrentLyricLines.Count > 1, OmniMixLyricHelper.FormatLyricLine(CurrentLyricLines(1), True), " ")
            Return
        End If

        LabLyricCurrent.Text = OmniMixLyricHelper.FormatLyricLine(CurrentLyricLines(Index), True)
        LabLyricNext.Text = If(Index + 1 < CurrentLyricLines.Count, OmniMixLyricHelper.FormatLyricLine(CurrentLyricLines(Index + 1), True), " ")
    End Sub

    Private Sub ClearLyrics(Optional Text As String = "暂无歌词")
        CurrentLyricUuid = ""
        CurrentLyricLines = New List(Of OmniMixLyricLineInfo)
        LabLyricCurrent.Text = If(String.IsNullOrWhiteSpace(Text), "暂无歌词", Text)
        LabLyricNext.Text = " "
    End Sub

    Private Async Function SendPlaybackCommandAsync(Command As String) As Task
        Dim BaseUrl = LastBaseUrl
        Dim InstanceId = ActiveInstanceId

        SetCommandButtonsEnabled(False)
        Try
            If String.IsNullOrWhiteSpace(BaseUrl) Then
                Dim Status = Await OmniMixApiClient.DiscoverAsync()
                If Status IsNot Nothing AndAlso Status.IsOnline Then BaseUrl = Status.BaseUrl
            End If
            If String.IsNullOrWhiteSpace(BaseUrl) Then Return

            If String.IsNullOrWhiteSpace(InstanceId) Then
                Dim Instances = Await OmniMixApiClient.GetInstancesAsync(BaseUrl)
                Dim Config = Await OmniMixApiClient.GetConfigAsync(BaseUrl)
                Dim Instance = PickActiveInstance(Instances, ConfigString(Config, "active_instance", ""))
                If Instance IsNot Nothing Then InstanceId = Instance.Id
            End If
            If String.IsNullOrWhiteSpace(InstanceId) Then Return

            Await OmniMixApiClient.SendInstanceCommandAsync(BaseUrl, InstanceId, Command)
            LastBaseUrl = BaseUrl
            ActiveInstanceId = InstanceId
            Await RefreshPlaybackAsync()
        Catch Ex As Exception
            Logger.Warn(Ex, $"悬浮窗发送播放命令失败：{Command}")
            Dim ProgressRatio = If(SliderProgress.MaxValue > 0, SliderProgress.Value / SliderProgress.MaxValue, 0.0)
            RenderStatus(LabTitle.Text, "播放命令发送失败：" & Ex.Message, LabState.Text, LabTime.Text, ProgressRatio)
        Finally
            SetCommandButtonsEnabled(Not String.IsNullOrWhiteSpace(ActiveInstanceId))
        End Try
    End Function

    Private Sub RenderStatus(Title As String, Meta As String, State As String, TimeText As String, Progress As Double)
        LabTitle.Text = If(String.IsNullOrWhiteSpace(Title), "OmniMix", Title)
        LabMeta.Text = If(String.IsNullOrWhiteSpace(Meta), " ", Meta)
        LabState.Text = If(String.IsNullOrWhiteSpace(State), "--", State)
        LabTime.Text = If(String.IsNullOrWhiteSpace(TimeText), "--", TimeText)

        Dim IsDraggingProgress = ReferenceEquals(SliderProgress, DragControl)
        If Not IsDraggingProgress Then
            SliderProgress.MaxValue = If(ActiveDuration > 0, Math.Max(1, CInt(Math.Ceiling(ActiveDuration))), 100)
            SliderProgress.Value = If(ActiveDuration > 0, CInt(Math.Max(0, Math.Min(SliderProgress.MaxValue, Math.Round(ActivePosition)))), 0)
        End If
    End Sub

    Private Sub UpdateCommandButtons(HasInstance As Boolean, IsPlaying As Boolean)
        SetCommandButtonsEnabled(HasInstance)
        BtnToggle.Logo = If(IsPlaying, IconPause, IconPlay)
        BtnToggle.ToolTip = If(IsPlaying, "暂停", "播放")
    End Sub

    Private Sub SetCommandButtonsEnabled(Enabled As Boolean)
        BtnPrev.IsEnabled = Enabled
        BtnToggle.IsEnabled = Enabled
        BtnNext.IsEnabled = Enabled
        Dim Opacity = If(Enabled, 1.0, 0.45)
        BtnPrev.Opacity = Opacity
        BtnToggle.Opacity = Opacity
        BtnNext.Opacity = Opacity
    End Sub

    Private Shared Function GetThemeColor() As Color
        Try
            Dim ThemeId = Settings.Get(Of Integer)("OmniMixFloatingWindowTheme")
            Dim Hue As Integer = 210
            Dim Sat As Integer = 85
            Dim LightAdjust As Integer = 0
            
            Select Case ThemeId
                Case 1
                    Hue = 175
                    Sat = 72
                    LightAdjust = 1
                Case 2
                    Hue = 122
                    Sat = 72
                    LightAdjust = 0
                Case 3
                    Hue = 48
                    Sat = 90
                    LightAdjust = 3
                Case 4
                    Hue = 28
                    Sat = 62
                    LightAdjust = -1
                Case 5
                    Hue = 215
                    Sat = 18
                    LightAdjust = -18
                Case 6
                    Hue = 330
                    Sat = 72
                    LightAdjust = 0
                Case 7
                    Hue = 272
                    Sat = 78
                    LightAdjust = -1
                Case 8
                    Hue = 43
                    Sat = 76
                    LightAdjust = 0
                Case 9
                    Hue = 24
                    Sat = 86
                    LightAdjust = 0
                Case 10
                    Hue = 355
                    Sat = 78
                    LightAdjust = -1
                Case 11
                    Hue = 198
                    Sat = 92
                    LightAdjust = -2
                Case 12, 13
                    Hue = 292
                    Sat = 82
                    LightAdjust = 0
                Case 14
                    Hue = Settings.Get(Of Integer)("OmniMixFloatingWindowThemeHue")
                    Sat = Settings.Get(Of Integer)("OmniMixFloatingWindowThemeSat")
                    LightAdjust = Settings.Get(Of Integer)("OmniMixFloatingWindowThemeLight") - 20
                Case Else
                    Hue = 210
                    Sat = 85
                    LightAdjust = 0
            End Select

            Dim TargetColor = New MyColor().FromHSL2(Hue, Sat, 45 + LightAdjust)
            Return CType(TargetColor, Color)
        Catch Ex As Exception
            Logger.Warn(Ex, "计算悬浮窗主题色失败，回退到默认颜色")
        End Try
        Return Color.FromRgb(11, 91, 203)
    End Function

    Private Function ResolveTrackCover(Track As OmniMixTrackInfo, BaseUrl As String) As String
        If Track Is Nothing Then Return ""
        If Not String.IsNullOrWhiteSpace(Track.Uuid) AndAlso Not String.IsNullOrWhiteSpace(BaseUrl) Then
            Return BaseUrl.TrimEnd("/"c) & "/api/track/cover?uuid=" & Uri.EscapeDataString(Track.Uuid)
        End If
        For Each Candidate In {Track.CoverPath, Track.CoverUrl, Track.ImageUrl}
            If Not String.IsNullOrWhiteSpace(Candidate) Then Return Candidate
        Next
        Return ""
    End Function

    Private Async Sub SetCoverImage(Source As String)
        If String.IsNullOrWhiteSpace(Source) Then
            ClearCoverImage()
            Return
        End If

        Try
            Dim NormalizedSource = Source.Trim()
            If String.Equals(CurrentCoverSource, NormalizedSource, StringComparison.Ordinal) AndAlso ImgCover.Source IsNot Nothing Then Return

            Dim SourceUri = ResolveImageUri(NormalizedSource)
            If SourceUri Is Nothing Then
                ClearCoverImage()
                Return
            End If

            Dim LoadSerial = Threading.Interlocked.Increment(CoverLoadSerial)
            CurrentCoverSource = NormalizedSource

            Dim Bitmap = Await LoadCoverBitmapAsync(SourceUri)
            If LoadSerial <> CoverLoadSerial OrElse Not String.Equals(CurrentCoverSource, NormalizedSource, StringComparison.Ordinal) Then Return

            ImgCover.Source = Bitmap
            ImgCover.Visibility = Visibility.Visible
            PathCoverPlaceholder.Visibility = Visibility.Collapsed
        Catch Ex As Exception
            Logger.Warn(Ex, $"加载悬浮窗播放封面失败（{Source}）")
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
            Dim Resource = Application.GetResourceStream(SourceUri)
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
        If Trimmed.StartsWith("/", StringComparison.Ordinal) AndAlso Not String.IsNullOrWhiteSpace(LastBaseUrl) Then
            Return New Uri(LastBaseUrl.TrimEnd("/"c) & Trimmed, UriKind.Absolute)
        End If

        If Not String.IsNullOrWhiteSpace(LastBaseUrl) Then Return New Uri(LastBaseUrl.TrimEnd("/"c) & "/" & Trimmed.TrimStart("/"c), UriKind.Absolute)
        Return Nothing
    End Function

    Private Sub ClearCoverImage()
        Threading.Interlocked.Increment(CoverLoadSerial)
        CurrentCoverSource = ""
        ImgCover.Source = Nothing
        ImgCover.Visibility = Visibility.Collapsed
        PathCoverPlaceholder.Visibility = Visibility.Visible
    End Sub

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

    Private Shared Function ConfigString(Config As Dictionary(Of String, JsonElement), Key As String, DefaultValue As String) As String
        If Config Is Nothing OrElse Not Config.ContainsKey(Key) Then Return DefaultValue
        Dim Value = Config(Key)
        Select Case Value.ValueKind
            Case JsonValueKind.String
                Return NonEmpty(Value.GetString(), DefaultValue)
            Case JsonValueKind.Number, JsonValueKind.True, JsonValueKind.False
                Return Value.ToString()
            Case Else
                Return DefaultValue
        End Select
    End Function

    Private Shared Function NonEmpty(ParamArray Values As String()) As String
        For Each Value In Values
            If Not String.IsNullOrWhiteSpace(Value) Then Return Value
        Next
        Return ""
    End Function

    Private Shared Function FormatDuration(Seconds As Double) As String
        If Seconds <= 0 Then Return "0:00"
        Dim TotalSeconds = CInt(Math.Floor(Seconds))
        Return $"{TotalSeconds \ 60}:{(TotalSeconds Mod 60).ToString().PadLeft(2, "0"c)}"
    End Function

    Private Sub SliderProgress_Change(sender As Object, user As Boolean) Handles SliderProgress.Change
        If Not ReferenceEquals(SliderProgress, DragControl) Then Return
        ActivePosition = SliderProgress.Value
        LabTime.Text = FormatDuration(ActivePosition) & If(ActiveDuration > 0, " / " & FormatDuration(ActiveDuration), "")
        PendingSeekPosition = ActivePosition
        SeekDebounceTimer.Stop()
        SeekDebounceTimer.Start()
    End Sub

    Private Async Sub SeekDebounceTimer_Tick(sender As Object, e As EventArgs)
        SeekDebounceTimer.Stop()
        If ReferenceEquals(SliderProgress, DragControl) Then
            SeekDebounceTimer.Start()
            Return
        End If
        If IsSendingSeek Then
            SeekDebounceTimer.Start()
            Return
        End If
        If PendingSeekPosition < 0 Then Return
        If String.IsNullOrWhiteSpace(LastBaseUrl) OrElse String.IsNullOrWhiteSpace(ActiveInstanceId) Then Return

        Dim TargetPosition = PendingSeekPosition
        PendingSeekPosition = -1
        IsSendingSeek = True
        Try
            Await OmniMixApiClient.SeekInstanceAsync(LastBaseUrl, ActiveInstanceId, TargetPosition)
        Catch Ex As Exception
            Logger.Warn(Ex, "悬浮窗跳转进度失败")
        Finally
            IsSendingSeek = False
        End Try
    End Sub

    Private Sub FloatingPlaybackWindow_MouseMove(sender As Object, e As MouseEventArgs) Handles Me.MouseMove
        If DragControl IsNot Nothing AndAlso ReferenceEquals(DragControl, SliderProgress) Then
            If Mouse.LeftButton = MouseButtonState.Pressed Then
                SliderProgress.DragDoing()
            Else
                DragStopFloating()
            End If
        End If
    End Sub

    Private Sub FloatingPlaybackWindow_MouseUp(sender As Object, e As MouseButtonEventArgs) Handles Me.MouseLeftButtonUp
        DragStopFloating()
    End Sub

    Private Sub DragStopFloating()
        If DragControl IsNot Nothing AndAlso ReferenceEquals(DragControl, SliderProgress) Then
            Dim Control = DragControl
            DragControl = Nothing
            Control.DragStop()
        End If
    End Sub
End Class
