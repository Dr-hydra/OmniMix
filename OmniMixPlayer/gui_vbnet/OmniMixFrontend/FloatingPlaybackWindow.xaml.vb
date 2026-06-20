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
    Private CurrentCoverSource As String = ""
    Private CoverLoadSerial As Integer = 0

    Public Sub New()
        InitializeComponent()
        RefreshTimer = New DispatcherTimer With {.Interval = TimeSpan.FromSeconds(1)}
        AddHandler RefreshTimer.Tick, AddressOf RefreshTimer_Tick
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
        Try
            DragMove()
        Catch
        End Try
    End Sub

    Private Sub BtnClose_Click(sender As Object, e As RoutedEventArgs) Handles BtnClose.Click
        Hide()
    End Sub

    Public Sub ApplyAppearance()
        Try
            Dim ThemeColor = GetThemeColor()
            Dim OpacityPercent = Math.Max(25, Math.Min(100, Settings.Get(Of Integer)("OmniMixFloatingWindowOpacity")))
            Dim Alpha = CByte(Math.Max(0, Math.Min(255, CInt(OpacityPercent / 100.0 * 255))))
            Dim BackgroundColor = Color.FromArgb(Alpha, ThemeColor.R, ThemeColor.G, ThemeColor.B)
            PanRoot.Background = New SolidColorBrush(BackgroundColor)
            PanRoot.BorderBrush = New SolidColorBrush(Color.FromArgb(CByte(Math.Min(210, Alpha)), 255, 255, 255))

            Dim Accent = New SolidColorBrush(Color.FromArgb(230, 255, 255, 255))
            BarProgress.Foreground = Accent
            PanCover.Background = New SolidColorBrush(Color.FromArgb(CByte(Math.Max(30, Alpha \ 4)), 255, 255, 255))
        Catch Ex As Exception
            Logger.Warn(Ex, "刷新 OmniMix 悬浮窗外观失败")
        End Try
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
            RenderStatus("没有曲目正在播放", "等待游戏或桌面播放实例连接", "--", "--", 0)
            ClearCoverImage()
            Return
        End If

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
        Dim TimeText = FormatDuration(ActiveInstance.Position) & If(Duration > 0, " / " & FormatDuration(Duration), "")
        Dim Progress = If(Duration > 0, ActiveInstance.Position / Duration, 0)
        RenderStatus(Title, String.Join(" · ", MetaParts), State, TimeText, Progress)
        SetCoverImage(ResolveTrackCover(Track, BaseUrl))
    End Function

    Private Sub RenderStatus(Title As String, Meta As String, State As String, TimeText As String, Progress As Double)
        LabTitle.Text = If(String.IsNullOrWhiteSpace(Title), "OmniMix", Title)
        LabMeta.Text = If(String.IsNullOrWhiteSpace(Meta), " ", Meta)
        LabState.Text = If(String.IsNullOrWhiteSpace(State), "--", State)
        LabTime.Text = If(String.IsNullOrWhiteSpace(TimeText), "--", TimeText)
        BarProgress.Value = Math.Max(0, Math.Min(1, If(Double.IsNaN(Progress), 0, Progress)))
    End Sub

    Private Shared Function GetThemeColor() As Color
        Try
            Dim Brush = TryCast(Application.Current.TryFindResource("ColorBrush2"), SolidColorBrush)
            If Brush IsNot Nothing Then Return Brush.Color
        Catch
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
End Class
