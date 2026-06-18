Imports System.Windows.Threading
Imports System.Text.Json

Public Class FloatingPlaybackWindow

    Private ReadOnly RefreshTimer As DispatcherTimer
    Private IsRefreshing As Boolean
    Private LastBaseUrl As String = ""

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
    End Function

    Private Sub RenderStatus(Title As String, Meta As String, State As String, TimeText As String, Progress As Double)
        LabTitle.Text = If(String.IsNullOrWhiteSpace(Title), "OmniMix", Title)
        LabMeta.Text = If(String.IsNullOrWhiteSpace(Meta), " ", Meta)
        LabState.Text = If(String.IsNullOrWhiteSpace(State), "--", State)
        LabTime.Text = If(String.IsNullOrWhiteSpace(TimeText), "--", TimeText)
        BarProgress.Value = Math.Max(0, Math.Min(1, If(Double.IsNaN(Progress), 0, Progress)))
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
