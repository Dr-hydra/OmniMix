Imports System.Threading

Public Module OmniMixDesktopPlayerService

    Private Const DesktopPlayerModId As String = "omnimix.vbnet.desktop"
    Private ReadOnly ServiceLock As New Object()
    Private CurrentBaseUrl As String = ""
    Private CurrentInstanceId As String = ""
    Private LastActivatedInstanceId As String = ""
    Private CurrentRun As Task = Nothing
    Private CurrentCancel As CancellationTokenSource = Nothing
    Private CurrentSink As OmniMixDesktopPcmPlaybackSink = Nothing

    Public Sub EnsureConnected(BaseUrl As String)
        If String.IsNullOrWhiteSpace(BaseUrl) OrElse Not Settings.Get(Of Boolean)("OmniMixDesktopPlayerEnabled") Then
            Disconnect()
            Return
        End If

        SyncLock ServiceLock
            If CurrentCancel IsNot Nothing AndAlso
               Not CurrentCancel.IsCancellationRequested AndAlso
               String.Equals(CurrentBaseUrl, BaseUrl, StringComparison.OrdinalIgnoreCase) Then
                If Not String.IsNullOrWhiteSpace(CurrentInstanceId) Then ActivateDesktopInstance(BaseUrl, CurrentInstanceId)
                Return
            End If
        End SyncLock

        Disconnect()

        Dim CancelSource As New CancellationTokenSource()
        SyncLock ServiceLock
            CurrentBaseUrl = BaseUrl
            CurrentCancel = CancelSource
            CurrentRun = Task.Run(Function() RunAsync(BaseUrl, CancelSource.Token))
        End SyncLock
    End Sub

    Public Sub ReconcileWithInstances(BaseUrl As String, Instances As IEnumerable(Of OmniMixPlaybackInstanceInfo))
        If String.IsNullOrWhiteSpace(BaseUrl) Then
            Disconnect()
            Return
        End If

        Dim HasOnlineGameInstance = If(Instances, Enumerable.Empty(Of OmniMixPlaybackInstanceInfo)()).
            Any(Function(Instance) IsOnlineGameInstance(Instance))

        If HasOnlineGameInstance Then
            Disconnect()
        Else
            EnsureConnected(BaseUrl)
        End If
    End Sub

    Public Sub Disconnect()
        Dim CancelSource As CancellationTokenSource = Nothing
        Dim Sink As OmniMixDesktopPcmPlaybackSink = Nothing
        Dim BaseUrl As String = ""
        Dim InstanceId As String = ""

        SyncLock ServiceLock
            CancelSource = CurrentCancel
            CurrentCancel = Nothing
            BaseUrl = CurrentBaseUrl
            InstanceId = CurrentInstanceId
            CurrentBaseUrl = ""
            CurrentInstanceId = ""
            LastActivatedInstanceId = ""
            Sink = CurrentSink
            CurrentSink = Nothing
        End SyncLock

        Try
            CancelSource?.Cancel()
        Catch
        End Try
        Try
            Sink?.Dispose()
        Catch Ex As Exception
            Logger.Warn(Ex, "停止 OmniMix 桌面播放器音频输出失败")
        End Try
        If Not String.IsNullOrWhiteSpace(BaseUrl) AndAlso Not String.IsNullOrWhiteSpace(InstanceId) Then
            Task.Run(Async Function()
                         Try
                             Await OmniMixApiClient.DisconnectInstanceAsync(BaseUrl, InstanceId)
                         Catch Ex As Exception
                             Logger.Warn(Ex, "断开 OmniMix 桌面播放器实例失败")
                         End Try
                     End Function)
        End If
    End Sub

    Public Sub DisconnectAndWait()
        Dim Run As Task = Nothing
        Dim BaseUrl As String = ""
        Dim InstanceId As String = ""

        SyncLock ServiceLock
            Run = CurrentRun
            BaseUrl = CurrentBaseUrl
            InstanceId = CurrentInstanceId
        End SyncLock

        Disconnect()

        If Not String.IsNullOrWhiteSpace(BaseUrl) AndAlso Not String.IsNullOrWhiteSpace(InstanceId) Then
            Try
                OmniMixApiClient.DisconnectInstanceAsync(BaseUrl, InstanceId).Wait(800)
            Catch Ex As Exception
                Logger.Warn(Ex, "断开 OmniMix 桌面播放器实例失败")
            End Try
        End If

        Try
            If Run IsNot Nothing Then Run.Wait(800)
        Catch
        End Try
    End Sub

    Private Async Function RunAsync(BaseUrl As String, CancelToken As CancellationToken) As Task
        Dim InstanceIdToDisconnect As String = ""
        Try
            Dim InstanceId = Await OmniMixApiClient.ConnectDesktopPlayerInstanceAsync(BaseUrl)
            If String.IsNullOrWhiteSpace(InstanceId) Then Return

            SyncLock ServiceLock
                If CancelToken.IsCancellationRequested Then Return
                CurrentInstanceId = InstanceId
                CurrentSink = New OmniMixDesktopPcmPlaybackSink($"Global\OmniMixPlayer_PCM_{InstanceId}")
                CurrentSink.Start()
            End SyncLock
            ActivateDesktopInstance(BaseUrl, InstanceId)

            Logger.Info("OmniMix 桌面播放器实例已连接：" & InstanceId)

            While Not CancelToken.IsCancellationRequested
                Await Task.Delay(TimeSpan.FromSeconds(8), CancelToken)
                Dim Alive = Await OmniMixApiClient.HeartbeatInstanceAsync(BaseUrl, InstanceId)
                If Not Alive Then Exit While
            End While
        Catch Ex As TaskCanceledException
        Catch Ex As OperationCanceledException
        Catch Ex As Exception
            Logger.Warn(Ex, "OmniMix 桌面播放器实例运行失败")
        Finally
            Dim InstanceId As String = ""
            Dim Sink As OmniMixDesktopPcmPlaybackSink = Nothing
            Dim ShouldDisconnect = False
            SyncLock ServiceLock
                InstanceId = CurrentInstanceId
                ShouldDisconnect = Not String.IsNullOrWhiteSpace(InstanceId)
                CurrentInstanceId = ""
                LastActivatedInstanceId = ""
                Sink = CurrentSink
                CurrentSink = Nothing
            End SyncLock
            Try
                Sink?.Dispose()
            Catch Ex As Exception
                Logger.Warn(Ex, "停止 OmniMix 桌面播放器音频输出失败")
            End Try

            If ShouldDisconnect Then
                InstanceIdToDisconnect = InstanceId
            End If
        End Try

        If Not String.IsNullOrWhiteSpace(InstanceIdToDisconnect) Then
            Try
                Await OmniMixApiClient.DisconnectInstanceAsync(BaseUrl, InstanceIdToDisconnect)
            Catch Ex As Exception
                Logger.Warn(Ex, "断开 OmniMix 桌面播放器实例失败")
            End Try
        End If
    End Function

    Private Function IsOnlineGameInstance(Instance As OmniMixPlaybackInstanceInfo) As Boolean
        If Instance Is Nothing OrElse Not Instance.Attached Then Return False
        If IsDesktopPlayerInstance(Instance) Then Return False
        If String.Equals(Instance.Role, "GameMod", StringComparison.OrdinalIgnoreCase) Then Return True
        Return Not String.IsNullOrWhiteSpace(Instance.ModId) OrElse Not String.IsNullOrWhiteSpace(Instance.GameName)
    End Function

    Private Function IsDesktopPlayerInstance(Instance As OmniMixPlaybackInstanceInfo) As Boolean
        If Instance Is Nothing Then Return False
        Return String.Equals(Instance.ModId, DesktopPlayerModId, StringComparison.OrdinalIgnoreCase)
    End Function

    Private Sub ActivateDesktopInstance(BaseUrl As String, InstanceId As String)
        If String.IsNullOrWhiteSpace(BaseUrl) OrElse String.IsNullOrWhiteSpace(InstanceId) Then Return

        SyncLock ServiceLock
            If String.Equals(LastActivatedInstanceId, InstanceId, StringComparison.OrdinalIgnoreCase) Then Return
            LastActivatedInstanceId = InstanceId
        End SyncLock

        Task.Run(Async Function()
                     Try
                         Await OmniMixApiClient.SetActiveInstanceAsync(BaseUrl, InstanceId)
                     Catch Ex As Exception
                         Logger.Warn(Ex, "切换到 OmniMix 桌面播放器实例失败")
                     End Try
                 End Function)
    End Sub

End Module
