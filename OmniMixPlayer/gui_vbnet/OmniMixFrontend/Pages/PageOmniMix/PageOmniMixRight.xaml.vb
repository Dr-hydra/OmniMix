Imports System.Globalization
Imports System.Text.Json
Imports System.Windows.Media
Imports System.Windows.Shapes
Imports Microsoft.Win32

Public Class PageOmniMixRight

    Private PageKey As String = "Home"
    Private InitialStatusText As String = ""
    Private HasCheckedBackend As Boolean = False
    Private CurrentBaseUrl As String = ""
    Private CurrentModuleId As String = ""
    Private CurrentUiKind As String = "default"
    Private CurrentLinkId As String = ""
    Private CurrentDetailKind As String = ""
    Private CurrentGameDetailId As String = ""
    Private ActiveInstanceId As String = ""
    Private CanControlActiveInstance As Boolean = False
    Private IsShowingHistory As Boolean = False
    Private IsUpdatingPlaybackUi As Boolean = False
    Private CurrentModulesPane As String = "launchpad"
    Private PreviousModulesPaneForDetail As String = "launchpad"
    Private CurrentLibraryPane As String = ""
    Private CurrentLibraryPlaylist As OmniMixPlaylistData = New OmniMixPlaylistData()
    Private CurrentLibraryTagSongs As Dictionary(Of String, List(Of OmniMixSongInfo)) = New Dictionary(Of String, List(Of OmniMixSongInfo))(StringComparer.OrdinalIgnoreCase)
    Private IsRefreshingLibraryCache As Boolean = False
    Private CurrentModules As List(Of OmniMixModuleInfo) = New List(Of OmniMixModuleInfo)
    Private ExpandedModuleKey As String = ""
    Private ExpandedModuleTree As OmniMixRawNodeData = Nothing
    Private ExpandedModuleError As String = ""
    Private ExpandedModuleLoading As Boolean = False
    Private ExpandedModuleTitle As String = ""
    Private ModuleUiRequestSerial As Integer = 0
    Private CurrentSettingsPane As String = "config"
    Private CurrentEqualizerInstanceId As String = ""
    Private CurrentEqualizerState As OmniMixEqualizerStateInfo = Nothing
    Private CurrentEqualizerPresets As Dictionary(Of String, OmniMixEqualizerStateInfo) = New Dictionary(Of String, OmniMixEqualizerStateInfo)
    Private EqualizerSelectedPointId As String = ""
    Private IsDraggingEqualizerPoint As Boolean = False
    Private IsEqualizerDragChanged As Boolean = False
    Private DeploymentLogs As List(Of String) = New List(Of String)
    Private ReadOnly WsClient As New OmniMixWsClient()
    Private ReadOnly ExpandedLibraryAlbums As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly PlaybackAutoRefreshTimer As New System.Windows.Threading.DispatcherTimer With {.Interval = TimeSpan.FromSeconds(1)}
    Private IsAutoRefreshingPlayback As Boolean = False
    Private CurrentQueueRenderKey As String = ""
    Private Const IconMoveUp As String = "M512 170.667 192 490.667h192v341.333h128V490.667h192L512 170.667z"
    Private Const IconMoveDown As String = "M512 853.333 832 533.333H640V192H512v341.333H320L512 853.333z"
    Private Const IconPlay As String = "M352 224v672l480-336-480-336z"
    Private Const IconPlus As String = "M448 128h128v320h320v128H576v320H448V576H128V448h320z"
    Private Const IconExpandDown As String = "M192 352h640L512 704z"
    Private Const IconExpandUp As String = "M192 672h640L512 320z"
    Private Const EqualizerPlotLeft As Double = 46
    Private Const EqualizerPlotRight As Double = 18
    Private Const EqualizerPlotTop As Double = 18
    Private Const EqualizerPlotBottom As Double = 28

    Private Class LibraryQueueGroupPayload
        Public Property GroupId As String = ""
        Public Property Name As String = ""
        Public Property Uuids As List(Of String) = New List(Of String)
    End Class

    Private Class LibraryAlbumGroup
        Public Property GroupId As String = ""
        Public Property Name As String = ""
        Public Property ModuleId As String = ""
        Public Property CoverPath As String = ""
        Public Property Songs As List(Of OmniMixSongInfo) = New List(Of OmniMixSongInfo)
    End Class

    Public Shared Function Create(PageKey As String) As PageOmniMixRight
        Dim Page As New PageOmniMixRight
        Page.Configure(PageKey)
        Return Page
    End Function

    Public Sub Configure(PageKey As String)
        Me.PageKey = PageKey
        GridHome.Visibility = If(PageKey = "Home", Visibility.Visible, Visibility.Collapsed)
        CardLibrary.Visibility = If(PageKey = "Library", Visibility.Visible, Visibility.Collapsed)
        CardModules.Visibility = If(PageKey = "Modules", Visibility.Visible, Visibility.Collapsed)
        CardSettings.Visibility = If(PageKey = "Settings", Visibility.Visible, Visibility.Collapsed)
        CardAbout.Visibility = If(PageKey = "About", Visibility.Visible, Visibility.Collapsed)
        CardModuleUi.Visibility = Visibility.Collapsed
        Select Case PageKey
            Case "Home"
                LabTitle.Text = "播放"
                LabSubtitle.Text = "左侧控制当前播放，右侧管理播放队列。"
                LabStatus.Text = "正在等待后端状态。"
            Case "Library"
                LabTitle.Text = "音乐库"
                LabSubtitle.Text = "按来源浏览后端聚合出的音乐库。"
                LabStatus.Text = "接口：/api/playlist。"
            Case "Modules"
                LabTitle.Text = "插件"
                LabSubtitle.Text = "在 Mod 与游戏集成之间切换，并管理音乐源模块。"
                LabStatus.Text = "接口：/api/modules、模块设置 UI、快捷入口、播放实例和 WebSocket UI 事件。"
            Case "Settings"
                LabTitle.Text = "设置"
                LabSubtitle.Text = "后端配置、播放实例档案和 GUI 本地偏好。"
                LabStatus.Text = "下一步按 Flutter 设置页继续移植具体配置项。"
            Case "About"
                LabTitle.Text = "关于"
                LabSubtitle.Text = "了解 OmniMix Player 的桌面端功能、模块能力和运行方式。"
                LabStatus.Text = "OmniMix Player 用于连接本机后端、聚合音乐来源，并把播放状态与音频流提供给游戏集成实例。"
            Case Else
                LabTitle.Text = "OmniMix"
                LabSubtitle.Text = "未知页面。"
                LabStatus.Text = "页面键：" & PageKey
        End Select
        InitialStatusText = LabStatus.Text
    End Sub

    Public Async Sub SetLibraryPane(Pane As String)
        CurrentLibraryPane = If(Pane, "")
        If PageKey <> "Library" Then Return

        CardLibrary.Title = "音乐库"
        LabPlaylistSourcesSummary.Visibility = Visibility.Collapsed
        PanPlaylistSources.Visibility = Visibility.Collapsed
        PanLibraryList.Visibility = Visibility.Visible
        LabLibrarySummary.Visibility = Visibility.Visible

        If Not String.IsNullOrWhiteSpace(CurrentBaseUrl) AndAlso Not IsRefreshingLibraryCache Then
            Await RefreshLibraryCacheAsync(CurrentBaseUrl)
        Else
            RenderLibrary()
        End If
    End Sub

    Public Sub SetModulesPane(ShowGameIntegration As Boolean)
        SetModulesPane(If(ShowGameIntegration, "game", "mod"))
    End Sub

    Public Async Sub SetModulesPane(Pane As String)
        If PageKey <> "Modules" Then Return
        If CardModuleUi.Visibility = Visibility.Visible Then CollapseModuleUi(False)
        Dim NormalizedPane = If(Pane, "launchpad").Trim().ToLowerInvariant()
        If NormalizedPane <> "game" AndAlso NormalizedPane <> "mod" Then NormalizedPane = "launchpad"
        If String.Equals(CurrentModulesPane, NormalizedPane, StringComparison.OrdinalIgnoreCase) Then
            UpdatePluginPaneVisibility()
            If CurrentModules.Count > 0 Then
                If CurrentModulesPane = "launchpad" Then RenderLaunchpad(CurrentModules)
                If CurrentModulesPane = "mod" Then RenderModules(CurrentModules)
            End If
            Return
        End If

        CurrentModulesPane = NormalizedPane
        If String.IsNullOrWhiteSpace(CurrentBaseUrl) Then
            Await RefreshBackendStatusAsync()
        Else
            Await RefreshModulesAsync(CurrentBaseUrl)
        End If
    End Sub

    Public Sub PrepareForOmniMixPageSwitch(TargetPage As FormMain.PageType)
        If PageKey <> "Modules" OrElse TargetPage = FormMain.PageType.Link Then Return

        ModuleUiRequestSerial += 1
        If CardModuleUi.Visibility = Visibility.Visible Then CollapseModuleUi(False)
        CardModuleUi.Visibility = Visibility.Collapsed
        CardModules.Visibility = Visibility.Collapsed
        PanModuleUi.Children.Clear()
        CurrentModuleId = ""
        CurrentUiKind = "default"
        CurrentLinkId = ""
        CurrentDetailKind = ""
        CurrentGameDetailId = ""
        ExpandedModuleKey = ""
        ExpandedModuleTree = Nothing
        ExpandedModuleError = ""
        ExpandedModuleLoading = False
    End Sub

    Public Async Sub SetSettingsPane(Pane As String)
        CurrentSettingsPane = If(String.IsNullOrWhiteSpace(Pane), "config", Pane)
        If PageKey <> "Settings" Then Return

        Dim ShowConfig = String.Equals(CurrentSettingsPane, "config", StringComparison.OrdinalIgnoreCase)
        Dim ShowPersonalization = String.Equals(CurrentSettingsPane, "personalization", StringComparison.OrdinalIgnoreCase)
        Dim ShowMaintenance = String.Equals(CurrentSettingsPane, "maintenance", StringComparison.OrdinalIgnoreCase)
        Dim ShowInstances = String.Equals(CurrentSettingsPane, "instances", StringComparison.OrdinalIgnoreCase)
        Dim ShowArchives = String.Equals(CurrentSettingsPane, "archives", StringComparison.OrdinalIgnoreCase)
        Dim ShowEqualizer = String.Equals(CurrentSettingsPane, "equalizer", StringComparison.OrdinalIgnoreCase)

        If ShowInstances Then
            CardSettings.Title = "设置 - 播放实例"
        ElseIf ShowArchives Then
            CardSettings.Title = "设置 - 归档"
        ElseIf ShowEqualizer Then
            CardSettings.Title = "设置 - 均衡器"
        ElseIf ShowPersonalization Then
            CardSettings.Title = "设置 - 个性化"
        ElseIf ShowMaintenance Then
            CardSettings.Title = "设置 - 初始化与缓存"
        Else
            CardSettings.Title = "设置 - 后端配置"
        End If
        LabSettingsSummary.Visibility = If(ShowConfig, Visibility.Visible, Visibility.Collapsed)
        PanSettingsConfig.Visibility = If(ShowConfig, Visibility.Visible, Visibility.Collapsed)
        PanSettingsChecks.Visibility = If(ShowConfig, Visibility.Visible, Visibility.Collapsed)
        PanSettingsConfigButtons.Visibility = If(ShowConfig, Visibility.Visible, Visibility.Collapsed)
        PanSettingsService.Visibility = Visibility.Collapsed
        PanSettingsPersonalization.Visibility = If(ShowPersonalization, Visibility.Visible, Visibility.Collapsed)
        PanSettingsMaintenance.Visibility = If(ShowMaintenance, Visibility.Visible, Visibility.Collapsed)
        PanSettingsEqualizer.Visibility = If(ShowEqualizer, Visibility.Visible, Visibility.Collapsed)
        LabInstanceStats.Visibility = If(ShowInstances, Visibility.Visible, Visibility.Collapsed)
        PanSettingsInstances.Visibility = If(ShowInstances, Visibility.Visible, Visibility.Collapsed)
        LabArchiveSummary.Visibility = If(ShowArchives, Visibility.Visible, Visibility.Collapsed)
        BtnArchivesRefresh.Visibility = If(ShowArchives, Visibility.Visible, Visibility.Collapsed)
        PanSettingsArchives.Visibility = If(ShowArchives, Visibility.Visible, Visibility.Collapsed)

        If ShowPersonalization Then
            SettingService.RefreshSettings(PanSettingsPersonalization)
            RefreshPersonalizationUi()
        ElseIf ShowMaintenance Then
            RefreshCachePathText()
            Await RefreshCacheSizeAsync()
        ElseIf ShowEqualizer Then
            If String.IsNullOrWhiteSpace(CurrentBaseUrl) Then
                LabEqualizerSummary.Text = "等待连接后端后读取均衡器。"
                PanEqualizerPresets.Children.Clear()
                PanEqualizerPoints.Children.Clear()
            Else
                Await RefreshEqualizerAsync(CurrentBaseUrl)
            End If
        ElseIf ShowConfig Then
            Await RefreshServiceStatusAsync()
        End If
    End Sub

    Private Async Sub PageOmniMixRight_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        BtnBackendPathBrowse.Logo = Logo.IconButtonOpen
        BtnBackendPathReset.Logo = Logo.IconButtonRefresh
        CheckCloseBackendWithGui.Checked = Settings.Get(Of Boolean)("OmniMixCloseBackendWithGui")
        RemoveHandler CheckCloseBackendWithGui.Change, AddressOf CheckCloseBackendWithGui_Change
        AddHandler CheckCloseBackendWithGui.Change, AddressOf CheckCloseBackendWithGui_Change
        RemoveHandler PlaybackAutoRefreshTimer.Tick, AddressOf PlaybackAutoRefreshTimer_Tick
        AddHandler PlaybackAutoRefreshTimer.Tick, AddressOf PlaybackAutoRefreshTimer_Tick
        RemoveHandler WsClient.MessageReceived, AddressOf WsClient_MessageReceived
        AddHandler WsClient.MessageReceived, AddressOf WsClient_MessageReceived
        If HasCheckedBackend Then
            UpdatePlaybackAutoRefreshTimer()
            Return
        End If
        HasCheckedBackend = True
        Await RefreshBackendStatusAsync()
    End Sub

    Private Sub CheckCloseBackendWithGui_Change(sender As Object, user As Boolean)
        If user Then Settings.Set("OmniMixCloseBackendWithGui", CheckCloseBackendWithGui.Checked)
    End Sub

    Private Sub PageOmniMixRight_Unloaded(sender As Object, e As RoutedEventArgs) Handles Me.Unloaded
        PlaybackAutoRefreshTimer.Stop()
        RemoveHandler WsClient.MessageReceived, AddressOf WsClient_MessageReceived
    End Sub

    Private Sub WsClient_MessageReceived(Message As String)
        If String.IsNullOrWhiteSpace(Message) Then Return

        Try
            Using Doc = JsonDocument.Parse(Message)
                Dim Root = Doc.RootElement
                Dim TypeElement As JsonElement
                If Not Root.TryGetProperty("type", TypeElement) OrElse TypeElement.GetString() <> "ui_push" Then Return

                Dim DataElement As JsonElement
                If Not Root.TryGetProperty("data", DataElement) Then Return

                Dim ModuleIdElement As JsonElement
                If Not DataElement.TryGetProperty("moduleId", ModuleIdElement) Then Return
                Dim ModuleId = ModuleIdElement.GetString()
                If String.IsNullOrWhiteSpace(ModuleId) Then Return

                Dim TreeElement As JsonElement
                If Not DataElement.TryGetProperty("tree", TreeElement) OrElse TreeElement.ValueKind = JsonValueKind.Null Then Return

                Dim Options As New JsonSerializerOptions With {.PropertyNameCaseInsensitive = True}
                Dim Tree = JsonSerializer.Deserialize(Of OmniMixRawNodeData)(TreeElement.GetRawText(), Options)
                If Tree Is Nothing Then Return

                RunInUi(Sub()
                            If CardModuleUi.Visibility <> Visibility.Visible Then Return
                            If Not String.Equals(CurrentModuleId, ModuleId, StringComparison.OrdinalIgnoreCase) Then Return
                            PanModuleUi.Children.Clear()
                            PanModuleUi.Children.Add(OmniMixRawNodeRenderer.Render(Tree, CurrentBaseUrl, AddressOf ModuleUiEvent_Dispatch))
                            LabModulesSummary.Text = "模块 UI 已更新：" & ExpandedModuleTitle
                            LabModuleUiSummary.Text = "模块 UI 已更新。"
                        End Sub)
            End Using
        Catch
        End Try
    End Sub

    Private Async Function RefreshBackendStatusAsync() As Task
        LoadState.Visibility = Visibility.Visible
        LoadState.State.LoadingState = MyLoading.MyLoadingState.Run
        LabStatus.Text = InitialStatusText & vbCrLf & vbCrLf & "正在发现或启动 OmniMix 后端..."
        FrmMain?.SetOmniMixConnectionStatus(False)

        Dim Status = Await OmniMixBackendManager.EnsureStartedAsync()
        If Status.IsOnline Then
            LoadState.State.LoadingState = MyLoading.MyLoadingState.Stop
            LoadState.Visibility = Visibility.Collapsed
            LabStatus.Text = InitialStatusText & vbCrLf & vbCrLf &
                "后端状态：在线" & vbCrLf &
                "连接地址：" & Status.BaseUrl & vbCrLf &
                "健康检查：/api/health 已通过" & vbCrLf &
                If(Status.StartedBackend, "启动方式：GUI 已自动启动后端", "启动方式：发现已有后端")
            CurrentBaseUrl = Status.BaseUrl
            FrmMain?.SetOmniMixConnectionStatus(True, Status.BaseUrl)
            Await SyncGameIntegrationBindingsAsync(Status.BaseUrl)
            If PageKey = "Home" Then Await RefreshPlaybackAsync(Status.BaseUrl)
            If PageKey = "Library" Then Await RefreshLibraryAsync(Status.BaseUrl)
            If PageKey = "Modules" Then Await RefreshModulesAsync(Status.BaseUrl)
            If PageKey = "Settings" Then Await RefreshSettingsAsync(Status.BaseUrl)
            UpdatePlaybackAutoRefreshTimer()
        Else
            LoadState.State.LoadingState = MyLoading.MyLoadingState.Error
            LabStatus.Text = InitialStatusText & vbCrLf & vbCrLf &
                "后端状态：离线" & vbCrLf &
                Status.Message & vbCrLf &
                "GUI 会继续运行，后续可以在这里重新尝试连接后端。"
            FrmMain?.SetOmniMixConnectionStatus(False)
            If PageKey = "Library" Then
                PanLibraryList.Children.Clear()
                PanPlaylistSources.Children.Clear()
                LabLibrarySummary.Text = "后端未连接，暂时无法读取曲库。"
                LabPlaylistSourcesSummary.Text = ""
                LabPlaylistSourcesSummary.Visibility = Visibility.Collapsed
                PanPlaylistSources.Visibility = Visibility.Collapsed
            End If
            If PageKey = "Modules" Then
                PanModulesList.Children.Clear()
                PanGameIntegrationList.Children.Clear()
                LabModulesSummary.Text = "后端未连接，暂时无法读取模块列表。"
            End If
            If PageKey = "Home" Then
                ActiveInstanceId = ""
                CanControlActiveInstance = False
                RenderPlayback(New List(Of OmniMixPlaybackInstanceInfo), Nothing)
                LabPlaybackSummary.Text = "后端未连接，暂时无法读取播放实例。"
                RenderQueueItems(New List(Of OmniMixQueueItemInfo), IsShowingHistory)
                LabQueueSummary.Text = ""
                LabQueueSummary.Visibility = Visibility.Collapsed
            End If
            If PageKey = "Settings" Then
                LabSettingsSummary.Text = "后端未连接，暂时无法读取或保存配置。"
                Await RefreshServiceStatusAsync()
                LabInstanceStats.Text = "实例统计：--"
                PanSettingsInstances.Children.Clear()
                LabArchiveSummary.Text = "归档：后端未连接。"
                PanSettingsArchives.Children.Clear()
                LabEqualizerSummary.Text = "均衡器：后端未连接。"
                PanEqualizerPresets.Children.Clear()
                PanEqualizerPoints.Children.Clear()
                CurrentEqualizerInstanceId = ""
                CurrentEqualizerState = Nothing
                CurrentEqualizerPresets.Clear()
            End If
            UpdatePlaybackAutoRefreshTimer()
        End If
    End Function

    Private Async Function RefreshSettingsAsync(BaseUrl As String) As Task
        If PageKey <> "Settings" Then Return

        LabSettingsSummary.Text = "正在读取后端配置..."
        PanSettingsInstances.Children.Clear()

        Try
            Dim Config = Await OmniMixApiClient.GetConfigAsync(BaseUrl)
            TxtBackendPort.Text = ConfigString(Config, "backend_port", "17890")
            TxtBackendBind.Text = ConfigString(Config, "backend_bind", "127.0.0.1")
            Dim ConfiguredBackendPath = OmniMixBackendManager.GetConfiguredBackendPath()
            Dim DefaultBackendPath = OmniMixBackendManager.FindDefaultBackendExe()
            TxtBackendPath.HintText = If(String.IsNullOrWhiteSpace(DefaultBackendPath), "OmniMixPlayer.Backend.exe", "默认：" & DefaultBackendPath)
            If Not String.IsNullOrWhiteSpace(ConfiguredBackendPath) AndAlso
               Not OmniMixPlatformService.ArePathsEqual(ConfiguredBackendPath, DefaultBackendPath) Then
                TxtBackendPath.Text = ConfiguredBackendPath
            Else
                TxtBackendPath.Text = ""
                If Not String.IsNullOrWhiteSpace(ConfiguredBackendPath) Then OmniMixBackendManager.SetConfiguredBackendPath("")
            End If
            BtnBackendPathBrowse.Logo = Logo.IconButtonOpen
            BtnBackendPathReset.Logo = Logo.IconButtonRefresh
            CheckAutostart.Checked = ConfigBoolean(Config, "autostart", False)
            CheckMinimizeToTray.Checked = ConfigBoolean(Config, "minimize_to_tray", True)
            Await RefreshServiceStatusAsync()

            Dim Stats = Await OmniMixApiClient.GetInstanceStatsAsync(BaseUrl)
            Dim Instances = Await OmniMixApiClient.GetInstancesAsync(BaseUrl)
            Dim Archives = Await OmniMixApiClient.GetArchivesAsync(BaseUrl)
            RenderSettingsStats(Stats, Instances, ConfigString(Config, "active_instance", ""))
            RenderSettingsArchives(Archives, Instances, ConfigString(Config, "active_instance", ""))
            LabSettingsSummary.Text = "已读取后端配置。保存后会写入全局配置文件；端口和绑定地址通常需要重启后端后生效。"
            SetSettingsPane(CurrentSettingsPane)
        Catch Ex As Exception
            LabSettingsSummary.Text = "设置读取失败：" & Ex.Message
            LabInstanceStats.Text = "实例统计：读取失败"
            LabArchiveSummary.Text = "归档：读取失败"
        End Try
    End Function

    Private Sub RenderSettingsStats(Stats As OmniMixInstanceStatsInfo, Instances As List(Of OmniMixPlaybackInstanceInfo), ActiveInstanceConfigId As String)
        Stats = If(Stats, New OmniMixInstanceStatsInfo)
        Instances = If(Instances, New List(Of OmniMixPlaybackInstanceInfo))
        FrmMain?.UpdateOmniMixInstanceMenu(Instances, ActiveInstanceConfigId)
        LabInstanceStats.Text =
            $"实例统计：{Stats.InstanceCount} 个实例，{Stats.AttachedAudioClients} 个在线音频端，{Stats.ControllerClients} 个控制端，队列合计 {Stats.TotalQueueItems} 首。"

        PanSettingsInstances.Children.Clear()
        If Instances.Count = 0 Then
            PanSettingsInstances.Children.Add(New MyListItem With {
                .Title = "暂无播放实例",
                .Info = "后端在线，但还没有音频实例连接。",
                .Height = 50,
                .PaddingLeft = 8,
                .Margin = New Thickness(0, 0, 0, 2),
                .IsScaleAnimationEnabled = False,
                .Type = MyListItem.CheckType.Clickable
            })
            Return
        End If

        For Each Instance In Instances.OrderBy(Function(Item) Item.Id)
            Dim IsActive = String.Equals(Instance.Id, ActiveInstanceConfigId, StringComparison.OrdinalIgnoreCase)
            Dim InfoParts As New List(Of String)
            If IsActive Then InfoParts.Add("当前")
            InfoParts.Add(If(Instance.Attached, "在线", "离线"))
            InfoParts.Add(If(Instance.IsServerManaged, "后端控制", "客户端控制"))
            If Not String.IsNullOrWhiteSpace(Instance.GameName) Then InfoParts.Add(Instance.GameName)
            If Not String.IsNullOrWhiteSpace(Instance.ModId) Then InfoParts.Add("Mod " & Instance.ModId)
            If Instance.Attached AndAlso Not Instance.SharedMemoryReady Then InfoParts.Add("共享内存未就绪")
            InfoParts.Add("音量 " & CInt(Math.Round(Instance.Volume * 100)) & "%")
            InfoParts.Add("队列 " & Instance.QueueCount)

            Dim Item As New MyListItem With {
                .Title = If(IsActive, "✓ ", "") & NonEmpty(Instance.GameName, NonEmpty(Instance.Id, Instance.ClientId)),
                .Info = String.Join(" · ", InfoParts),
                .Height = 42,
                .PaddingLeft = 8,
                .Margin = New Thickness(0, 0, 0, 2),
                .IsScaleAnimationEnabled = False,
                .Type = MyListItem.CheckType.Clickable,
                .Tag = Instance
            }

            Dim Buttons As New List(Of MyIconButton)
            Dim SelectButton As New MyIconButton With {
                .Logo = Logo.IconButtonOpen,
                .LogoScale = 1.05,
                .ToolTip = If(IsActive, "当前实例", "设为当前实例"),
                .Tag = Instance,
                .IsEnabled = Not IsActive
            }
            AddHandler SelectButton.Click, AddressOf SettingsInstanceSelectButton_Click
            Buttons.Add(SelectButton)

            Dim EditButton As New MyIconButton With {
                .Logo = Logo.IconButtonEdit,
                .LogoScale = 0.95,
                .ToolTip = "编辑实例元数据",
                .Tag = Instance
            }
            AddHandler EditButton.Click, AddressOf SettingsInstanceMetaButton_Click
            Buttons.Add(EditButton)

            Dim ArchiveButton As New MyIconButton With {
                .Logo = Logo.IconButtonSave,
                .LogoScale = 0.95,
                .ToolTip = "保存实例归档",
                .Tag = Instance
            }
            AddHandler ArchiveButton.Click, AddressOf SettingsInstanceArchiveButton_Click
            Buttons.Add(ArchiveButton)

            If Not Instance.Attached Then
                Dim DeleteButton As New MyIconButton With {
                    .Logo = Logo.IconButtonDelete,
                    .LogoScale = 0.95,
                    .ToolTip = "删除离线实例",
                    .Tag = Instance
                }
                AddHandler DeleteButton.Click, AddressOf SettingsInstanceDeleteButton_Click
                Buttons.Add(DeleteButton)
            End If

            Item.Buttons = Buttons
            PanSettingsInstances.Children.Add(Item)
        Next
    End Sub

    Private Sub RenderSettingsArchives(Archives As List(Of OmniMixArchiveInfo), Instances As List(Of OmniMixPlaybackInstanceInfo), ActiveInstanceConfigId As String)
        Archives = If(Archives, New List(Of OmniMixArchiveInfo))
        Instances = If(Instances, New List(Of OmniMixPlaybackInstanceInfo))

        PanSettingsArchives.Children.Clear()
        LabArchiveSummary.Text = $"归档：{Archives.Count} 个已保存实例归档。"

        If Archives.Count = 0 Then
            PanSettingsArchives.Children.Add(New MyListItem With {
                .Title = "暂无归档",
                .Info = "可在上方实例列表中把当前播放实例保存为归档。",
                .Height = 42,
                .PaddingLeft = 8,
                .Margin = New Thickness(0, 0, 0, 2),
                .IsScaleAnimationEnabled = False,
                .Type = MyListItem.CheckType.Clickable
            })
            Return
        End If

        For Each Archive In Archives.OrderByDescending(Function(Item) ArchiveTicks(Item.ArchivedAt)).ThenBy(Function(Item) Item.InstanceId)
            Dim IsOnline = Instances.Any(Function(Instance) String.Equals(Instance.Id, Archive.InstanceId, StringComparison.OrdinalIgnoreCase) AndAlso Instance.Attached)
            Dim InfoParts As New List(Of String)
            If Not String.IsNullOrWhiteSpace(Archive.Mode) Then InfoParts.Add(Archive.Mode)
            If Not String.IsNullOrWhiteSpace(Archive.ModId) Then InfoParts.Add(Archive.ModId)
            InfoParts.Add("实例 " & NonEmpty(Archive.InstanceId, "--"))
            If IsOnline Then InfoParts.Add("在线，暂不删除")
            If Not String.IsNullOrWhiteSpace(Archive.ArchivedAt) Then InfoParts.Add(FormatArchiveTime(Archive.ArchivedAt))

            Dim Item As New MyListItem With {
                .Title = NonEmpty(Archive.DisplayName, Archive.InstanceId),
                .Info = String.Join(" · ", InfoParts),
                .Height = 42,
                .PaddingLeft = 8,
                .Margin = New Thickness(0, 0, 0, 2),
                .IsScaleAnimationEnabled = False,
                .Type = MyListItem.CheckType.Clickable,
                .Tag = Archive
            }

            Dim Buttons As New List(Of MyIconButton)
            Dim InheritButton As New MyIconButton With {
                .Logo = Logo.IconButtonOpen,
                .LogoScale = 1.05,
                .ToolTip = "应用到实例",
                .Tag = Tuple.Create(Archive, ActiveInstanceConfigId)
            }
            AddHandler InheritButton.Click, AddressOf ArchiveInheritButton_Click
            Buttons.Add(InheritButton)

            Dim RenameButton As New MyIconButton With {
                .Logo = Logo.IconButtonEdit,
                .LogoScale = 0.95,
                .ToolTip = "重命名归档",
                .Tag = Archive
            }
            AddHandler RenameButton.Click, AddressOf ArchiveRenameButton_Click
            Buttons.Add(RenameButton)

            If Not IsOnline Then
                Dim DeleteButton As New MyIconButton With {
                    .Logo = Logo.IconButtonDelete,
                    .LogoScale = 0.95,
                    .ToolTip = "删除归档",
                    .Tag = Archive
                }
                AddHandler DeleteButton.Click, AddressOf ArchiveDeleteButton_Click
                Buttons.Add(DeleteButton)
            End If

            Item.Buttons = Buttons
            PanSettingsArchives.Children.Add(Item)
        Next
    End Sub

    Private Async Sub BtnSettingsRefresh_Click(sender As Object, e As EventArgs) Handles BtnSettingsRefresh.Click
        If String.IsNullOrWhiteSpace(CurrentBaseUrl) Then
            Await RefreshBackendStatusAsync()
        Else
            Await RefreshSettingsAsync(CurrentBaseUrl)
        End If
    End Sub

    Private Async Sub BtnSettingsSave_Click(sender As Object, e As EventArgs) Handles BtnSettingsSave.Click
        If String.IsNullOrWhiteSpace(CurrentBaseUrl) Then Return

        Dim Port = TxtBackendPort.Text.Trim()
        Dim ParsedPort As Integer
        If Not Integer.TryParse(Port, ParsedPort) OrElse ParsedPort <= 0 OrElse ParsedPort >= 65536 Then
            LabSettingsSummary.Text = "端口无效，请输入 1 到 65535 之间的数字。"
            Return
        End If

        Dim Bind = TxtBackendBind.Text.Trim()
        Dim BackendPath = TxtBackendPath.Text.Trim()
        If Not String.IsNullOrWhiteSpace(BackendPath) Then
            If Not File.Exists(BackendPath) Then
                LabSettingsSummary.Text = "后端程序不存在：" & BackendPath
                Return
            End If
            If Not String.Equals(System.IO.Path.GetFileName(BackendPath), "OmniMixPlayer.Backend.exe", StringComparison.OrdinalIgnoreCase) Then
                LabSettingsSummary.Text = "请选择 OmniMixPlayer.Backend.exe。"
                Return
            End If
        End If
        If String.IsNullOrWhiteSpace(Bind) Then
            LabSettingsSummary.Text = "绑定地址不能为空。"
            Return
        End If

        Try
            OmniMixBackendManager.SetConfiguredBackendPath(BackendPath)
            Dim EffectiveBackendPath = OmniMixBackendManager.FindBackendExe()
            Dim ServiceUpdated = Await OmniMixPlatformService.UpdateServiceBinaryPathAsync(EffectiveBackendPath)
            Dim Updates As New Dictionary(Of String, Object) From {
                {"backend_port", Port},
                {"backend_bind", Bind},
                {"autostart", CheckAutostart.Checked},
                {"minimize_to_tray", CheckMinimizeToTray.Checked}
            }
            Await OmniMixApiClient.PutConfigRawAsync(CurrentBaseUrl, Updates)
            Await OmniMixApiClient.SaveConfigAsync(CurrentBaseUrl)
            LabSettingsSummary.Text = "配置已保存。端口和绑定地址变更会在后端重启后生效。"
            If ServiceUpdated Then LabSettingsSummary.Text &= vbCrLf & "后端服务路径已同步。"
        Catch Ex As Exception
            LabSettingsSummary.Text = "配置保存失败：" & Ex.Message
        End Try
    End Sub

    Private Sub BtnBackendPathBrowse_Click(sender As Object, e As EventArgs) Handles BtnBackendPathBrowse.Click
        Using Dialog As New System.Windows.Forms.OpenFileDialog()
            Dialog.Title = "选择 OmniMixPlayer.Backend.exe"
            Dialog.Filter = "OmniMixPlayer.Backend.exe|OmniMixPlayer.Backend.exe|可执行文件|*.exe|所有文件|*.*"
            Dialog.CheckFileExists = True
            Dim CurrentPath = TxtBackendPath.Text.Trim()
            If File.Exists(CurrentPath) Then
                Dialog.FileName = CurrentPath
                Dialog.InitialDirectory = System.IO.Path.GetDirectoryName(CurrentPath)
            Else
                Dim DefaultPath = OmniMixBackendManager.FindDefaultBackendExe()
                If File.Exists(DefaultPath) Then Dialog.InitialDirectory = System.IO.Path.GetDirectoryName(DefaultPath)
            End If
            If Dialog.ShowDialog() <> System.Windows.Forms.DialogResult.OK Then Return
            TxtBackendPath.Text = Dialog.FileName
        End Using
    End Sub

    Private Async Sub BtnClearFrontendCache_Click(sender As Object, e As EventArgs) Handles BtnClearFrontendCache.Click
        If MyMsgBox("确定要清理前端缓存吗？曲库数据库、模块数据和设置不会被删除。", "清理缓存", "清理", "取消") <> 1 Then Return

        BtnClearFrontendCache.IsEnabled = False
        Try
            Await Task.Run(AddressOf ClearFrontendCache)
            Hint("前端缓存已清理。", HintType.Green)
            Await RefreshCacheSizeAsync()
        Catch Ex As Exception
            Logger.Warn(Ex, "清理前端缓存失败")
            Hint("缓存清理失败：" & Ex.Message, HintType.Red)
        Finally
            BtnClearFrontendCache.IsEnabled = True
        End Try
    End Sub

    Private Async Sub BtnRefreshCacheSize_Click(sender As Object, e As EventArgs) Handles BtnRefreshCacheSize.Click
        Await RefreshCacheSizeAsync()
    End Sub

    Private Sub BtnBrowseCachePath_Click(sender As Object, e As EventArgs) Handles BtnBrowseCachePath.Click
        Using Dialog As New System.Windows.Forms.FolderBrowserDialog()
            Dialog.Description = "选择 OmniMix 前端缓存文件夹"
            Dialog.ShowNewFolderButton = True
            Dim CurrentPath = TxtCachePath.Text.Trim()
            If Directory.Exists(CurrentPath) Then Dialog.SelectedPath = CurrentPath
            If Dialog.ShowDialog() = System.Windows.Forms.DialogResult.OK Then TxtCachePath.Text = Dialog.SelectedPath
        End Using
    End Sub

    Private Async Sub BtnApplyCachePath_Click(sender As Object, e As EventArgs) Handles BtnApplyCachePath.Click
        Dim SelectedPath = TxtCachePath.Text.Trim()
        If String.IsNullOrWhiteSpace(SelectedPath) Then
            Hint("请选择缓存文件夹，或点击恢复默认。", HintType.Blue)
            Return
        End If
        Try
            SelectedPath = System.IO.Path.GetFullPath(SelectedPath).TrimEnd("\"c, "/"c) & "\"
            Directory.CreateDirectory(SelectedPath)
            CheckPermissionWithException(SelectedPath)
            Settings.Set("SystemSystemCache", SelectedPath)
            PathTemp = SelectedPath
            Directory.CreateDirectory(System.IO.Path.Combine(PathTemp, "Cache", "Http"))
            RefreshCachePathText()
            Await RefreshCacheSizeAsync()
            Hint("缓存文件夹已更新。", HintType.Green)
        Catch Ex As Exception
            Logger.Warn(Ex, "更新缓存文件夹失败")
            Hint("缓存文件夹不可用：" & Ex.Message, HintType.Red)
        End Try
    End Sub

    Private Async Sub BtnResetCachePath_Click(sender As Object, e As EventArgs) Handles BtnResetCachePath.Click
        Settings.Reset("SystemSystemCache")
        PathTemp = System.IO.Path.GetTempPath().TrimEnd("\"c, "/"c) & "\OmniMixPlayer\"
        Directory.CreateDirectory(System.IO.Path.Combine(PathTemp, "Cache", "Http"))
        RefreshCachePathText()
        Await RefreshCacheSizeAsync()
        Hint("已恢复系统默认缓存文件夹。", HintType.Green)
    End Sub

    Private Sub RefreshCachePathText()
        Dim ConfiguredPath = Settings.Get(Of String)("SystemSystemCache")
        TxtCachePath.Text = If(String.IsNullOrWhiteSpace(ConfiguredPath), PathTemp, ConfiguredPath)
        TxtCachePath.HintText = System.IO.Path.GetTempPath().TrimEnd("\"c, "/"c) & "\OmniMixPlayer\"
    End Sub

    Private Sub PersonalizationSlider_Change(sender As Object, user As Boolean) Handles SliderWindowOpacity.Change, SliderControlOpacity.Change, SliderBackgroundOpacity.Change, SliderBackgroundClarity.Change
        RefreshPersonalizationUi()
        If FrmMain Is Nothing Then Return
        If sender Is SliderWindowOpacity Then
            FrmMain.UpdateWindowOpacity()
        ElseIf sender Is SliderControlOpacity Then
            FrmMain.UpdateControlOpacity()
        Else
            FrmMain.LoadBackgroundImage()
        End If
    End Sub

    Private Sub PersonalizationTheme_Check(sender As Object, e As EventArgs) Handles RadioTheme0.Check, RadioTheme1.Check, RadioTheme5.Check, RadioTheme7.Check, RadioTheme9.Check, RadioTheme10.Check, RadioThemeCustom.Check
        RefreshPersonalizationUi()
        ThemeRefresh(Settings.Get(Of Integer)("UiLauncherTheme"))
        ThemeRefreshMain()
    End Sub

    Private Sub PersonalizationThemeSlider_Change(sender As Object, user As Boolean) Handles SliderThemeHue.Change, SliderThemeSat.Change, SliderThemeLight.Change
        RefreshPersonalizationUi()
        If Settings.Get(Of Integer)("UiLauncherTheme") = 14 Then
            ThemeNow = -1
            ThemeRefresh(14)
            ThemeRefreshMain()
        End If
    End Sub

    Private Sub BtnSelectBackgroundImage_Click(sender As Object, e As EventArgs) Handles BtnSelectBackgroundImage.Click
        Dim Dialog As New OpenFileDialog With {
            .Filter = "图像文件 (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|所有文件 (*.*)|*.*",
            .Title = "选择背景图片"
        }
        If Dialog.ShowDialog() = True Then
            Settings.Set("UiBackgroundImagePath", Dialog.FileName)
            RefreshPersonalizationUi()
            If FrmMain IsNot Nothing Then FrmMain.LoadBackgroundImage()
        End If
    End Sub

    Private Sub BtnResetBackgroundImage_Click(sender As Object, e As EventArgs) Handles BtnResetBackgroundImage.Click
        Settings.Set("UiBackgroundImagePath", "__default__")
        RefreshPersonalizationUi()
        If FrmMain IsNot Nothing Then FrmMain.LoadBackgroundImage()
    End Sub

    Private Sub BtnSolidBackground_Click(sender As Object, e As EventArgs) Handles BtnSolidBackground.Click
        Settings.Set("UiBackgroundImagePath", "__solid__")
        RefreshPersonalizationUi()
        If FrmMain IsNot Nothing Then FrmMain.LoadBackgroundImage()
    End Sub

    Private Sub RefreshPersonalizationUi()
        If LabWindowOpacity Is Nothing Then Return
        LabWindowOpacity.Text = "窗口不透明度：" & CInt(SliderWindowOpacity.Value) & "%"
        LabControlOpacity.Text = "控件不透明度：" & CInt(SliderControlOpacity.Value) & "%"
        LabBackgroundOpacity.Text = "背景图不透明度：" & CInt(SliderBackgroundOpacity.Value) & "%"
        LabBackgroundClarity.Text = "背景清晰度：" & CInt(SliderBackgroundClarity.Value) & "%"
        LabThemeHue.Text = "色调：" & CInt(SliderThemeHue.Value)
        LabThemeSat.Text = "饱和度：" & CInt(SliderThemeSat.Value) & "%"
        LabThemeLight.Text = "亮度微调：" & (CInt(SliderThemeLight.Value) - 20)
        CardCustomTheme.Visibility = If(Settings.Get(Of Integer)("UiLauncherTheme") = 14, Visibility.Visible, Visibility.Collapsed)

        Dim ImagePath = Settings.Get(Of String)("UiBackgroundImagePath")
        If String.IsNullOrWhiteSpace(ImagePath) OrElse ImagePath = "__default__" Then
            LabBackgroundImagePath.Text = "使用默认背景图"
        ElseIf ImagePath = "__solid__" Then
            LabBackgroundImagePath.Text = "使用纯色背景"
        Else
            LabBackgroundImagePath.Text = ImagePath
        End If
    End Sub

    Private Async Sub BtnInitializeSelected_Click(sender As Object, e As EventArgs) Handles BtnInitializeSelected.Click
        Dim InitializeSettings = CheckInitializeFrontendSettings.Checked
        Dim InitializeBackend = CheckInitializeBackendConfig.Checked
        Dim InitializeLibrary = CheckInitializeLibrary.Checked
        Dim InitializeSources = CheckInitializeMusicSources.Checked
        Dim InitializeCache = CheckInitializeCache.Checked
        If Not InitializeSettings AndAlso Not InitializeBackend AndAlso Not InitializeLibrary AndAlso Not InitializeSources AndAlso Not InitializeCache Then
            Hint("请至少选择一项初始化范围。", HintType.Blue)
            Return
        End If

        Dim Scope As New List(Of String)
        If InitializeSettings Then Scope.Add("前端设置")
        If InitializeBackend Then Scope.Add("后端配置、实例与历史")
        If InitializeLibrary Then Scope.Add("全部曲库")
        If InitializeSources Then Scope.Add("音乐源登录与模块数据")
        If InitializeCache Then Scope.Add("前端缓存")
        Dim Prompt = "确定要初始化以下内容吗？" & vbCrLf & String.Join("、", Scope) & vbCrLf & vbCrLf &
                     "此操作不可撤销；游戏集成安装状态会保留。"
        If MyMsgBox(Prompt, "初始化所选内容", "初始化", "取消", IsWarn:=True) <> 1 Then Return

        BtnInitializeSelected.IsEnabled = False
        Try
            Dim BackendRoots = Await GetInitializationBackendRootsAsync()
            Dim CacheRoots = GetInitializationCacheRoots()
            If (InitializeBackend OrElse InitializeLibrary OrElse InitializeSources) AndAlso BackendRoots.Count = 0 Then
                Throw New DirectoryNotFoundException("未找到有效的 OmniMix 后端目录，无法初始化后端数据。")
            End If
            If InitializeBackend OrElse InitializeLibrary OrElse InitializeSources Then Await StopBackendForInitializationAsync(BackendRoots)
            If InitializeSettings Then
                For Each Key In Settings.Entries.Keys.ToList()
                    Settings.Reset(Key)
                Next
                PathTemp = System.IO.Path.GetTempPath().TrimEnd("\"c, "/"c) & "\OmniMixPlayer\"
            End If
            Await Task.Run(Sub() InitializeSelectedData(BackendRoots, CacheRoots, InitializeBackend, InitializeLibrary, InitializeSources, InitializeCache))

            CurrentBaseUrl = ""
            HasCheckedBackend = False
            CurrentLibraryPlaylist = New OmniMixPlaylistData()
            CurrentLibraryTagSongs.Clear()
            CurrentModules.Clear()
            ExpandedLibraryAlbums.Clear()
            SettingService.RefreshSettings(PanSettingsPersonalization)
            If FrmMain IsNot Nothing Then FrmMain.UpdateBackgroundAndTitleBar()
            RefreshCachePathText()
            Await RefreshCacheSizeAsync()
            Hint("所选内容已初始化，游戏集成安装状态已保留。", HintType.Green)
        Catch Ex As Exception
            Logger.Warn(Ex, "初始化所选内容失败")
            Hint("初始化失败：" & Ex.Message, HintType.Red)
        Finally
            BtnInitializeSelected.IsEnabled = True
        End Try
    End Sub

    Private Async Function StopBackendForInitializationAsync(BackendRoots As IEnumerable(Of String)) As Task
        If Not String.IsNullOrWhiteSpace(CurrentBaseUrl) Then
            Try
                Await OmniMixApiClient.StopBackendAsync(CurrentBaseUrl)
            Catch Ex As Exception
                Logger.Warn(Ex, "初始化前通过 API 停止后端失败")
            End Try
        End If
        If Await OmniMixPlatformService.GetServiceStateAsync() = OmniMixServiceState.Running Then
            Await OmniMixPlatformService.StopServiceAsync()
        End If
        Await Task.Delay(1500)
        Await Task.Run(Sub() StopBackendProcesses(BackendRoots))
    End Function

    Private Shared Sub StopBackendProcesses(BackendRoots As IEnumerable(Of String))
        Dim NormalizedRoots = BackendRoots.
            Where(Function(Item) Not String.IsNullOrWhiteSpace(Item)).
            Select(Function(Item) System.IO.Path.GetFullPath(Item).TrimEnd("\"c, "/"c)).
            ToList()

        For Each Proc In Process.GetProcessesByName("OmniMixPlayer.Backend")
            Using Proc
                Try
                    Dim ProcessPath = Proc.MainModule?.FileName
                    If String.IsNullOrWhiteSpace(ProcessPath) Then Continue For
                    Dim ProcessRoot = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(ProcessPath)).TrimEnd("\"c, "/"c)
                    If Not NormalizedRoots.Any(Function(Item) String.Equals(Item, ProcessRoot, StringComparison.OrdinalIgnoreCase)) Then Continue For

                    If Not Proc.HasExited Then
                        Proc.Kill(True)
                        Proc.WaitForExit(5000)
                    End If
                Catch Ex As Exception
                    Logger.Warn(Ex, "初始化前停止残留后端进程失败")
                End Try
            End Using
        Next
    End Sub

    Private Async Function GetInitializationBackendRootsAsync() As Task(Of List(Of String))
        Dim BackendPaths As New List(Of String) From {
            OmniMixBackendManager.FindBackendExe(),
            OmniMixBackendManager.GetConfiguredBackendPath(),
            OmniMixBackendManager.FindDefaultBackendExe(),
            Await OmniMixPlatformService.GetServiceBinaryPathAsync()
        }
        Dim Result As New List(Of String)
        For Each BackendPath In BackendPaths
            If String.IsNullOrWhiteSpace(BackendPath) Then Continue For
            Try
                Dim FullBackendPath = System.IO.Path.GetFullPath(BackendPath.Trim().Trim(""""c))
                If Not File.Exists(FullBackendPath) OrElse
                   Not String.Equals(System.IO.Path.GetFileName(FullBackendPath), "OmniMixPlayer.Backend.exe", StringComparison.OrdinalIgnoreCase) Then Continue For
                Dim BackendRoot = System.IO.Path.GetDirectoryName(FullBackendPath).TrimEnd("\"c, "/"c)
                If Not Result.Any(Function(Item) String.Equals(Item, BackendRoot, StringComparison.OrdinalIgnoreCase)) Then Result.Add(BackendRoot)
            Catch
            End Try
        Next
        Return Result
    End Function

    Private Shared Function GetInitializationCacheRoots() As List(Of String)
        Dim Candidates As New List(Of String) From {
            PathTemp,
            Settings.Get(Of String)("SystemSystemCache"),
            System.IO.Path.GetTempPath().TrimEnd("\"c, "/"c) & "\OmniMixPlayer\"
        }
        Dim Result As New List(Of String)
        For Each Candidate In Candidates
            If String.IsNullOrWhiteSpace(Candidate) Then Continue For
            Try
                Dim FullPath = System.IO.Path.GetFullPath(Candidate).TrimEnd("\"c, "/"c)
                If Not Result.Any(Function(Item) String.Equals(Item, FullPath, StringComparison.OrdinalIgnoreCase)) Then Result.Add(FullPath)
            Catch
            End Try
        Next
        Return Result
    End Function

    Private Sub InitializeSelectedData(BackendRoots As IEnumerable(Of String), CacheRoots As IEnumerable(Of String), InitializeBackend As Boolean, InitializeLibrary As Boolean, InitializeSources As Boolean, InitializeCache As Boolean)
        For Each BackendRoot In BackendRoots
            Dim ConfigDir = System.IO.Path.Combine(BackendRoot, "config")

            If InitializeBackend Then
                DeleteMatchingFiles(ConfigDir, "global_config.json")
                DeleteMatchingFiles(ConfigDir, "omnimix_instances*.db")
            End If
            If InitializeLibrary Then
                DeleteMatchingFiles(ConfigDir, "omnimix_library*.db")
                Dim LocalFolderData = System.IO.Path.Combine(BackendRoot, "modules", "com.chillpatcher.localfolder", "data")
                DirectoryUtils.Delete(LocalFolderData)
            End If
            If InitializeSources Then
                If Directory.Exists(ConfigDir) Then
                    For Each ConfigFile In Directory.GetFiles(ConfigDir, "*.json")
                        If Not String.Equals(System.IO.Path.GetFileName(ConfigFile), "global_config.json", StringComparison.OrdinalIgnoreCase) Then FileUtils.Delete(ConfigFile)
                    Next
                End If
                Dim ModulesDir = System.IO.Path.Combine(BackendRoot, "modules")
                If Directory.Exists(ModulesDir) Then
                    DeleteKnownModuleLoginFiles(ModulesDir)
                    For Each ModuleDir In Directory.GetDirectories(ModulesDir)
                        DirectoryUtils.Delete(System.IO.Path.Combine(ModuleDir, "data"))
                    Next
                End If
            End If
        Next
        If InitializeSources Then ClearMusicSourceUserProfileData()
        If InitializeCache Then ClearFrontendCaches(CacheRoots)
    End Sub

    Private Shared Sub DeleteKnownModuleLoginFiles(ModulesDir As String)
        For Each RelativePath In {
            "com.chillpatcher.qqmusic\data\qqmusic_cookie.json",
            "com.chillpatcher.bilibili\data\bilibili_session.json",
            "com.chillpatcher.spotify\data\spotify_session.json"
        }
            FileUtils.Delete(System.IO.Path.Combine(ModulesDir, RelativePath))
        Next
    End Sub

    Private Shared Sub ClearMusicSourceUserProfileData()
        DirectoryUtils.Delete(System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "go-musicfox"))

        Dim WindowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
        If Not String.IsNullOrWhiteSpace(WindowsDir) Then
            DirectoryUtils.Delete(System.IO.Path.Combine(
                WindowsDir,
                "System32",
                "config",
                "systemprofile",
                "AppData",
                "Local",
                "go-musicfox"))
            DirectoryUtils.Delete(System.IO.Path.Combine(
                WindowsDir,
                "System32",
                "config",
                "systemprofile",
                "AppData",
                "Roaming",
                "go-musicfox"))
        End If
    End Sub

    Private Shared Sub DeleteMatchingFiles(DirectoryPath As String, Pattern As String)
        If Not Directory.Exists(DirectoryPath) Then Return
        For Each FilePath In Directory.GetFiles(DirectoryPath, Pattern)
            FileUtils.Delete(FilePath)
        Next
    End Sub

    Private Shared Sub ClearFrontendCache()
        ClearFrontendCaches({PathTemp})
    End Sub

    Private Shared Sub ClearFrontendCaches(CacheRoots As IEnumerable(Of String))
        For Each CacheRoot In CacheRoots
            If String.IsNullOrWhiteSpace(CacheRoot) Then Continue For
            For Each CacheName In {"Cache", "Download", "TaskTemp", "MyImage"}
                DirectoryUtils.Delete(System.IO.Path.Combine(CacheRoot, CacheName))
            Next
        Next
        Directory.CreateDirectory(System.IO.Path.Combine(PathTemp, "Cache", "Http"))
    End Sub

    Private Async Function RefreshCacheSizeAsync() As Task
        LabCacheSummary.Text = "正在统计缓存大小..."
        Try
            Dim Size = Await Task.Run(Function() GetFrontendCacheSize())
            LabCacheSummary.Text = "当前前端缓存：" & FormatFileSize(Size) & "。统计范围包含网络缓存、下载临时文件、任务临时文件和图片缓存。"
        Catch Ex As Exception
            Logger.Warn(Ex, "统计前端缓存大小失败")
            LabCacheSummary.Text = "缓存大小统计失败：" & Ex.Message
        End Try
    End Function

    Private Shared Function GetFrontendCacheSize() As Long
        Dim Total As Long = 0
        For Each CacheName In {"Cache", "Download", "TaskTemp", "MyImage"}
            Dim CachePath = System.IO.Path.Combine(PathTemp, CacheName)
            If Not Directory.Exists(CachePath) Then Continue For
            Try
                For Each Info In New DirectoryInfo(CachePath).EnumerateFiles("*", SearchOption.AllDirectories)
                    Total += Info.Length
                Next
            Catch
            End Try
        Next
        Return Total
    End Function

    Private Async Sub BtnBackendPathReset_Click(sender As Object, e As EventArgs) Handles BtnBackendPathReset.Click
        OmniMixBackendManager.SetConfiguredBackendPath("")
        Dim DefaultBackendPath = OmniMixBackendManager.FindDefaultBackendExe()
        TxtBackendPath.HintText = If(String.IsNullOrWhiteSpace(DefaultBackendPath), "OmniMixPlayer.Backend.exe", "默认：" & DefaultBackendPath)
        TxtBackendPath.Text = ""
        LabSettingsSummary.Text = "已恢复为默认构建后端。"
        Dim ServiceUpdated = Await OmniMixPlatformService.UpdateServiceBinaryPathAsync(DefaultBackendPath)
        If ServiceUpdated Then LabSettingsSummary.Text &= vbCrLf & "后端服务路径已同步。"
        Await RefreshServiceStatusAsync()
    End Sub

    Private Async Sub BtnBackendReconnect_Click(sender As Object, e As EventArgs) Handles BtnBackendReconnect.Click
        CurrentBaseUrl = ""
        Await RefreshBackendStatusAsync()
    End Sub

    Private Async Sub BtnBackendStop_Click(sender As Object, e As EventArgs) Handles BtnBackendStop.Click
        If String.IsNullOrWhiteSpace(CurrentBaseUrl) Then
            LabSettingsSummary.Text = "后端未连接，无需停止。"
            Return
        End If
        If MyMsgBox("确定要停止 OmniMix 后端吗？停止后曲库、播放控制和模块 UI 会暂时不可用，可稍后点击「启动/重连」恢复。", "停止后端", "停止", "取消", IsWarn:=True) <> 1 Then Return

        Try
            Dim StoppedUrl = CurrentBaseUrl
            LabSettingsSummary.Text = "正在停止 OmniMix 后端..."
            Await OmniMixApiClient.StopBackendAsync(StoppedUrl)
            CurrentBaseUrl = ""
            HasCheckedBackend = False
            LoadState.Visibility = Visibility.Visible
            LoadState.State.LoadingState = MyLoading.MyLoadingState.Error
            LabStatus.Text = InitialStatusText & vbCrLf & vbCrLf &
                "后端状态：已停止" & vbCrLf &
                "可在设置页点击「启动/重连」重新启动或发现后端。"
            FrmMain?.SetOmniMixConnectionStatus(False)
            LabSettingsSummary.Text = "后端已停止。"
            LabInstanceStats.Text = "实例统计：--"
            PanSettingsInstances.Children.Clear()
            LabArchiveSummary.Text = "归档：后端已停止。"
            PanSettingsArchives.Children.Clear()
            LabEqualizerSummary.Text = "均衡器：后端已停止。"
            PanEqualizerPresets.Children.Clear()
            PanEqualizerPoints.Children.Clear()
            CurrentEqualizerInstanceId = ""
            CurrentEqualizerState = Nothing
            CurrentEqualizerPresets.Clear()
            Hint("OmniMix 后端已停止。", HintType.Green)
        Catch Ex As Exception
            LabSettingsSummary.Text = "停止后端失败：" & Ex.Message
            Hint("停止 OmniMix 后端失败：" & Ex.Message, HintType.Red)
        End Try
    End Sub

    Private Async Function RefreshServiceStatusAsync() As Task
        If PageKey <> "Settings" Then Return

        Try
            LabServiceSummary.Text = "后端服务：正在读取..."
            Dim State = Await OmniMixPlatformService.GetServiceStateAsync()
            Dim AutoStart = Await OmniMixPlatformService.IsServiceAutoStartAsync()
            Dim BackendPath = OmniMixBackendManager.FindBackendExe()
            Dim RegisteredPath = If(State = OmniMixServiceState.NotInstalled, "", Await OmniMixPlatformService.GetServiceBinaryPathAsync())

            CheckServiceAutoStart.Checked = AutoStart

            BtnServiceInstall.Visibility = If(State = OmniMixServiceState.NotInstalled, Visibility.Visible, Visibility.Collapsed)
            BtnServiceUninstall.Visibility = If(State = OmniMixServiceState.NotInstalled, Visibility.Collapsed, Visibility.Visible)
            BtnServiceStart.Visibility = If(State = OmniMixServiceState.Installed, Visibility.Visible, Visibility.Collapsed)
            BtnServiceStop.Visibility = If(State = OmniMixServiceState.Running, Visibility.Visible, Visibility.Collapsed)
            BtnServiceToggleAutoStart.IsEnabled = State <> OmniMixServiceState.NotInstalled

            Dim StateText = GetServiceStateText(State)
            Dim AutoText = If(State = OmniMixServiceState.NotInstalled, "自启动：--", If(AutoStart, "自启动：已开启", "自启动：手动启动"))
            Dim PathText As String
            If State = OmniMixServiceState.NotInstalled Then
                PathText = If(String.IsNullOrWhiteSpace(BackendPath), "未找到后端 exe，暂不能安装服务。", "可安装后端服务：" & BackendPath)
            ElseIf String.IsNullOrWhiteSpace(RegisteredPath) Then
                PathText = "已安装，但未能读取服务路径。"
            ElseIf Not String.IsNullOrWhiteSpace(BackendPath) AndAlso Not OmniMixPlatformService.ArePathsEqual(BackendPath, RegisteredPath) Then
                PathText = "服务路径可能已过期：" & RegisteredPath
            Else
                PathText = "服务路径：" & RegisteredPath
            End If

            LabServiceSummary.Text = "后端服务：" & StateText & " · " & AutoText & vbCrLf & PathText
        Catch Ex As Exception
            LabServiceSummary.Text = "后端服务状态读取失败：" & Ex.Message
        End Try
    End Function

    Private Async Sub BtnServiceRefresh_Click(sender As Object, e As EventArgs) Handles BtnServiceRefresh.Click
        Await RefreshServiceStatusAsync()
    End Sub

    Private Async Sub BtnServiceInstall_Click(sender As Object, e As EventArgs) Handles BtnServiceInstall.Click
        Dim BackendPath = OmniMixBackendManager.FindBackendExe()
        If String.IsNullOrWhiteSpace(BackendPath) Then
            LabServiceSummary.Text = "未找到 OmniMixPlayer.Backend.exe，无法安装服务。"
            Return
        End If
        If MyMsgBox("将把 OmniMix 后端安装为 Windows 服务。这个操作可能会弹出 UAC 提权窗口。", "安装后端服务", "安装", "取消") <> 1 Then Return

        LabServiceSummary.Text = "正在安装后端服务..."
        Dim Success = Await OmniMixPlatformService.InstallServiceAsync()
        Hint(If(Success, "后端服务已安装。", "后端服务安装失败。"), If(Success, HintType.Green, HintType.Red))
        Await RefreshServiceStatusAsync()
    End Sub

    Private Async Sub BtnServiceUninstall_Click(sender As Object, e As EventArgs) Handles BtnServiceUninstall.Click
        If MyMsgBox("确定要卸载 OmniMix 后端服务吗？这不会删除后端程序文件，但服务模式将不可用。", "卸载后端服务", "卸载", "取消", IsWarn:=True) <> 1 Then Return

        LabServiceSummary.Text = "正在卸载后端服务..."
        Dim Success = Await OmniMixPlatformService.UninstallServiceAsync()
        Hint(If(Success, "后端服务已卸载。", "后端服务卸载失败。"), If(Success, HintType.Green, HintType.Red))
        Await RefreshServiceStatusAsync()
    End Sub

    Private Async Sub BtnServiceStart_Click(sender As Object, e As EventArgs) Handles BtnServiceStart.Click
        LabServiceSummary.Text = "正在启动后端服务..."
        Dim Success = Await OmniMixPlatformService.StartServiceAsync()
        Hint(If(Success, "后端服务启动命令已发送。", "后端服务启动失败。"), If(Success, HintType.Green, HintType.Red))
        Await RefreshServiceStatusAsync()
        If Success Then Await DiscoverBackendAfterServiceStartAsync()
    End Sub

    Private Async Sub BtnServiceStop_Click(sender As Object, e As EventArgs) Handles BtnServiceStop.Click
        If MyMsgBox("确定要停止 OmniMix 后端服务吗？停止后曲库、播放控制和模块 UI 会暂时不可用。", "停止后端服务", "停止", "取消", IsWarn:=True) <> 1 Then Return

        LabServiceSummary.Text = "正在停止后端服务..."
        Dim Success = Await OmniMixPlatformService.StopServiceAsync()
        If Success Then
            CurrentBaseUrl = ""
            FrmMain?.SetOmniMixConnectionStatus(False)
        End If
        Hint(If(Success, "后端服务已停止。", "后端服务停止失败。"), If(Success, HintType.Green, HintType.Red))
        Await RefreshServiceStatusAsync()
    End Sub

    Private Async Sub BtnServiceToggleAutoStart_Click(sender As Object, e As EventArgs) Handles BtnServiceToggleAutoStart.Click
        Dim State = Await OmniMixPlatformService.GetServiceStateAsync()
        If State = OmniMixServiceState.NotInstalled Then
            LabServiceSummary.Text = "服务尚未安装，无法切换自启动。"
            Return
        End If

        Dim CurrentAutoStart = Await OmniMixPlatformService.IsServiceAutoStartAsync()
        Dim Success = Await OmniMixPlatformService.SetServiceAutoStartAsync(Not CurrentAutoStart)
        Hint(If(Success, "服务自启动设置已更新。", "服务自启动设置失败。"), If(Success, HintType.Green, HintType.Red))
        Await RefreshServiceStatusAsync()
    End Sub

    Private Async Function DiscoverBackendAfterServiceStartAsync() As Task
        For i = 0 To 12
            Await Task.Delay(500)
            Dim Status = Await OmniMixApiClient.DiscoverAsync()
            If Status.IsOnline Then
                CurrentBaseUrl = Status.BaseUrl
                FrmMain?.SetOmniMixConnectionStatus(True, Status.BaseUrl)
                Await SyncGameIntegrationBindingsAsync(Status.BaseUrl)
                LabStatus.Text = InitialStatusText & vbCrLf & vbCrLf &
                    "后端状态：在线" & vbCrLf &
                    "连接地址：" & Status.BaseUrl & vbCrLf &
                    "启动方式：Windows 服务"
                If PageKey = "Settings" Then Await RefreshSettingsAsync(Status.BaseUrl)
                Return
            End If
        Next

        FrmMain?.SetOmniMixConnectionStatus(False)
        LabServiceSummary.Text = LabServiceSummary.Text & vbCrLf & "服务启动后暂未发现 /api/health，可稍后刷新。"
    End Function

    Private Shared Function GetServiceStateText(State As OmniMixServiceState) As String
        Select Case State
            Case OmniMixServiceState.Running
                Return "运行中"
            Case OmniMixServiceState.Installed
                Return "已安装，未运行"
            Case Else
                Return "未安装"
        End Select
    End Function

    Private Async Sub BtnArchivesRefresh_Click(sender As Object, e As EventArgs) Handles BtnArchivesRefresh.Click
        If String.IsNullOrWhiteSpace(CurrentBaseUrl) Then
            Await RefreshBackendStatusAsync()
        Else
            Await RefreshSettingsAsync(CurrentBaseUrl)
        End If
    End Sub

    Private Async Sub SettingsInstanceSelectButton_Click(sender As Object, e As EventArgs)
        Dim Button = CType(sender, MyIconButton)
        Dim Instance = TryCast(Button.Tag, OmniMixPlaybackInstanceInfo)
        If Instance Is Nothing OrElse String.IsNullOrWhiteSpace(CurrentBaseUrl) OrElse String.IsNullOrWhiteSpace(Instance.Id) Then Return

        Try
            Await OmniMixApiClient.SetActiveInstanceAsync(CurrentBaseUrl, Instance.Id)
            ActiveInstanceId = Instance.Id
            LabInstanceStats.Text = "已设为当前实例：" & NonEmpty(Instance.GameName, Instance.Id)
            Await RefreshSettingsAsync(CurrentBaseUrl)
        Catch Ex As Exception
            LabInstanceStats.Text = "设置当前实例失败：" & Ex.Message
        End Try
    End Sub

    Private Async Sub SettingsInstanceMetaButton_Click(sender As Object, e As EventArgs)
        Dim Button = CType(sender, MyIconButton)
        Dim Instance = TryCast(Button.Tag, OmniMixPlaybackInstanceInfo)
        If Instance Is Nothing OrElse String.IsNullOrWhiteSpace(CurrentBaseUrl) OrElse String.IsNullOrWhiteSpace(Instance.Id) Then Return

        Dim GameName = MyMsgBoxInput(
            "编辑实例元数据",
            "修改实例“" & Instance.Id & "”显示的游戏名称。",
            NonEmpty(Instance.GameName, Instance.Id),
            HintText:="游戏名称")
        If GameName Is Nothing Then Return

        Dim ModId = MyMsgBoxInput(
            "编辑实例元数据",
            "修改实例绑定的 Mod ID。",
            If(Instance.ModId, ""),
            HintText:="Mod ID")
        If ModId Is Nothing Then Return

        Dim Mode = MyMsgBoxInput(
            "编辑实例元数据",
            "修改实例模式。通常保持 ServerManaged 或 ClientManaged。",
            NonEmpty(Instance.Mode, "ServerManaged"),
            HintText:="ServerManaged / ClientManaged")
        If Mode Is Nothing Then Return

        Try
            Await OmniMixApiClient.SetInstanceMetaAsync(CurrentBaseUrl, Instance.Id, ModId.Trim(), GameName.Trim(), Mode.Trim())
            LabInstanceStats.Text = "实例元数据已保存：" & NonEmpty(GameName, Instance.Id)
            Await RefreshSettingsAsync(CurrentBaseUrl)
        Catch Ex As Exception
            LabInstanceStats.Text = "实例元数据保存失败：" & Ex.Message
        End Try
    End Sub

    Private Async Sub SettingsInstanceDeleteButton_Click(sender As Object, e As EventArgs)
        Dim Button = CType(sender, MyIconButton)
        Dim Instance = TryCast(Button.Tag, OmniMixPlaybackInstanceInfo)
        If Instance Is Nothing OrElse String.IsNullOrWhiteSpace(CurrentBaseUrl) OrElse String.IsNullOrWhiteSpace(Instance.Id) Then Return
        If Instance.Attached Then
            LabInstanceStats.Text = "实例仍在线，暂不删除。"
            Return
        End If

        If MyMsgBox("确定要删除离线实例“" & NonEmpty(Instance.GameName, Instance.Id) & "”吗？此操作会删除该实例的后端记录。", "删除实例", "删除", "取消", IsWarn:=True) <> 1 Then Return

        Try
            Await OmniMixApiClient.DeleteInstanceAsync(CurrentBaseUrl, Instance.Id)
            If String.Equals(ActiveInstanceId, Instance.Id, StringComparison.OrdinalIgnoreCase) Then ActiveInstanceId = ""
            LabInstanceStats.Text = "实例已删除：" & Instance.Id
            Await RefreshSettingsAsync(CurrentBaseUrl)
        Catch Ex As Exception
            LabInstanceStats.Text = "删除实例失败：" & Ex.Message
        End Try
    End Sub

    Private Async Sub SettingsInstanceArchiveButton_Click(sender As Object, e As EventArgs)
        Dim Button = CType(sender, MyIconButton)
        Dim Instance = TryCast(Button.Tag, OmniMixPlaybackInstanceInfo)
        If Instance Is Nothing OrElse String.IsNullOrWhiteSpace(CurrentBaseUrl) OrElse String.IsNullOrWhiteSpace(Instance.Id) Then Return

        Dim Label = MyMsgBoxInput(
            "保存实例归档",
            "将实例“" & Instance.Id & "”的当前播放列表、队列和均衡器设置保存为归档。",
            NonEmpty(Instance.GameName, Instance.Id),
            HintText:="归档名称")
        If Label Is Nothing Then Return

        Try
            Await OmniMixApiClient.ArchiveInstanceAsync(CurrentBaseUrl, Instance.Id, Label)
            LabArchiveSummary.Text = "已保存实例归档：" & NonEmpty(Label, Instance.Id)
            Await RefreshSettingsAsync(CurrentBaseUrl)
        Catch Ex As Exception
            LabArchiveSummary.Text = "保存实例归档失败：" & Ex.Message
        End Try
    End Sub

    Private Async Sub ArchiveRenameButton_Click(sender As Object, e As EventArgs)
        Dim Button = CType(sender, MyIconButton)
        Dim Archive = TryCast(Button.Tag, OmniMixArchiveInfo)
        If Archive Is Nothing OrElse String.IsNullOrWhiteSpace(CurrentBaseUrl) OrElse String.IsNullOrWhiteSpace(Archive.InstanceId) Then Return

        Dim Label = MyMsgBoxInput(
            "重命名归档",
            "修改归档“" & Archive.DisplayName & "”的显示名称。",
            Archive.Label,
            HintText:="归档名称")
        If Label Is Nothing Then Return

        Try
            Await OmniMixApiClient.RenameArchiveAsync(CurrentBaseUrl, Archive.InstanceId, Label)
            LabArchiveSummary.Text = "归档已重命名。"
            Await RefreshSettingsAsync(CurrentBaseUrl)
        Catch Ex As Exception
            LabArchiveSummary.Text = "重命名归档失败：" & Ex.Message
        End Try
    End Sub

    Private Async Sub ArchiveInheritButton_Click(sender As Object, e As EventArgs)
        Dim Button = CType(sender, MyIconButton)
        Dim Payload = TryCast(Button.Tag, Tuple(Of OmniMixArchiveInfo, String))
        If Payload Is Nothing OrElse String.IsNullOrWhiteSpace(CurrentBaseUrl) Then Return

        Dim Archive = Payload.Item1
        If Archive Is Nothing OrElse String.IsNullOrWhiteSpace(Archive.InstanceId) Then Return

        Dim DefaultTargetId = Payload.Item2
        If String.IsNullOrWhiteSpace(DefaultTargetId) Then
            Try
                Dim Instances = Await OmniMixApiClient.GetInstancesAsync(CurrentBaseUrl)
                Dim Candidate = PickActiveInstance(Instances)
                If Candidate IsNot Nothing Then DefaultTargetId = Candidate.Id
            Catch
            End Try
        End If

        Dim TargetId = MyMsgBoxInput(
            "应用归档",
            "输入要继承归档“" & Archive.DisplayName & "”的目标实例 ID。归档未绑定在线实例时可能会被消费；已绑定时会复制设置。",
            If(DefaultTargetId, ""),
            HintText:="目标实例 ID")
        If TargetId Is Nothing Then Return
        TargetId = TargetId.Trim()
        If String.IsNullOrWhiteSpace(TargetId) Then
            LabArchiveSummary.Text = "目标实例 ID 不能为空。"
            Return
        End If

        If MyMsgBox("确定要把归档“" & Archive.DisplayName & "”应用到实例“" & TargetId & "”吗？", "应用归档", "应用", "取消") <> 1 Then Return

        Try
            Dim Result = Await OmniMixApiClient.InheritFromArchiveAsync(CurrentBaseUrl, TargetId, Archive.InstanceId)
            Dim Consumed = False
            Dim ConsumedElement As JsonElement
            If Result IsNot Nothing AndAlso Result.TryGetValue("consumed", ConsumedElement) AndAlso ConsumedElement.ValueKind = JsonValueKind.True Then
                Consumed = True
            End If
            LabArchiveSummary.Text = If(Consumed, "归档已消费并继承到实例：", "归档设置已复制到实例：") & TargetId
            Await RefreshSettingsAsync(CurrentBaseUrl)
        Catch Ex As Exception
            LabArchiveSummary.Text = "应用归档失败：" & Ex.Message
        End Try
    End Sub

    Private Async Sub ArchiveDeleteButton_Click(sender As Object, e As EventArgs)
        Dim Button = CType(sender, MyIconButton)
        Dim Archive = TryCast(Button.Tag, OmniMixArchiveInfo)
        If Archive Is Nothing OrElse String.IsNullOrWhiteSpace(CurrentBaseUrl) OrElse String.IsNullOrWhiteSpace(Archive.InstanceId) Then Return

        If MyMsgBox("确定要删除归档“" & Archive.DisplayName & "”吗？此操作不可撤销。", "删除归档", "删除", "取消", IsWarn:=True) <> 1 Then Return

        Try
            Await OmniMixApiClient.DeleteArchiveAsync(CurrentBaseUrl, Archive.InstanceId)
            LabArchiveSummary.Text = "归档已删除。"
            Await RefreshSettingsAsync(CurrentBaseUrl)
        Catch Ex As Exception
            LabArchiveSummary.Text = "删除归档失败：" & Ex.Message
        End Try
    End Sub

    Private Async Function RefreshEqualizerAsync(BaseUrl As String) As Task
        If PageKey <> "Settings" OrElse Not String.Equals(CurrentSettingsPane, "equalizer", StringComparison.OrdinalIgnoreCase) Then Return

        LabEqualizerSummary.Text = "正在读取均衡器..."
        PanEqualizerPresets.Children.Clear()
        PanEqualizerPoints.Children.Clear()
        SetEqualizerButtonsEnabled(False)

        Try
            Dim Instance = Await GetControllableInstanceAsync()
            If Instance Is Nothing OrElse String.IsNullOrWhiteSpace(Instance.Id) Then
                CurrentEqualizerInstanceId = ""
                CurrentEqualizerState = Nothing
                CurrentEqualizerPresets.Clear()
                LabEqualizerSummary.Text = "均衡器：暂无播放实例。等待游戏或音频端连接后可编辑。"
                LabEqualizerGraphStatus.Text = "暂无可编辑实例"
                LabEqualizerEnabled.Text = "-"
                LabEqualizerGain.Text = "-"
                LabEqualizerSoftClip.Text = "-"
                CanvasEqualizerGraph.Children.Clear()
                PanEqualizerPoints.Children.Add(New MyListItem With {
                    .Title = "暂无可编辑实例",
                    .Info = "后端在线，但还没有播放实例可用于读取均衡器。",
                    .Height = 50,
                    .PaddingLeft = 8,
                    .Margin = New Thickness(0, 0, 0, 4),
                    .IsScaleAnimationEnabled = False,
                    .Type = MyListItem.CheckType.Clickable
                })
                Return
            End If

            CurrentEqualizerInstanceId = Instance.Id
            CurrentEqualizerState = NormalizeEqualizerState(Await OmniMixApiClient.GetInstanceEqualizerAsync(BaseUrl, Instance.Id))
            Try
                CurrentEqualizerPresets = Await OmniMixApiClient.GetInstanceEqualizerPresetsAsync(BaseUrl, Instance.Id)
            Catch
                CurrentEqualizerPresets = New Dictionary(Of String, OmniMixEqualizerStateInfo)
            End Try
            RenderEqualizer(Instance, CurrentEqualizerState, CurrentEqualizerPresets)
        Catch Ex As Exception
            CurrentEqualizerInstanceId = ""
            CurrentEqualizerState = Nothing
            CurrentEqualizerPresets.Clear()
            LabEqualizerSummary.Text = "均衡器读取失败：" & Ex.Message
            SetEqualizerButtonsEnabled(False)
        End Try
    End Function

    Private Sub RenderEqualizer(Instance As OmniMixPlaybackInstanceInfo, State As OmniMixEqualizerStateInfo, Presets As Dictionary(Of String, OmniMixEqualizerStateInfo))
        State = NormalizeEqualizerState(State)
        Presets = If(Presets, New Dictionary(Of String, OmniMixEqualizerStateInfo))

        SetEqualizerButtonsEnabled(True)
        BtnEqualizerToggle.Text = If(State.Enabled, "禁用均衡器", "启用均衡器")
        BtnEqualizerSoftClip.Text = If(State.SoftClipEnabled, "关闭软削波", "开启软削波")
        LabEqualizerSummary.Text = $"实例 {NonEmpty(Instance.Id, Instance.ClientId)} · {BuildEqualizerStateInfo(State)}"
        LabEqualizerGraphStatus.Text = NonEmpty(Instance.GameName, NonEmpty(Instance.Id, Instance.ClientId)) & " · " & State.Points.Count & " 个控制点"
        LabEqualizerEnabled.Text = If(State.Enabled, "已启用", "未启用")
        LabEqualizerGain.Text = FormatDb(State.GlobalGainDb)
        LabEqualizerSoftClip.Text = If(State.SoftClipEnabled, "开启", "关闭")
        If Not State.Points.Any(Function(Item) String.Equals(Item.Id, EqualizerSelectedPointId, StringComparison.OrdinalIgnoreCase)) Then
            EqualizerSelectedPointId = If(State.Points.Count = 0, "", State.Points.OrderBy(Function(Item) Item.Frequency).First().Id)
        End If
        RenderEqualizerGraph(State)

        PanEqualizerPresets.Children.Clear()
        If Presets.Count = 0 Then
            PanEqualizerPresets.Children.Add(New MyListItem With {
                .Title = "暂无预设",
                .Info = "后端暂未返回均衡器预设。",
                .Height = 50,
                .PaddingLeft = 8,
                .Margin = New Thickness(0, 0, 0, 2),
                .IsScaleAnimationEnabled = False,
                .Type = MyListItem.CheckType.Clickable
            })
        Else
            For Each Preset In Presets.OrderBy(Function(Item) Item.Key)
                Dim Item As New MyListItem With {
                    .Title = Preset.Key,
                    .Info = BuildEqualizerStateInfo(Preset.Value),
                    .Height = 52,
                    .PaddingLeft = 8,
                    .Margin = New Thickness(0, 0, 0, 4),
                    .IsScaleAnimationEnabled = False,
                    .Type = MyListItem.CheckType.Clickable,
                    .Tag = Preset
                }
                Dim ApplyButton As New MyIconButton With {
                    .Logo = Logo.IconButtonOpen,
                    .LogoScale = 1.05,
                    .ToolTip = "应用预设",
                    .Tag = Tuple.Create(Preset.Key, Preset.Value)
                }
                AddHandler ApplyButton.Click, AddressOf EqualizerPresetApplyButton_Click
                Item.Buttons = New List(Of MyIconButton) From {ApplyButton}
                PanEqualizerPresets.Children.Add(Item)
            Next
        End If

        PanEqualizerPoints.Children.Clear()
        If State.Points.Count = 0 Then
            PanEqualizerPoints.Children.Add(New MyListItem With {
                .Title = "暂无控制点",
                .Info = "当前为平直响应，可添加控制点或应用预设。",
                .Height = 56,
                .PaddingLeft = 8,
                .Margin = New Thickness(0, 0, 0, 4),
                .IsScaleAnimationEnabled = False,
                .Type = MyListItem.CheckType.Clickable
            })
            Return
        End If

        For Each Point In State.Points.OrderBy(Function(Item) Item.Frequency)
            Dim Item As New MyListItem With {
                .Title = GetEqualizerTypeLabel(Point.Type) & " · " & FormatFrequency(Point.Frequency),
                .Info = $"{FormatDb(Point.GainDb)} · Q {Point.Q.ToString("0.##", CultureInfo.InvariantCulture)} · {Point.Id}",
                .Height = 56,
                .PaddingLeft = 8,
                .Margin = New Thickness(0, 0, 0, 4),
                .IsScaleAnimationEnabled = False,
                .Type = MyListItem.CheckType.Clickable,
                .Tag = Point
            }

            Dim EditButton As New MyIconButton With {
                .Logo = Logo.IconButtonEdit,
                .LogoScale = 1.0,
                .ToolTip = "编辑控制点",
                .Tag = Point
            }
            AddHandler EditButton.Click, AddressOf EqualizerPointEditButton_Click

            Dim DeleteButton As New MyIconButton With {
                .Logo = Logo.IconButtonDelete,
                .LogoScale = 1.0,
                .ToolTip = "删除控制点",
                .Tag = Point
            }
            AddHandler DeleteButton.Click, AddressOf EqualizerPointDeleteButton_Click

            Item.Buttons = New List(Of MyIconButton) From {EditButton, DeleteButton}
            PanEqualizerPoints.Children.Add(Item)
        Next
    End Sub

    Private Sub RenderEqualizerGraph(State As OmniMixEqualizerStateInfo)
        If CanvasEqualizerGraph Is Nothing Then Return
        State = NormalizeEqualizerState(State)

        Dim Width = CanvasEqualizerGraph.ActualWidth
        Dim Height = CanvasEqualizerGraph.ActualHeight
        If Width < 80 OrElse Height < 80 Then
            CanvasEqualizerGraph.Dispatcher.BeginInvoke(Sub() RenderEqualizerGraph(State), System.Windows.Threading.DispatcherPriority.Background)
            Return
        End If

        CanvasEqualizerGraph.Children.Clear()

        Dim GridBrush = GraphBrush("ColorBrushGray5", Colors.Gray, 0.38)
        Dim TextBrush = GraphBrush("ColorBrushGray3", Colors.Gray, 0.85)
        Dim BaseBrush = GraphBrush("ColorBrushGray3", Colors.Gray, 0.7)
        Dim CurveBrush = GraphBrush("ColorBrush3", Colors.DeepSkyBlue, If(State.Enabled, 1, 0.42))
        Dim FillBrush = GraphBrush("ColorBrush3", Colors.DeepSkyBlue, If(State.Enabled, 0.15, 0.06))
        Dim PointBrush = GraphBrush("ColorBrush2", Colors.DeepSkyBlue, If(State.Enabled, 1, 0.5))
        Dim SelectedBrush = GraphBrush("ColorBrush3", Colors.DeepSkyBlue, 1)

        Dim Frequencies = New Integer() {20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000}
        For Each Frequency In Frequencies
            Dim X = EqualizerFrequencyToX(Frequency, Width)
            AddGraphLine(X, EqualizerPlotTop, X, Height - EqualizerPlotBottom, GridBrush, 1)
            AddGraphText(If(Frequency >= 1000, (Frequency \ 1000).ToString() & "k", Frequency.ToString()), X + 3, Height - 20, TextBrush, 10)
        Next

        Dim Gains = New Integer() {-24, -18, -12, -6, 0, 6, 12, 18, 24}
        For Each Gain In Gains
            Dim Y = EqualizerGainToY(Gain, Height)
            AddGraphLine(EqualizerPlotLeft, Y, Width - EqualizerPlotRight, Y, If(Gain = 0, BaseBrush, GridBrush), If(Gain = 0, 1.6, 1))
            AddGraphText(If(Gain > 0, "+", "") & Gain.ToString() & " dB", 6, Y - 8, TextBrush, 10)
        Next

        Dim Filters = State.Points.Select(Function(PointInfo) New EqualizerBiquad(PointInfo.Frequency, PointInfo.GainDb, PointInfo.Q, PointInfo.Type, 48000)).ToList()
        Dim CurvePoints As New PointCollection()
        Dim FillPoints As New PointCollection()
        Dim BaseY = EqualizerGainToY(0, Height)
        FillPoints.Add(New Point(EqualizerPlotLeft, BaseY))

        For i = 0 To 199
            Dim T = i / 199.0
            Dim Frequency = Math.Exp(Math.Log(20) + T * (Math.Log(20000) - Math.Log(20)))
            Dim TotalGain = State.GlobalGainDb
            For Each EqFilter In Filters
                TotalGain += EqFilter.GetMagnitudeDb(Frequency, 48000)
            Next
            TotalGain = Clamp(TotalGain, -24, 24)
            Dim X = EqualizerFrequencyToX(Frequency, Width)
            Dim Y = EqualizerGainToY(TotalGain, Height)
            CurvePoints.Add(New Point(X, Y))
            FillPoints.Add(New Point(X, Y))
        Next

        FillPoints.Add(New Point(Width - EqualizerPlotRight, BaseY))
        CanvasEqualizerGraph.Children.Add(New Polygon With {
            .Points = FillPoints,
            .Fill = FillBrush,
            .IsHitTestVisible = False
        })
        CanvasEqualizerGraph.Children.Add(New Polyline With {
            .Points = CurvePoints,
            .Stroke = CurveBrush,
            .StrokeThickness = 2.6,
            .StrokeLineJoin = PenLineJoin.Round,
            .IsHitTestVisible = False
        })

        If Math.Abs(State.GlobalGainDb) > 0.05 Then
            Dim GainY = EqualizerGainToY(State.GlobalGainDb, Height)
            AddGraphLine(EqualizerPlotLeft, GainY, Width - EqualizerPlotRight, GainY, GraphBrush("ColorBrush2", Colors.DeepSkyBlue, 0.55), 1.2)
            AddGraphText("Pre " & FormatDb(State.GlobalGainDb), Width - 72, GainY - 16, TextBrush, 10)
        End If

        For Each PointInfo In State.Points.OrderBy(Function(Item) Item.Frequency)
            Dim X = EqualizerFrequencyToX(PointInfo.Frequency, Width)
            Dim Y = EqualizerGainToY(If(IsPassEqualizerType(PointInfo.Type), 0, PointInfo.GainDb), Height)
            Dim Selected = String.Equals(PointInfo.Id, EqualizerSelectedPointId, StringComparison.OrdinalIgnoreCase)
            Dim Radius = If(Selected, 8.5, 6.5)
            If Selected Then
                Dim Halo As New Ellipse With {
                    .Width = 28,
                    .Height = 28,
                    .Fill = GraphBrush("ColorBrush3", Colors.DeepSkyBlue, 0.18),
                    .IsHitTestVisible = False
                }
                Canvas.SetLeft(Halo, X - 14)
                Canvas.SetTop(Halo, Y - 14)
                CanvasEqualizerGraph.Children.Add(Halo)
            End If

            Dim Dot As New Ellipse With {
                .Width = Radius * 2,
                .Height = Radius * 2,
                .Fill = If(Selected, SelectedBrush, PointBrush),
                .Stroke = GraphBrush("ColorBrushBackground", Colors.White, 0.96),
                .StrokeThickness = 2,
                .Tag = PointInfo,
                .Cursor = Cursors.Hand
            }
            Canvas.SetLeft(Dot, X - Radius)
            Canvas.SetTop(Dot, Y - Radius)
            AddHandler Dot.MouseLeftButtonDown, AddressOf EqualizerPointDot_MouseLeftButtonDown
            CanvasEqualizerGraph.Children.Add(Dot)

            AddGraphText(FormatFrequency(PointInfo.Frequency), X - 18, Y - 24, TextBrush, 10)
        Next
    End Sub

    Private Sub AddGraphLine(X1 As Double, Y1 As Double, X2 As Double, Y2 As Double, Brush As Brush, Thickness As Double)
        CanvasEqualizerGraph.Children.Add(New Line With {
            .X1 = X1,
            .Y1 = Y1,
            .X2 = X2,
            .Y2 = Y2,
            .Stroke = Brush,
            .StrokeThickness = Thickness,
            .SnapsToDevicePixels = True,
            .IsHitTestVisible = False
        })
    End Sub

    Private Sub AddGraphText(Text As String, X As Double, Y As Double, Brush As Brush, Size As Double)
        Dim Label As New TextBlock With {
            .Text = Text,
            .FontSize = Size,
            .Foreground = Brush,
            .IsHitTestVisible = False
        }
        Canvas.SetLeft(Label, X)
        Canvas.SetTop(Label, Y)
        CanvasEqualizerGraph.Children.Add(Label)
    End Sub

    Private Function GraphBrush(Key As String, Fallback As Color, Optional Opacity As Double = 1) As Brush
        Dim Found = TryCast(TryFindResource(Key), Brush)
        Dim Result = If(Found Is Nothing, New SolidColorBrush(Fallback), Found.Clone())
        Result.Opacity *= Opacity
        Return Result
    End Function

    Private Shared Function EqualizerFrequencyToX(Frequency As Double, Width As Double) As Double
        Frequency = Clamp(Frequency, 20, 20000)
        Dim PlotWidth = Math.Max(1, Width - EqualizerPlotLeft - EqualizerPlotRight)
        Dim T = (Math.Log(Frequency) - Math.Log(20)) / (Math.Log(20000) - Math.Log(20))
        Return EqualizerPlotLeft + T * PlotWidth
    End Function

    Private Shared Function EqualizerXToFrequency(X As Double, Width As Double) As Double
        Dim PlotWidth = Math.Max(1, Width - EqualizerPlotLeft - EqualizerPlotRight)
        Dim T = Clamp((X - EqualizerPlotLeft) / PlotWidth, 0, 1)
        Return Math.Exp(Math.Log(20) + T * (Math.Log(20000) - Math.Log(20)))
    End Function

    Private Shared Function EqualizerGainToY(GainDb As Double, Height As Double) As Double
        Dim PlotHeight = Math.Max(1, Height - EqualizerPlotTop - EqualizerPlotBottom)
        Dim T = (Clamp(GainDb, -24, 24) + 24) / 48
        Return EqualizerPlotTop + (1 - T) * PlotHeight
    End Function

    Private Shared Function EqualizerYToGain(Y As Double, Height As Double) As Double
        Dim PlotHeight = Math.Max(1, Height - EqualizerPlotTop - EqualizerPlotBottom)
        Dim T = Clamp(1 - ((Y - EqualizerPlotTop) / PlotHeight), 0, 1)
        Return -24 + T * 48
    End Function

    Private Shared Function IsPassEqualizerType(TypeValue As String) As Boolean
        Dim Normalized = NormalizeEqualizerType(TypeValue)
        Return Normalized = "LowPass" OrElse Normalized = "HighPass"
    End Function

    Private Sub CanvasEqualizerGraph_SizeChanged(sender As Object, e As SizeChangedEventArgs) Handles CanvasEqualizerGraph.SizeChanged
        If CurrentEqualizerState IsNot Nothing Then RenderEqualizerGraph(CurrentEqualizerState)
    End Sub

    Private Async Sub CanvasEqualizerGraph_MouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs) Handles CanvasEqualizerGraph.MouseLeftButtonDown
        If CurrentEqualizerState Is Nothing Then Return

        Dim Position = e.GetPosition(CanvasEqualizerGraph)
        If e.ClickCount >= 2 Then
            Dim State = CloneEqualizerState(CurrentEqualizerState)
            Dim TypeValue = "Peaking"
            Dim NewPoint As New OmniMixEqualizerPointInfo With {
                .Id = "vb_" & Guid.NewGuid().ToString("N"),
                .Frequency = EqualizerXToFrequency(Position.X, CanvasEqualizerGraph.ActualWidth),
                .GainDb = EqualizerYToGain(Position.Y, CanvasEqualizerGraph.ActualHeight),
                .Q = 1,
                .Type = TypeValue
            }
            State.Points.Add(NewPoint)
            EqualizerSelectedPointId = NewPoint.Id
            Await SaveEqualizerStateAsync(State, "已添加控制点。")
            e.Handled = True
            Return
        End If

        Dim Hit = FindEqualizerPointAt(Position)
        EqualizerSelectedPointId = If(Hit Is Nothing, "", Hit.Id)
        IsDraggingEqualizerPoint = Hit IsNot Nothing
        IsEqualizerDragChanged = False
        If IsDraggingEqualizerPoint Then CanvasEqualizerGraph.CaptureMouse()
        RenderEqualizerGraph(CurrentEqualizerState)
        e.Handled = True
    End Sub

    Private Sub EqualizerPointDot_MouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs)
        Dim PointInfo = TryCast(CType(sender, FrameworkElement).Tag, OmniMixEqualizerPointInfo)
        If PointInfo Is Nothing Then Return
        EqualizerSelectedPointId = PointInfo.Id
        IsDraggingEqualizerPoint = True
        IsEqualizerDragChanged = False
        CanvasEqualizerGraph.CaptureMouse()
        RenderEqualizerGraph(CurrentEqualizerState)
        e.Handled = True
    End Sub

    Private Sub CanvasEqualizerGraph_MouseMove(sender As Object, e As MouseEventArgs) Handles CanvasEqualizerGraph.MouseMove
        If Not IsDraggingEqualizerPoint OrElse CurrentEqualizerState Is Nothing Then Return
        If e.LeftButton <> MouseButtonState.Pressed Then
            IsDraggingEqualizerPoint = False
            CanvasEqualizerGraph.ReleaseMouseCapture()
            Return
        End If

        Dim Target = CurrentEqualizerState.Points.FirstOrDefault(Function(Item) String.Equals(Item.Id, EqualizerSelectedPointId, StringComparison.OrdinalIgnoreCase))
        If Target Is Nothing Then Return

        Dim Position = e.GetPosition(CanvasEqualizerGraph)
        Target.Frequency = EqualizerXToFrequency(Position.X, CanvasEqualizerGraph.ActualWidth)
        If Not IsPassEqualizerType(Target.Type) Then Target.GainDb = EqualizerYToGain(Position.Y, CanvasEqualizerGraph.ActualHeight)
        IsEqualizerDragChanged = True
        LabEqualizerGraphStatus.Text = $"{GetEqualizerTypeLabel(Target.Type)} · {FormatFrequency(Target.Frequency)} · {FormatDb(Target.GainDb)} · Q {Target.Q.ToString("0.##", CultureInfo.InvariantCulture)}"
        RenderEqualizerGraph(CurrentEqualizerState)
        e.Handled = True
    End Sub

    Private Async Sub CanvasEqualizerGraph_MouseLeftButtonUp(sender As Object, e As MouseButtonEventArgs) Handles CanvasEqualizerGraph.MouseLeftButtonUp
        If Not IsDraggingEqualizerPoint Then Return
        IsDraggingEqualizerPoint = False
        CanvasEqualizerGraph.ReleaseMouseCapture()
        If IsEqualizerDragChanged Then
            IsEqualizerDragChanged = False
            Await SaveEqualizerStateAsync(CloneEqualizerState(CurrentEqualizerState), "已更新控制点。")
        End If
        e.Handled = True
    End Sub

    Private Function FindEqualizerPointAt(Position As Point) As OmniMixEqualizerPointInfo
        If CurrentEqualizerState Is Nothing Then Return Nothing
        Dim Width = CanvasEqualizerGraph.ActualWidth
        Dim Height = CanvasEqualizerGraph.ActualHeight
        Dim Best As OmniMixEqualizerPointInfo = Nothing
        Dim BestDistance = 18.0

        For Each PointInfo In CurrentEqualizerState.Points
            Dim X = EqualizerFrequencyToX(PointInfo.Frequency, Width)
            Dim Y = EqualizerGainToY(If(IsPassEqualizerType(PointInfo.Type), 0, PointInfo.GainDb), Height)
            Dim Distance = Math.Sqrt(Math.Pow(X - Position.X, 2) + Math.Pow(Y - Position.Y, 2))
            If Distance < BestDistance Then
                BestDistance = Distance
                Best = PointInfo
            End If
        Next

        Return Best
    End Function

    Private Class EqualizerBiquad
        Private ReadOnly B0 As Double
        Private ReadOnly B1 As Double
        Private ReadOnly B2 As Double
        Private ReadOnly A1 As Double
        Private ReadOnly A2 As Double

        Public Sub New(Frequency As Double, GainDb As Double, Q As Double, TypeValue As String, SampleRate As Double)
            Frequency = Clamp(Frequency, 20, Math.Min(20000, SampleRate / 2 - 1))
            Q = Clamp(Q, 0.1, 20)
            Dim W0 = 2 * Math.PI * Frequency / SampleRate
            Dim SinW0 = Math.Sin(W0)
            Dim CosW0 = Math.Cos(W0)
            Dim Alpha = SinW0 / (2 * Q)
            Dim A0 = 1.0

            Select Case NormalizeEqualizerType(TypeValue)
                Case "LowShelf"
                    Dim A = Math.Pow(10, GainDb / 40)
                    Dim SqrtA = Math.Sqrt(A)
                    Dim TwoSqrtAAlpha = 2 * SqrtA * Alpha
                    A0 = (A + 1) + (A - 1) * CosW0 + TwoSqrtAAlpha
                    B0 = (A * ((A + 1) - (A - 1) * CosW0 + TwoSqrtAAlpha)) / A0
                    B1 = (2 * A * ((A - 1) - (A + 1) * CosW0)) / A0
                    B2 = (A * ((A + 1) - (A - 1) * CosW0 - TwoSqrtAAlpha)) / A0
                    A1 = (-2 * ((A - 1) + (A + 1) * CosW0)) / A0
                    A2 = ((A + 1) + (A - 1) * CosW0 - TwoSqrtAAlpha) / A0
                Case "HighShelf"
                    Dim A = Math.Pow(10, GainDb / 40)
                    Dim SqrtA = Math.Sqrt(A)
                    Dim TwoSqrtAAlpha = 2 * SqrtA * Alpha
                    A0 = (A + 1) - (A - 1) * CosW0 + TwoSqrtAAlpha
                    B0 = (A * ((A + 1) + (A - 1) * CosW0 + TwoSqrtAAlpha)) / A0
                    B1 = (-2 * A * ((A - 1) + (A + 1) * CosW0)) / A0
                    B2 = (A * ((A + 1) + (A - 1) * CosW0 - TwoSqrtAAlpha)) / A0
                    A1 = (2 * ((A - 1) - (A + 1) * CosW0)) / A0
                    A2 = ((A + 1) - (A - 1) * CosW0 - TwoSqrtAAlpha) / A0
                Case "LowPass"
                    A0 = 1 + Alpha
                    B0 = ((1 - CosW0) / 2) / A0
                    B1 = (1 - CosW0) / A0
                    B2 = ((1 - CosW0) / 2) / A0
                    A1 = (-2 * CosW0) / A0
                    A2 = (1 - Alpha) / A0
                Case "HighPass"
                    A0 = 1 + Alpha
                    B0 = ((1 + CosW0) / 2) / A0
                    B1 = -(1 + CosW0) / A0
                    B2 = ((1 + CosW0) / 2) / A0
                    A1 = (-2 * CosW0) / A0
                    A2 = (1 - Alpha) / A0
                Case Else
                    Dim A = Math.Pow(10, GainDb / 40)
                    A0 = 1 + Alpha / A
                    B0 = (1 + Alpha * A) / A0
                    B1 = (-2 * CosW0) / A0
                    B2 = (1 - Alpha * A) / A0
                    A1 = (-2 * CosW0) / A0
                    A2 = (1 - Alpha / A) / A0
            End Select
        End Sub

        Public Function GetMagnitudeDb(Frequency As Double, SampleRate As Double) As Double
            Dim W = 2 * Math.PI * Frequency / SampleRate
            Dim CosW = Math.Cos(W)
            Dim SinW = Math.Sin(W)
            Dim Cos2W = Math.Cos(2 * W)
            Dim Sin2W = Math.Sin(2 * W)
            Dim NumReal = B0 + B1 * CosW + B2 * Cos2W
            Dim NumImag = -(B1 * SinW + B2 * Sin2W)
            Dim DenReal = 1 + A1 * CosW + A2 * Cos2W
            Dim DenImag = -(A1 * SinW + A2 * Sin2W)
            Dim NumSq = NumReal * NumReal + NumImag * NumImag
            Dim DenSq = DenReal * DenReal + DenImag * DenImag
            If DenSq < 0.000000000001 Then Return 0
            Dim Ratio = NumSq / DenSq
            If Ratio < 0.000000000001 Then Return -120
            Return 10 * Math.Log10(Ratio)
        End Function
    End Class

    Private Sub SetEqualizerButtonsEnabled(Enabled As Boolean)
        BtnEqualizerRefresh.IsEnabled = Not String.IsNullOrWhiteSpace(CurrentBaseUrl)
        BtnEqualizerToggle.IsEnabled = Enabled
        BtnEqualizerSoftClip.IsEnabled = Enabled
        BtnEqualizerGlobalGain.IsEnabled = Enabled
        BtnEqualizerAddPoint.IsEnabled = Enabled
        BtnEqualizerReset.IsEnabled = Enabled
    End Sub

    Private Async Function SaveEqualizerStateAsync(State As OmniMixEqualizerStateInfo, Message As String) As Task
        If String.IsNullOrWhiteSpace(CurrentBaseUrl) OrElse String.IsNullOrWhiteSpace(CurrentEqualizerInstanceId) Then Return

        Try
            State = NormalizeEqualizerState(State)
            Await OmniMixApiClient.PutInstanceEqualizerAsync(CurrentBaseUrl, CurrentEqualizerInstanceId, State)
            CurrentEqualizerState = CloneEqualizerState(State)
            Await RefreshEqualizerAsync(CurrentBaseUrl)
            If Not String.IsNullOrWhiteSpace(Message) Then LabEqualizerSummary.Text = Message & " " & LabEqualizerSummary.Text
        Catch Ex As Exception
            LabEqualizerSummary.Text = "均衡器保存失败：" & Ex.Message
        End Try
    End Function

    Private Async Sub BtnEqualizerRefresh_Click(sender As Object, e As EventArgs) Handles BtnEqualizerRefresh.Click
        If String.IsNullOrWhiteSpace(CurrentBaseUrl) Then
            Await RefreshBackendStatusAsync()
        Else
            Await RefreshEqualizerAsync(CurrentBaseUrl)
        End If
    End Sub

    Private Async Sub BtnEqualizerToggle_Click(sender As Object, e As EventArgs) Handles BtnEqualizerToggle.Click
        Dim State = CloneEqualizerState(CurrentEqualizerState)
        State.Enabled = Not State.Enabled
        Await SaveEqualizerStateAsync(State, If(State.Enabled, "已启用均衡器。", "已禁用均衡器。"))
    End Sub

    Private Async Sub BtnEqualizerSoftClip_Click(sender As Object, e As EventArgs) Handles BtnEqualizerSoftClip.Click
        Dim State = CloneEqualizerState(CurrentEqualizerState)
        State.SoftClipEnabled = Not State.SoftClipEnabled
        Await SaveEqualizerStateAsync(State, If(State.SoftClipEnabled, "已开启软削波。", "已关闭软削波。"))
    End Sub

    Private Async Sub BtnEqualizerGlobalGain_Click(sender As Object, e As EventArgs) Handles BtnEqualizerGlobalGain.Click
        Dim Gain As Double
        If Not PromptEqualizerNumber("全局增益", "输入均衡器全局增益（dB）。", If(CurrentEqualizerState Is Nothing, 0, CurrentEqualizerState.GlobalGainDb), -24, 24, Gain) Then Return

        Dim State = CloneEqualizerState(CurrentEqualizerState)
        State.GlobalGainDb = Gain
        Await SaveEqualizerStateAsync(State, "已更新全局增益。")
    End Sub

    Private Async Sub BtnEqualizerAddPoint_Click(sender As Object, e As EventArgs) Handles BtnEqualizerAddPoint.Click
        Dim TypeValue = PromptEqualizerType("Peaking")
        If String.IsNullOrWhiteSpace(TypeValue) Then Return

        Dim Frequency As Double
        If Not PromptEqualizerNumber("添加控制点", "输入频率（Hz）。", 1000, 20, 22000, Frequency) Then Return
        Dim Gain As Double
        If Not PromptEqualizerNumber("添加控制点", "输入增益（dB）。", 0, -24, 24, Gain) Then Return
        Dim Q As Double
        If Not PromptEqualizerNumber("添加控制点", "输入 Q 值。", 1, 0.1, 20, Q) Then Return

        Dim State = CloneEqualizerState(CurrentEqualizerState)
        State.Points.Add(New OmniMixEqualizerPointInfo With {
            .Id = "vb_" & Guid.NewGuid().ToString("N"),
            .Frequency = Frequency,
            .GainDb = Gain,
            .Q = Q,
            .Type = TypeValue
        })
        Await SaveEqualizerStateAsync(State, "已添加控制点。")
    End Sub

    Private Async Sub BtnEqualizerReset_Click(sender As Object, e As EventArgs) Handles BtnEqualizerReset.Click
        If MyMsgBox("确定要把当前实例的均衡器重置为平直响应吗？", "重置均衡器", "重置", "取消", IsWarn:=True) <> 1 Then Return

        Await SaveEqualizerStateAsync(New OmniMixEqualizerStateInfo With {
            .Enabled = True,
            .GlobalGainDb = 0,
            .SoftClipEnabled = True,
            .Points = New List(Of OmniMixEqualizerPointInfo)
        }, "已重置为平直响应。")
    End Sub

    Private Async Sub EqualizerPresetApplyButton_Click(sender As Object, e As EventArgs)
        Dim Button = CType(sender, MyIconButton)
        Dim Payload = CType(Button.Tag, Tuple(Of String, OmniMixEqualizerStateInfo))
        Await SaveEqualizerStateAsync(CloneEqualizerState(Payload.Item2), "已应用预设“" & Payload.Item1 & "”。")
    End Sub

    Private Async Sub EqualizerPointEditButton_Click(sender As Object, e As EventArgs)
        Dim Button = CType(sender, MyIconButton)
        Dim Point = TryCast(Button.Tag, OmniMixEqualizerPointInfo)
        If Point Is Nothing Then Return

        Dim TypeValue = PromptEqualizerType(Point.Type)
        If String.IsNullOrWhiteSpace(TypeValue) Then Return
        Dim Frequency As Double
        If Not PromptEqualizerNumber("编辑控制点", "输入频率（Hz）。", Point.Frequency, 20, 22000, Frequency) Then Return
        Dim Gain As Double
        If Not PromptEqualizerNumber("编辑控制点", "输入增益（dB）。", Point.GainDb, -24, 24, Gain) Then Return
        Dim Q As Double
        If Not PromptEqualizerNumber("编辑控制点", "输入 Q 值。", Point.Q, 0.1, 20, Q) Then Return

        Dim State = CloneEqualizerState(CurrentEqualizerState)
        Dim Target = State.Points.FirstOrDefault(Function(Item) String.Equals(Item.Id, Point.Id, StringComparison.OrdinalIgnoreCase))
        If Target Is Nothing Then Return
        Target.Type = TypeValue
        Target.Frequency = Frequency
        Target.GainDb = Gain
        Target.Q = Q
        Await SaveEqualizerStateAsync(State, "已更新控制点。")
    End Sub

    Private Async Sub EqualizerPointDeleteButton_Click(sender As Object, e As EventArgs)
        Dim Button = CType(sender, MyIconButton)
        Dim Point = TryCast(Button.Tag, OmniMixEqualizerPointInfo)
        If Point Is Nothing Then Return
        If MyMsgBox("确定要删除控制点“" & FormatFrequency(Point.Frequency) & "”吗？", "删除控制点", "删除", "取消", IsWarn:=True) <> 1 Then Return

        Dim State = CloneEqualizerState(CurrentEqualizerState)
        State.Points.RemoveAll(Function(Item) String.Equals(Item.Id, Point.Id, StringComparison.OrdinalIgnoreCase))
        Await SaveEqualizerStateAsync(State, "已删除控制点。")
    End Sub

    Private Async Function RefreshPlaybackAsync(BaseUrl As String, Optional EnsureController As Boolean = True) As Task
        If PageKey <> "Home" Then Return

        If EnsureController Then LabPlaybackSummary.Text = "正在注册 GUI 控制端并读取播放实例..."

        Try
            Dim Instances = Await OmniMixApiClient.GetInstancesAsync(BaseUrl)
            Dim Config = Await OmniMixApiClient.GetConfigAsync(BaseUrl)
            Dim ActiveInstance = PickActiveInstance(Instances, ConfigString(Config, "active_instance", ""))
            ActiveInstanceId = If(ActiveInstance Is Nothing, "", ActiveInstance.Id)
            RenderPlayback(Instances, ActiveInstance)
            Await RefreshQueuePaneAsync(BaseUrl)
        Catch Ex As Exception
            ActiveInstanceId = ""
            CanControlActiveInstance = False
            LabPlaybackSummary.Text = "播放实例加载失败：" & Ex.Message
            RenderPlayback(New List(Of OmniMixPlaybackInstanceInfo), Nothing)
            RenderQueueItems(New List(Of OmniMixQueueItemInfo), IsShowingHistory)
        End Try
    End Function

    Private Sub RenderPlayback(Instances As List(Of OmniMixPlaybackInstanceInfo), ActiveInstance As OmniMixPlaybackInstanceInfo)
        Instances = If(Instances, New List(Of OmniMixPlaybackInstanceInfo))
        FrmMain?.UpdateOmniMixInstanceMenu(Instances, If(ActiveInstance Is Nothing, "", ActiveInstance.Id))
        Dim AttachedCount = Instances.Where(Function(Instance) Instance.Attached).Count()
        Dim ServerCount = Instances.Where(Function(Instance) Instance.IsServerManaged).Count()
        LabPlaybackSummary.Text = $"已读取 {Instances.Count} 个播放实例；在线 {AttachedCount} 个，可由后端控制 {ServerCount} 个。"

        IsUpdatingPlaybackUi = True
        Try
            Dim CanControl = CanUseLiveInstanceApi(ActiveInstance)
            CanControlActiveInstance = CanControl
            BtnPlaybackPrev.IsEnabled = CanControl
            BtnPlaybackToggle.IsEnabled = CanControl
            BtnPlaybackNext.IsEnabled = CanControl
            SliderVolume.IsEnabled = ActiveInstance IsNot Nothing

            If ActiveInstance Is Nothing Then
                CanControlActiveInstance = False
                LabPlaybackTrack.Text = "没有曲目正在播放"
                LabPlaybackMeta.Text = "后端已连接，但还没有可控制的音频实例。"
                LabPlaybackInstance.Text = "GUI 控制端已注册；等待音频实例连接。"
                LabPlaybackPosition.Text = "进度：--"
                SliderVolume.Value = 100
                LabPlaybackVolume.Text = "100%"
                LabQueueSummary.Text = ""
                LabQueueSummary.Visibility = Visibility.Collapsed
                Return
            End If

            Dim Track = ActiveInstance.CurrentTrack
            If Track Is Nothing OrElse (String.IsNullOrWhiteSpace(Track.Uuid) AndAlso String.IsNullOrWhiteSpace(Track.Title)) Then
                LabPlaybackTrack.Text = If(ActiveInstance.IsPlaying, "正在播放", "暂无曲目")
                LabPlaybackMeta.Text = "当前实例还没有曲目信息。"
            Else
                LabPlaybackTrack.Text = NonEmpty(Track.Title, Track.Uuid)
                Dim TrackParts As New List(Of String)
                If Not String.IsNullOrWhiteSpace(Track.Artist) Then TrackParts.Add(Track.Artist)
                If Track.Duration > 0 Then TrackParts.Add(FormatDuration(Track.Duration))
                If Not String.IsNullOrWhiteSpace(Track.ModuleId) Then TrackParts.Add("来源 " & Track.ModuleId)
                LabPlaybackMeta.Text = String.Join(" · ", TrackParts)
            End If

            Dim InstanceParts As New List(Of String) From {
                "实例 " & NonEmpty(ActiveInstance.Id, ActiveInstance.ClientId),
                If(ActiveInstance.Attached, "在线", "离线"),
                If(ActiveInstance.IsServerManaged, "后端控制", "客户端控制"),
                If(ActiveInstance.IsPlaying, "播放中", "已暂停")
            }
            If ActiveInstance.QueueCount > 0 Then InstanceParts.Add($"队列 {ActiveInstance.QueueIndex + 1}/{ActiveInstance.QueueCount}")
            If ActiveInstance.HistoryCount > 0 Then InstanceParts.Add($"历史 {ActiveInstance.HistoryCount}")
            If ActiveInstance.TargetLatency > 0 Then InstanceParts.Add($"延迟 {CInt(Math.Round(ActiveInstance.TargetLatency * 1000))} ms")
            LabPlaybackInstance.Text = String.Join(" · ", InstanceParts)

            Dim Duration = If(Track Is Nothing, 0, Track.Duration)
            LabPlaybackPosition.Text = "进度：" & FormatDuration(ActiveInstance.Position) & If(Duration > 0, " / " & FormatDuration(Duration), "")
            Dim VolumeValue = CInt(Math.Max(0, Math.Min(100, Math.Round(ActiveInstance.Volume * 100))))
            SliderVolume.Value = VolumeValue
            LabPlaybackVolume.Text = VolumeValue & "%"
        Finally
            IsUpdatingPlaybackUi = False
        End Try
    End Sub

    Private Async Function RefreshQueuePaneAsync(BaseUrl As String) As Task
        If PageKey <> "Home" Then Return

        If String.IsNullOrWhiteSpace(ActiveInstanceId) Then
            RenderQueueItems(New List(Of OmniMixQueueItemInfo), IsShowingHistory)
            LabQueueSummary.Text = ""
            LabQueueSummary.Visibility = Visibility.Collapsed
            Return
        End If

        If IsShowingHistory Then
            Dim History As List(Of OmniMixQueueItemInfo) = Nothing
            Try
                History = Await OmniMixApiClient.GetInstanceHistoryAsync(BaseUrl, ActiveInstanceId)
            Catch
                History = Nothing
            End Try
            If History Is Nothing Then History = Await LoadOfflineProfileQueueItemsAsync(BaseUrl, ActiveInstanceId, True)
            RenderQueueItems(History, True)
        Else
            Dim Queue As List(Of OmniMixQueueItemInfo) = Nothing
            Try
                Queue = Await OmniMixApiClient.GetInstanceQueueAsync(BaseUrl, ActiveInstanceId)
            Catch
                Queue = Nothing
            End Try
            If Queue Is Nothing Then Queue = Await LoadOfflineProfileQueueItemsAsync(BaseUrl, ActiveInstanceId, False)
            RenderQueueItems(Queue, False)
        End If
    End Function

    Private Sub EnsureQueueToolbarIcons()
        BtnQueueClear.Logo = Logo.IconButtonDelete
        BtnQueueClear.LogoScale = 0.82
        BtnQueueClear.Theme = MyIconButton.Themes.Red
        BtnPlaybackRefresh.Logo = Logo.IconButtonRefresh
        BtnPlaybackRefresh.LogoScale = 0.84
        BtnPlaybackRefresh.Theme = MyIconButton.Themes.Color
    End Sub

    Private Sub UpdateQueueTabSelection(IsHistory As Boolean)
        If IsHistory Then
            PanQueueTab.SetResourceReference(Border.BackgroundProperty, "ColorBrushSemiTransparent")
            PanHistoryTab.SetResourceReference(Border.BackgroundProperty, "ColorBrush7")
        Else
            PanQueueTab.SetResourceReference(Border.BackgroundProperty, "ColorBrush7")
            PanHistoryTab.SetResourceReference(Border.BackgroundProperty, "ColorBrushSemiTransparent")
        End If
    End Sub

    Private Function BuildQueueRenderKey(Items As List(Of OmniMixQueueItemInfo), IsHistory As Boolean, CanEditQueue As Boolean, CanPlayQueueItems As Boolean) As String
        Dim Parts As New List(Of String) From {
            If(IsHistory, "history", "queue"),
            If(CanEditQueue, "edit", "read"),
            If(CanPlayQueueItems, "play", "nop"),
            Items.Count.ToString(CultureInfo.InvariantCulture)
        }

        For Each Item In Items.OrderBy(Function(QueueItem) QueueItem.Index)
            Parts.Add(String.Join("|", {
                Item.Index.ToString(CultureInfo.InvariantCulture),
                If(Item.Uuid, ""),
                If(Item.Title, ""),
                If(Item.Artist, ""),
                Item.Duration.ToString("0.###", CultureInfo.InvariantCulture),
                If(Item.ModuleId, ""),
                ResolveQueueItemCover(Item)
            }))
        Next

        Return String.Join(vbLf, Parts)
    End Function

    Private Sub RenderQueueItems(Items As List(Of OmniMixQueueItemInfo), IsHistory As Boolean)
        Items = If(Items, New List(Of OmniMixQueueItemInfo))
        EnsureQueueToolbarIcons()
        UpdateQueueTabSelection(IsHistory)
        BtnQueueTab.Text = "队列"
        BtnHistoryTab.Text = "历史"
        Dim CanEditQueue = Not String.IsNullOrWhiteSpace(CurrentBaseUrl) AndAlso Not String.IsNullOrWhiteSpace(ActiveInstanceId)
        BtnQueueClear.IsEnabled = CanEditQueue AndAlso Items.Count > 0
        BtnQueueClear.Opacity = If(BtnQueueClear.IsEnabled, 1, 0.35)

        Dim PaneName = If(IsHistory, "历史", "队列")
        LabQueueSummary.Visibility = Visibility.Visible
        LabQueueSummary.Text = If(Items.Count = 0, PaneName & "为空。", $"{PaneName}中有 {Items.Count} 首曲目。")

        Dim RenderKey = BuildQueueRenderKey(Items, IsHistory, CanEditQueue, CanControlActiveInstance)
        If String.Equals(CurrentQueueRenderKey, RenderKey, StringComparison.Ordinal) Then Return
        CurrentQueueRenderKey = RenderKey
        PanQueueList.Children.Clear()

        If Items.Count = 0 Then
            PanQueueList.Children.Add(New MyListItem With {
                .Title = If(IsHistory, "没有历史曲目", "没有待播放曲目"),
                .Info = If(IsHistory, "播放过的曲目会出现在这里。", "从音乐库选择歌曲后会出现在这里。"),
                .Height = 64,
                .PaddingLeft = 8,
                .Margin = New Thickness(0, 0, 0, 2),
                .IsScaleAnimationEnabled = False,
                .Type = MyListItem.CheckType.Clickable
            })
            Return
        End If

        Dim OrderedItems = Items.OrderBy(Function(QueueItem) QueueItem.Index).ToList()
        For ListPosition = 0 To OrderedItems.Count - 1
            Dim CapturedItem = OrderedItems(ListPosition)
            Dim CapturedPosition = ListPosition
            Dim CapturedCount = OrderedItems.Count
            PanQueueList.Children.Add(New MyVirtualizingElement(Of MyListItem)(
                Function() CreateQueueListItem(CapturedItem, CapturedPosition, CapturedCount, IsHistory, PaneName, CanEditQueue, CanControlActiveInstance)
            ) With {.Height = 64})
        Next
    End Sub

    Private Function CreateQueueListItem(Item As OmniMixQueueItemInfo, ListPosition As Integer, TotalCount As Integer, IsHistory As Boolean, PaneName As String, CanEditQueue As Boolean, CanPlayQueueItems As Boolean) As MyListItem
        Dim InfoParts As New List(Of String)
        If Not String.IsNullOrWhiteSpace(Item.Artist) Then InfoParts.Add(Item.Artist)
        If Item.Duration > 0 Then InfoParts.Add(FormatDuration(Item.Duration))
        If Not String.IsNullOrWhiteSpace(Item.ModuleId) Then InfoParts.Add(Item.ModuleId)
        Dim ListItem As New MyListItem With {
            .Title = $"{Item.Index + 1}. {NonEmpty(Item.Title, Item.Uuid)}",
            .Info = String.Join(" · ", InfoParts),
            .Logo = ResolveQueueItemCover(Item),
            .LogoScale = 1,
            .LogoWidth = 58,
            .Height = 64,
            .PaddingLeft = 6,
            .Margin = New Thickness(0, 0, 0, 2),
            .IsScaleAnimationEnabled = False,
            .Type = MyListItem.CheckType.Clickable,
            .Tag = Item
        }

        Dim Buttons As New List(Of MyIconButton)
        Dim CanPlayQueueItem = CanEditQueue AndAlso CanPlayQueueItems AndAlso Not String.IsNullOrWhiteSpace(Item.Uuid)
        Dim PlayButton As New MyIconButton With {
            .Logo = IconPlay,
            .LogoScale = 0.78,
            .ToolTip = "播放",
            .Tag = Item.Uuid,
            .IsEnabled = CanPlayQueueItem,
            .Opacity = If(CanPlayQueueItem, 1, 0.35)
        }
        AddHandler PlayButton.Click, AddressOf QueueItemPlayButton_Click
        Buttons.Add(PlayButton)
        If CanEditQueue Then
            Dim MoveUpButton As New MyIconButton With {
                .Logo = IconMoveUp,
                .LogoScale = 0.82,
                .ToolTip = "上移",
                .Tag = Tuple.Create(Item.Index, IsHistory, -1),
                .IsEnabled = ListPosition > 0
            }
            AddHandler MoveUpButton.Click, AddressOf QueueItemMoveButton_Click
            Buttons.Add(MoveUpButton)

            Dim MoveDownButton As New MyIconButton With {
                .Logo = IconMoveDown,
                .LogoScale = 0.82,
                .ToolTip = "下移",
                .Tag = Tuple.Create(Item.Index, IsHistory, 1),
                .IsEnabled = ListPosition < TotalCount - 1
            }
            AddHandler MoveDownButton.Click, AddressOf QueueItemMoveButton_Click
            Buttons.Add(MoveDownButton)

            Dim RemoveButton As New MyIconButton With {
                .Logo = Logo.IconButtonDelete,
                .LogoScale = 1,
                .Theme = MyIconButton.Themes.Red,
                .ToolTip = "从" & PaneName & "中移除",
                .Tag = Tuple.Create(Item.Index, IsHistory)
            }
            AddHandler RemoveButton.Click, AddressOf QueueItemRemoveButton_Click
            Buttons.Add(RemoveButton)
        End If
        ListItem.Buttons = Buttons
        Return ListItem
    End Function

    Private Async Sub BtnQueueTab_Click(sender As Object, e As EventArgs) Handles BtnQueueTab.Click
        If Not IsShowingHistory Then Return
        IsShowingHistory = False
        Await RefreshQueuePaneOrBackendAsync()
    End Sub

    Private Async Sub BtnHistoryTab_Click(sender As Object, e As EventArgs) Handles BtnHistoryTab.Click
        If IsShowingHistory Then Return
        IsShowingHistory = True
        Await RefreshQueuePaneOrBackendAsync()
    End Sub

    Private Async Sub BtnQueueClear_Click(sender As Object, e As EventArgs) Handles BtnQueueClear.Click
        If String.IsNullOrWhiteSpace(CurrentBaseUrl) OrElse String.IsNullOrWhiteSpace(ActiveInstanceId) Then Return

        Try
            If IsShowingHistory Then
                Await OmniMixApiClient.ClearHistoryAsync(CurrentBaseUrl, ActiveInstanceId)
                LabQueueSummary.Text = "历史已清空。"
            Else
                Await OmniMixApiClient.ClearQueueAsync(CurrentBaseUrl, ActiveInstanceId)
                LabQueueSummary.Text = "队列已清空。"
            End If
            Await RefreshPlaybackAsync(CurrentBaseUrl, False)
        Catch Ex As Exception
            LabQueueSummary.Text = If(IsShowingHistory, "清空历史失败：", "清空队列失败：") & Ex.Message
        End Try
    End Sub

    Private Async Sub QueueItemPlayButton_Click(sender As Object, e As EventArgs)
        Dim Button = CType(sender, MyIconButton)
        Dim Uuid = TryCast(Button.Tag, String)
        If String.IsNullOrWhiteSpace(CurrentBaseUrl) OrElse String.IsNullOrWhiteSpace(ActiveInstanceId) OrElse String.IsNullOrWhiteSpace(Uuid) Then Return

        Try
            Await OmniMixApiClient.PlayAsync(CurrentBaseUrl, ActiveInstanceId, Uuid)
            Await RefreshPlaybackAsync(CurrentBaseUrl, False)
        Catch Ex As Exception
            LabQueueSummary.Visibility = Visibility.Visible
            LabQueueSummary.Text = "播放失败：" & Ex.Message
        End Try
    End Sub

    Private Async Sub QueueItemRemoveButton_Click(sender As Object, e As EventArgs)
        Dim Button = CType(sender, MyIconButton)
        Dim Payload = CType(Button.Tag, Tuple(Of Integer, Boolean))
        If String.IsNullOrWhiteSpace(CurrentBaseUrl) OrElse String.IsNullOrWhiteSpace(ActiveInstanceId) Then Return

        Try
            If Payload.Item2 Then
                Await OmniMixApiClient.RemoveHistoryItemAsync(CurrentBaseUrl, ActiveInstanceId, Payload.Item1)
                LabQueueSummary.Text = "已从历史中移除曲目。"
            Else
                Await OmniMixApiClient.RemoveQueueItemAsync(CurrentBaseUrl, ActiveInstanceId, Payload.Item1)
                LabQueueSummary.Text = "已从队列中移除曲目。"
            End If
            Await RefreshPlaybackAsync(CurrentBaseUrl)
        Catch Ex As Exception
            LabQueueSummary.Text = If(Payload.Item2, "移除历史曲目失败：", "移除队列曲目失败：") & Ex.Message
        End Try
    End Sub

    Private Async Sub QueueItemMoveButton_Click(sender As Object, e As EventArgs)
        Dim Button = CType(sender, MyIconButton)
        Dim Payload = CType(Button.Tag, Tuple(Of Integer, Boolean, Integer))
        If String.IsNullOrWhiteSpace(CurrentBaseUrl) OrElse String.IsNullOrWhiteSpace(ActiveInstanceId) Then Return

        Dim FromIndex = Payload.Item1
        Dim ToIndex = Math.Max(0, FromIndex + Payload.Item3)
        Try
            If Payload.Item2 Then
                Await OmniMixApiClient.MoveHistoryItemAsync(CurrentBaseUrl, ActiveInstanceId, FromIndex, ToIndex)
                LabQueueSummary.Text = "已调整历史曲目顺序。"
            Else
                Await OmniMixApiClient.MoveQueueItemAsync(CurrentBaseUrl, ActiveInstanceId, FromIndex, ToIndex)
                LabQueueSummary.Text = "已调整队列曲目顺序。"
            End If
            Await RefreshPlaybackAsync(CurrentBaseUrl)
        Catch Ex As Exception
            LabQueueSummary.Text = If(Payload.Item2, "调整历史顺序失败：", "调整队列顺序失败：") & Ex.Message
        End Try
    End Sub

    Private Sub UpdatePlaybackAutoRefreshTimer()
        If PageKey = "Home" AndAlso IsLoaded AndAlso Not String.IsNullOrWhiteSpace(CurrentBaseUrl) Then
            If Not PlaybackAutoRefreshTimer.IsEnabled Then PlaybackAutoRefreshTimer.Start()
        Else
            If PlaybackAutoRefreshTimer.IsEnabled Then PlaybackAutoRefreshTimer.Stop()
        End If
    End Sub

    Private Async Sub PlaybackAutoRefreshTimer_Tick(sender As Object, e As EventArgs)
        If PageKey <> "Home" OrElse IsAutoRefreshingPlayback OrElse String.IsNullOrWhiteSpace(CurrentBaseUrl) Then Return

        IsAutoRefreshingPlayback = True
        Try
            Await RefreshPlaybackAsync(CurrentBaseUrl, False)
        Catch
            ' Keep timer refresh quiet.
        Finally
            IsAutoRefreshingPlayback = False
        End Try
    End Sub

    Private Async Function RefreshQueuePaneOrBackendAsync() As Task
        If String.IsNullOrWhiteSpace(CurrentBaseUrl) Then
            Await RefreshBackendStatusAsync()
        Else
            Await RefreshQueuePaneAsync(CurrentBaseUrl)
        End If
    End Function

    Private Shared Function PickActiveInstance(Instances As List(Of OmniMixPlaybackInstanceInfo), Optional PreferredId As String = "") As OmniMixPlaybackInstanceInfo
        If Instances Is Nothing OrElse Instances.Count = 0 Then Return Nothing
        If Not String.IsNullOrWhiteSpace(PreferredId) Then
            Dim Preferred = Instances.FirstOrDefault(Function(Instance) String.Equals(Instance.Id, PreferredId, StringComparison.OrdinalIgnoreCase))
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

    Private Async Function SendPlaybackCommandAsync(Command As String) As Task
        If String.IsNullOrWhiteSpace(CurrentBaseUrl) OrElse String.IsNullOrWhiteSpace(ActiveInstanceId) Then Return
        Try
            Await OmniMixApiClient.SendInstanceCommandAsync(CurrentBaseUrl, ActiveInstanceId, Command)
            Await RefreshPlaybackAsync(CurrentBaseUrl)
        Catch Ex As Exception
            LabPlaybackSummary.Text = "播放控制失败：" & Ex.Message
        End Try
    End Function

    Private Async Sub BtnPlaybackRefresh_Click(sender As Object, e As EventArgs) Handles BtnPlaybackRefresh.Click
        If String.IsNullOrWhiteSpace(CurrentBaseUrl) Then
            Await RefreshBackendStatusAsync()
        Else
            Await RefreshPlaybackAsync(CurrentBaseUrl)
        End If
    End Sub

    Private Async Sub BtnPlaybackPrev_Click(sender As Object, e As EventArgs) Handles BtnPlaybackPrev.Click
        Await SendPlaybackCommandAsync("prev")
    End Sub

    Private Async Sub BtnPlaybackToggle_Click(sender As Object, e As EventArgs) Handles BtnPlaybackToggle.Click
        Await SendPlaybackCommandAsync("toggle")
    End Sub

    Private Async Sub BtnPlaybackNext_Click(sender As Object, e As EventArgs) Handles BtnPlaybackNext.Click
        Await SendPlaybackCommandAsync("next")
    End Sub

    Private Async Sub SliderVolume_Change(sender As Object, user As Boolean) Handles SliderVolume.Change
        If IsUpdatingPlaybackUi Then Return
        If String.IsNullOrWhiteSpace(CurrentBaseUrl) OrElse String.IsNullOrWhiteSpace(ActiveInstanceId) Then Return

        Dim Volume = SliderVolume.Value / 100.0
        LabPlaybackVolume.Text = SliderVolume.Value & "%"
        Try
            Await OmniMixApiClient.SetInstanceVolumeAsync(CurrentBaseUrl, ActiveInstanceId, Volume)
            LabPlaybackSummary.Text = "已更新音量为 " & SliderVolume.Value & "%。"
        Catch Ex As Exception
            LabPlaybackSummary.Text = "音量保存失败：" & Ex.Message
        End Try
    End Sub

    Private Async Function RefreshModulesAsync(BaseUrl As String) As Task
        If PageKey <> "Modules" Then Return

        UpdatePluginPaneVisibility()
        LabModulesSummary.Text = If(CurrentModulesPane = "game", "正在加载游戏集成状态...", If(CurrentModulesPane = "launchpad", "正在加载启动台...", "正在加载 Mod 列表..."))
        PanModulesList.Children.Clear()
        PanLaunchpadList.Children.Clear()
        PanGameIntegrationList.Children.Clear()

        Try
            If CurrentModulesPane = "game" Then
                Dim Stats = Await OmniMixApiClient.GetInstanceStatsAsync(BaseUrl)
                Dim Instances = Await OmniMixApiClient.GetInstancesAsync(BaseUrl)
                RenderGameIntegration(Stats, Instances)
            Else
                Dim Modules = Await OmniMixApiClient.GetModulesAsync(BaseUrl)
                CurrentModules = If(Modules, New List(Of OmniMixModuleInfo))
                If CurrentModulesPane = "launchpad" Then
                    RenderLaunchpad(CurrentModules)
                Else
                    RenderModules(CurrentModules)
                End If
            End If
        Catch Ex As Exception
            LabModulesSummary.Text = If(CurrentModulesPane = "game", "游戏集成状态加载失败：", If(CurrentModulesPane = "launchpad", "启动台加载失败：", "Mod 列表加载失败：")) & Ex.Message
        End Try
    End Function

    Private Sub UpdatePluginPaneVisibility()
        If PageKey = "Modules" AndAlso CardModuleUi.Visibility <> Visibility.Visible Then
            CardModules.Visibility = Visibility.Visible
        End If
        BtnModulesTab.Text = If(CurrentModulesPane = "launchpad", "启动台 ✓", If(CurrentModulesPane = "mod", "Mod ✓", "Mod"))
        BtnGameIntegrationTab.Text = If(CurrentModulesPane = "game", "游戏集成 ✓", "游戏集成")
        PanModulesList.Visibility = If(CurrentModulesPane = "mod", Visibility.Visible, Visibility.Collapsed)
        PanLaunchpadList.Visibility = If(CurrentModulesPane = "launchpad", Visibility.Visible, Visibility.Collapsed)
        PanGameIntegrationList.Visibility = If(CurrentModulesPane = "game", Visibility.Visible, Visibility.Collapsed)
        CardModules.Title = If(CurrentModulesPane = "game", "插件 - 游戏集成", If(CurrentModulesPane = "launchpad", "插件 - 启动台", "插件 - Mod"))
    End Sub

    Private Sub BtnModulesTab_Click(sender As Object, e As EventArgs) Handles BtnModulesTab.Click
        SetModulesPane(If(CurrentModulesPane = "launchpad", "mod", "launchpad"))
    End Sub

    Private Sub BtnGameIntegrationTab_Click(sender As Object, e As EventArgs) Handles BtnGameIntegrationTab.Click
        SetModulesPane("game")
    End Sub

    Private Sub AddOmniMixSectionHeader(Panel As Panel, Title As String, Info As String, Optional TopMargin As Double = 10)
        If Panel Is Nothing Then Return
        Panel.Children.Add(New MyListItem With {
            .Title = Title,
            .Info = Info,
            .Height = 40,
            .PaddingLeft = 8,
            .Margin = New Thickness(0, TopMargin, 0, 4),
            .IsScaleAnimationEnabled = False,
            .Type = MyListItem.CheckType.Clickable
        })
    End Sub

    Private Sub ScrollOmniMixPageToTop()
        Try
            PanBack.Dispatcher.BeginInvoke(Sub() PanBack.ScrollToHome(), System.Windows.Threading.DispatcherPriority.Background)
        Catch
        End Try
    End Sub

    Private Sub RenderGameIntegration(Stats As OmniMixInstanceStatsInfo, Instances As List(Of OmniMixPlaybackInstanceInfo))
        Stats = If(Stats, New OmniMixInstanceStatsInfo)
        Instances = If(Instances, New List(Of OmniMixPlaybackInstanceInfo))
        PanGameIntegrationList.Children.Clear()

        Dim AttachedCount = Instances.Where(Function(Instance) Instance.Attached).Count()
        Dim Games = OmniMixModDeploymentService.GetGameCatalog()
        Dim UpdateCount = Games.Where(Function(Game)
                                          Dim ModInfo = OmniMixModDeploymentService.GetPrimaryMod(Game)
                                          Dim GamePath = OmniMixModDeploymentService.LoadGamePath(Game.Id)
                                          Dim ModStatus = OmniMixModDeploymentService.CheckModStatus(GamePath, ModInfo)
                                          Dim FrameworkStatus = If(Game.SupportedFrameworks.Contains("bepinex_5"), OmniMixModDeploymentService.CheckBepInExStatus(GamePath), OmniMixBepInExStatus.NotInstalled)
                                          Return ModStatus = OmniMixModInstallStatus.NeedsUpdate OrElse FrameworkStatus = OmniMixBepInExStatus.NeedsUpdate
                                      End Function).Count()
        LabModulesSummary.Text = $"游戏集成：{Games.Count} 个支持游戏，{AttachedCount} 个在线" & If(UpdateCount > 0, $"，{UpdateCount} 个可更新。", "。")
        FrmMain?.UpdateOmniMixInstanceMenu(Instances, ActiveInstanceId)

        AddOmniMixSectionHeader(PanGameIntegrationList, "支持的游戏", "点击游戏查看目录、版本和集成操作。", 0)
        For Each Game In Games
            AddGameIntegrationGameItem(Game, Instances)
        Next

        If DeploymentLogs.Count > 0 Then
            AddOmniMixSectionHeader(PanGameIntegrationList, "最近部署", "游戏集成安装器最近执行的操作。", 12)
            PanGameIntegrationList.Children.Add(New MyListItem With {
                .Title = "最近部署日志",
                .Info = String.Join("  /  ", DeploymentLogs.TakeLast(Math.Min(DeploymentLogs.Count, 4))),
                .Height = 58,
                .PaddingLeft = 8,
                .Margin = New Thickness(0, 0, 0, 8),
                .IsScaleAnimationEnabled = False,
                .Type = MyListItem.CheckType.Clickable
            })
        End If

    End Sub

    Private Sub AddGameIntegrationGameItem(Game As OmniMixGameDeclaration, Instances As List(Of OmniMixPlaybackInstanceInfo))
        Dim GamePath = OmniMixModDeploymentService.LoadGamePath(Game.Id)
        Dim ModInfo = OmniMixModDeploymentService.GetPrimaryMod(Game)
        Dim BepInExStatus = If(Game.SupportedFrameworks.Contains("bepinex_5"), OmniMixModDeploymentService.CheckBepInExStatus(GamePath), OmniMixBepInExStatus.NotInstalled)
        Dim ModStatus = OmniMixModDeploymentService.CheckModStatus(GamePath, ModInfo)
        Instances = If(Instances, New List(Of OmniMixPlaybackInstanceInfo))
        Dim IsOnline = Instances.Any(Function(Instance)
                                         If Not Instance.Attached Then Return False
                                         If ModInfo IsNot Nothing AndAlso String.Equals(Instance.Id, OmniMixModDeploymentService.GetExpectedInstanceId(GamePath, ModInfo), StringComparison.OrdinalIgnoreCase) Then Return True
                                         If ModInfo IsNot Nothing AndAlso String.Equals(Instance.ModId, ModInfo.Id, StringComparison.OrdinalIgnoreCase) Then Return True
                                         Return String.Equals(Instance.GameName, Game.Name, StringComparison.OrdinalIgnoreCase) OrElse
                                             (Not String.IsNullOrWhiteSpace(Instance.GameName) AndAlso Instance.GameName.IndexOf(Game.Name, StringComparison.OrdinalIgnoreCase) >= 0)
                                     End Function)
        Dim HasUpdate = ModStatus = OmniMixModInstallStatus.NeedsUpdate OrElse BepInExStatus = OmniMixBepInExStatus.NeedsUpdate
        Dim StatusText = If(IsOnline, "🟢 在线", "🔴 离线") & If(HasUpdate, " · 有可用更新", "")

        Dim Item As New MyListItem With {
            .Title = Game.Name,
            .Info = StatusText,
            .Height = 54,
            .PaddingLeft = 8,
            .Margin = New Thickness(0, 0, 0, 6),
            .Logo = Logo.IconButtonSetup,
            .LogoScale = 0.86,
            .IsScaleAnimationEnabled = False,
            .Type = MyListItem.CheckType.Clickable,
            .Tag = Game
        }
        AddHandler Item.Click, AddressOf GameIntegrationItem_Click
        PanGameIntegrationList.Children.Add(Item)
    End Sub

    Private Sub GameIntegrationItem_Click(sender As Object, e As MouseButtonEventArgs)
        Dim Game = TryCast(CType(sender, FrameworkElement).Tag, OmniMixGameDeclaration)
        If Game Is Nothing Then Return
        OpenGameIntegrationDetail(Game)
    End Sub

    Private Sub OpenGameIntegrationDetail(Game As OmniMixGameDeclaration)
        If Game Is Nothing Then Return

        PreviousModulesPaneForDetail = CurrentModulesPane
        ModuleUiRequestSerial += 1
        CurrentDetailKind = "game"
        CurrentGameDetailId = Game.Id
        CurrentModuleId = ""
        CurrentUiKind = "game"
        CurrentLinkId = Game.Id
        ExpandedModuleTitle = Game.Name

        LabModuleUiTitle.Text = "游戏集成 - " & Game.Name
        CardModules.Visibility = Visibility.Collapsed
        CardModuleUi.Visibility = Visibility.Visible
        PanModuleUi.Children.Clear()
        RenderGameIntegrationDetail(Game)
        ScrollOmniMixPageToTop()
    End Sub

    Private Sub RenderGameIntegrationDetail(Game As OmniMixGameDeclaration)
        PanModuleUi.Children.Clear()

        Dim GamePath = OmniMixModDeploymentService.LoadGamePath(Game.Id)
        Dim IsValidPath = OmniMixModDeploymentService.VerifyGameDirectory(GamePath, Game)
        Dim ModInfo = OmniMixModDeploymentService.GetPrimaryMod(Game)
        Dim BepInExStatus = If(Game.SupportedFrameworks.Contains("bepinex_5"), OmniMixModDeploymentService.CheckBepInExStatus(GamePath), OmniMixBepInExStatus.NotInstalled)
        Dim ModStatus = OmniMixModDeploymentService.CheckModStatus(GamePath, ModInfo)

        Dim InfoParts As New List(Of String)
        InfoParts.Add(If(IsValidPath, "目录有效", If(String.IsNullOrWhiteSpace(GamePath), "未选择目录", "目录无效")))
        If Not String.IsNullOrWhiteSpace(GamePath) Then InfoParts.Add(GamePath)
        If IsValidPath Then InfoParts.Add("游戏版本 " & OmniMixModDeploymentService.GetGameVersion(GamePath, Game))
        If Game.SupportedFrameworks.Contains("bepinex_5") Then InfoParts.Add("BepInEx " & GetBepInExStatusText(BepInExStatus))
        If ModInfo IsNot Nothing Then InfoParts.Add(ModInfo.Name & " " & GetModStatusText(ModStatus))

        LabModuleUiSummary.Text = String.Join(" · ", InfoParts)
        PanModuleUi.Children.Add(New MyListItem With {
            .Title = Game.Name,
            .Info = String.Join(" · ", InfoParts),
            .Height = 58,
            .PaddingLeft = 8,
            .Margin = New Thickness(0, 0, 0, 10),
            .Logo = Logo.IconButtonSetup,
            .LogoScale = 0.86,
            .IsScaleAnimationEnabled = False,
            .Type = MyListItem.CheckType.Clickable
        })

        AddGameDetailAction(PanModuleUi, "选择游戏目录", "指定游戏根目录，用于安装和写入端口文件。", Logo.IconButtonOpen, "选择游戏目录", Game.Id, AddressOf GamePathSelectButton_Click)
        If Not String.IsNullOrWhiteSpace(GamePath) AndAlso Directory.Exists(GamePath) Then
            AddGameDetailAction(PanModuleUi, "打开游戏目录", GamePath, Logo.IconButtonList, "打开游戏目录", GamePath, AddressOf GamePathOpenButton_Click)
        End If

        If Game.SupportedFrameworks.Contains("bepinex_5") Then
            If BepInExStatus = OmniMixBepInExStatus.Managed Then
                AddGameDetailAction(PanModuleUi, "卸载 BepInEx", "移除 OmniMix 管理的 BepInEx 文件。", Logo.IconButtonDelete, "卸载 BepInEx", Game.Id, AddressOf BepInExUninstallButton_Click, True, MyIconButton.Themes.Red)
            Else
                Dim CanInstallBepInEx = IsValidPath AndAlso OmniMixModDeploymentService.IsPackageAvailable("BepInEx_win_x64_5.4.23.5.zip")
                Dim BepInExAction = If(BepInExStatus = OmniMixBepInExStatus.NeedsUpdate, "更新 BepInEx", "安装 BepInEx")
                AddGameDetailAction(PanModuleUi, BepInExAction, If(CanInstallBepInEx, BepInExAction & " 到当前随附版本。", "需要有效游戏目录和 BepInEx 压缩包。"), Logo.IconButtonSave, BepInExAction, Game.Id, AddressOf BepInExInstallButton_Click, CanInstallBepInEx)
            End If
        End If

        If ModInfo IsNot Nothing Then
            If ModStatus = OmniMixModInstallStatus.Installed Then
                AddGameDetailAction(PanModuleUi, "卸载 " & ModInfo.Name, "移除当前游戏集成 Mod。", Logo.IconButtonDelete, "卸载 " & ModInfo.Name, Game.Id, AddressOf ModUninstallButton_Click, True, MyIconButton.Themes.Red)
            Else
                Dim CanInstallMod = IsValidPath AndAlso OmniMixModDeploymentService.IsPackageAvailable(ModInfo.ArchiveName) AndAlso
                    (Not ModInfo.UsesFramework OrElse BepInExStatus <> OmniMixBepInExStatus.NotInstalled)
                Dim ModAction = If(ModStatus = OmniMixModInstallStatus.NeedsUpdate, "更新 " & ModInfo.Name, "安装 " & ModInfo.Name)
                AddGameDetailAction(PanModuleUi, ModAction, If(CanInstallMod, "写入当前随附版本并注册后端实例配置。", "需要有效目录、Mod 包以及必要框架。"), Logo.IconButtonSetup, ModAction, Game.Id, AddressOf ModInstallButton_Click, CanInstallMod)
            End If
        End If

        If DeploymentLogs.Count > 0 Then
            PanModuleUi.Children.Add(New MyListItem With {
                .Title = "最近操作",
                .Info = String.Join("  /  ", DeploymentLogs.TakeLast(Math.Min(DeploymentLogs.Count, 4))),
                .Height = 54,
                .PaddingLeft = 8,
                .Margin = New Thickness(0, 8, 0, 0),
                .IsScaleAnimationEnabled = False,
                .Type = MyListItem.CheckType.Clickable
            })
        End If
    End Sub

    Private Sub AddGameDetailAction(Panel As Panel, Title As String, Info As String, LogoPath As String, Tooltip As String, Tag As Object, Handler As MyIconButton.ClickEventHandler, Optional Enabled As Boolean = True, Optional Theme As MyIconButton.Themes = MyIconButton.Themes.Color)
        Dim ActionButton As New MyIconButton With {
            .Logo = LogoPath,
            .LogoScale = 0.95,
            .ToolTip = Tooltip,
            .Tag = Tag,
            .IsEnabled = Enabled,
            .Theme = Theme
        }
        AddHandler ActionButton.Click, Handler

        Dim Item As New MyListItem With {
            .Title = Title,
            .Info = Info,
            .Height = 52,
            .PaddingLeft = 8,
            .Margin = New Thickness(0, 0, 0, 5),
            .IsScaleAnimationEnabled = False,
            .Type = MyListItem.CheckType.Clickable
        }
        Item.Buttons = New List(Of MyIconButton) From {ActionButton}
        Panel.Children.Add(Item)
    End Sub

    Private Async Function RefreshGameIntegrationAfterOperationAsync(Game As OmniMixGameDeclaration) As Task
        If Game IsNot Nothing AndAlso CurrentDetailKind = "game" AndAlso String.Equals(CurrentGameDetailId, Game.Id, StringComparison.OrdinalIgnoreCase) Then
            OpenGameIntegrationDetail(Game)
            Return
        End If

        If String.IsNullOrWhiteSpace(CurrentBaseUrl) Then
            RenderGameIntegration(New OmniMixInstanceStatsInfo, New List(Of OmniMixPlaybackInstanceInfo))
        Else
            Await RefreshModulesAsync(CurrentBaseUrl)
        End If
    End Function

    Private Async Sub GamePathSelectButton_Click(sender As Object, e As EventArgs)
        Dim Game = OmniMixModDeploymentService.GetGame(TryCast(CType(sender, FrameworkElement).Tag, String))
        If Game Is Nothing Then Return

        Using Dialog As New System.Windows.Forms.FolderBrowserDialog()
            Dialog.Description = "选择 " & Game.Name & " 的游戏根目录"
            Dialog.ShowNewFolderButton = False
            Dim OldPath = OmniMixModDeploymentService.LoadGamePath(Game.Id)
            If Directory.Exists(OldPath) Then Dialog.SelectedPath = OldPath
            If Dialog.ShowDialog() <> System.Windows.Forms.DialogResult.OK Then Return
            OmniMixModDeploymentService.SaveGamePath(Game.Id, Dialog.SelectedPath)
            If OmniMixModDeploymentService.VerifyGameDirectory(Dialog.SelectedPath, Game) Then
                If Not String.IsNullOrWhiteSpace(CurrentBaseUrl) Then
                    Await OmniMixApiClient.AddPortFileDirAsync(CurrentBaseUrl, Dialog.SelectedPath)
                End If
                Hint("已保存游戏目录：" & Game.Name, HintType.Green)
            Else
                Hint("目录已保存，但未通过游戏签名校验。", HintType.Red)
            End If
        End Using

        If String.IsNullOrWhiteSpace(CurrentBaseUrl) Then
            RenderGameIntegration(New OmniMixInstanceStatsInfo, New List(Of OmniMixPlaybackInstanceInfo))
        Else
            Await RefreshModulesAsync(CurrentBaseUrl)
        End If
    End Sub

    Private Sub GamePathOpenButton_Click(sender As Object, e As EventArgs)
        Dim GamePath = TryCast(CType(sender, FrameworkElement).Tag, String)
        If Not String.IsNullOrWhiteSpace(GamePath) Then OpenExplorer(GamePath)
    End Sub

    Private Async Sub BepInExInstallButton_Click(sender As Object, e As EventArgs)
        Dim Game = OmniMixModDeploymentService.GetGame(TryCast(CType(sender, FrameworkElement).Tag, String))
        If Game Is Nothing Then Return
        Dim GamePath = OmniMixModDeploymentService.LoadGamePath(Game.Id)
        If Not OmniMixModDeploymentService.VerifyGameDirectory(GamePath, Game) Then
            LabModulesSummary.Text = "游戏目录无效，请先选择正确的游戏根目录。"
            Return
        End If
        If OmniMixModDeploymentService.CheckBepInExStatus(GamePath) = OmniMixBepInExStatus.Unmanaged Then
            If MyMsgBox("检测到该目录已有非 OmniMix 管理的 BepInEx。继续安装可能会覆盖现有加载器文件。", "安装 BepInEx", "继续", "取消", IsWarn:=True) <> 1 Then Return
        End If

        LabModulesSummary.Text = "正在安装 BepInEx..."
        Dim Logs As New List(Of String)
        Dim Success = Await Task.Run(Function() OmniMixModDeploymentService.DeployBepInEx(GamePath, Logs))
        DeploymentLogs = Logs
        Hint(If(Success, "BepInEx 安装完成。", "BepInEx 安装失败。"), If(Success, HintType.Green, HintType.Red))
        Await RefreshGameIntegrationAfterOperationAsync(Game)
    End Sub

    Private Async Sub BepInExUninstallButton_Click(sender As Object, e As EventArgs)
        Dim Game = OmniMixModDeploymentService.GetGame(TryCast(CType(sender, FrameworkElement).Tag, String))
        If Game Is Nothing Then Return
        Dim GamePath = OmniMixModDeploymentService.LoadGamePath(Game.Id)
        If MyMsgBox("确定要卸载 OmniMix 管理的 BepInEx 吗？非 OmniMix 管理的文件不会被保留在这次删除范围内。", "卸载 BepInEx", "卸载", "取消", IsWarn:=True) <> 1 Then Return

        LabModulesSummary.Text = "正在卸载 BepInEx..."
        Dim Logs As New List(Of String)
        Dim Success = Await Task.Run(Function() OmniMixModDeploymentService.UndeployBepInEx(GamePath, Logs))
        DeploymentLogs = Logs
        Hint(If(Success, "BepInEx 卸载完成。", "BepInEx 卸载失败。"), If(Success, HintType.Green, HintType.Red))
        Await RefreshGameIntegrationAfterOperationAsync(Game)
    End Sub

    Private Async Sub ModInstallButton_Click(sender As Object, e As EventArgs)
        Dim Game = OmniMixModDeploymentService.GetGame(TryCast(CType(sender, FrameworkElement).Tag, String))
        If Game Is Nothing Then Return
        Dim ModInfo = OmniMixModDeploymentService.GetPrimaryMod(Game)
        If ModInfo Is Nothing Then Return
        Dim GamePath = OmniMixModDeploymentService.LoadGamePath(Game.Id)
        If Not OmniMixModDeploymentService.VerifyGameDirectory(GamePath, Game) Then
            LabModulesSummary.Text = "游戏目录无效，请先选择正确的游戏根目录。"
            Return
        End If
        If ModInfo.UsesFramework AndAlso OmniMixModDeploymentService.CheckBepInExStatus(GamePath) = OmniMixBepInExStatus.NotInstalled Then
            LabModulesSummary.Text = "请先安装 BepInEx，再安装 " & ModInfo.Name & "。"
            Return
        End If
        If Not OmniMixModDeploymentService.IsPackageAvailable(ModInfo.ArchiveName) Then
            LabModulesSummary.Text = "缺少 Mod 包：" & ModInfo.ArchiveName
            Return
        End If

        LabModulesSummary.Text = "正在安装 " & ModInfo.Name & "..."
        Dim Logs As New List(Of String)
        Dim BackendPort = GetPortFromBaseUrl(CurrentBaseUrl, 17890)
        Dim InstanceId = Await Task.Run(Function() OmniMixModDeploymentService.DeployMod(GamePath, ModInfo, BackendPort, Logs))
        DeploymentLogs = Logs
        If String.IsNullOrWhiteSpace(InstanceId) Then
            Hint(ModInfo.Name & " 安装失败。", HintType.Red)
        Else
            Await RegisterDeployedInstanceAsync(InstanceId, ModInfo, GamePath)
            Hint(ModInfo.Name & " 安装完成。", HintType.Green)
        End If
        Await RefreshGameIntegrationAfterOperationAsync(Game)
    End Sub

    Private Async Sub ModUninstallButton_Click(sender As Object, e As EventArgs)
        Dim Game = OmniMixModDeploymentService.GetGame(TryCast(CType(sender, FrameworkElement).Tag, String))
        If Game Is Nothing Then Return
        Dim ModInfo = OmniMixModDeploymentService.GetPrimaryMod(Game)
        If ModInfo Is Nothing Then Return
        Dim GamePath = OmniMixModDeploymentService.LoadGamePath(Game.Id)
        If MyMsgBox("确定要卸载 " & ModInfo.Name & " 吗？", "卸载 Mod", "卸载", "取消", IsWarn:=True) <> 1 Then Return

        LabModulesSummary.Text = "正在卸载 " & ModInfo.Name & "..."
        Dim Logs As New List(Of String)
        Dim Success = Await Task.Run(Function() OmniMixModDeploymentService.UndeployMod(GamePath, ModInfo, Logs))
        DeploymentLogs = Logs
        Hint(If(Success, ModInfo.Name & " 卸载完成。", ModInfo.Name & " 卸载失败。"), If(Success, HintType.Green, HintType.Red))
        Await RefreshGameIntegrationAfterOperationAsync(Game)
    End Sub

    Private Async Function RegisterDeployedInstanceAsync(InstanceId As String, ModInfo As OmniMixModDeclaration, GamePath As String) As Task
        If String.IsNullOrWhiteSpace(CurrentBaseUrl) OrElse String.IsNullOrWhiteSpace(InstanceId) OrElse ModInfo Is Nothing Then Return
        Try
            If Not String.IsNullOrWhiteSpace(GamePath) Then
                Await OmniMixApiClient.AddPortFileDirAsync(CurrentBaseUrl, GamePath)
            End If
            Await OmniMixApiClient.SetInstanceMetaAsync(CurrentBaseUrl, InstanceId, ModInfo.Id, ModInfo.Name, ModInfo.Mode)
            Await OmniMixApiClient.UpdateInstanceProfileAsync(CurrentBaseUrl, InstanceId, BuildDefaultInstanceProfile())
        Catch Ex As Exception
            DeploymentLogs.Add("[" & Date.Now.ToString("HH:mm:ss") & "] Warning: failed to register backend profile: " & Ex.Message)
        End Try
    End Function

    Private Async Function SyncGameIntegrationBindingsAsync(BaseUrl As String) As Task
        If String.IsNullOrWhiteSpace(BaseUrl) Then Return

        Dim BackendPort = GetPortFromBaseUrl(BaseUrl, 17890)
        Dim ExpectedByMod As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Dim Logs As New List(Of String)

        For Each Game In OmniMixModDeploymentService.GetGameCatalog()
            Dim ModInfo = OmniMixModDeploymentService.GetPrimaryMod(Game)
            If ModInfo Is Nothing Then Continue For

            Dim GamePath = OmniMixModDeploymentService.LoadGamePath(Game.Id)
            If Not OmniMixModDeploymentService.VerifyGameDirectory(GamePath, Game) Then Continue For
            If OmniMixModDeploymentService.CheckModStatus(GamePath, ModInfo) = OmniMixModInstallStatus.NotInstalled Then Continue For

            Dim ExpectedId = OmniMixModDeploymentService.EnsureRuntimeBinding(GamePath, ModInfo, BackendPort, Logs)
            If String.IsNullOrWhiteSpace(ExpectedId) Then Continue For
            ExpectedByMod(ModInfo.Id) = ExpectedId

            Try
                Await OmniMixApiClient.AddPortFileDirAsync(BaseUrl, GamePath)
                Await OmniMixApiClient.SetInstanceMetaAsync(BaseUrl, ExpectedId, ModInfo.Id, ModInfo.Name, ModInfo.Mode)
            Catch
            End Try
        Next

        If ExpectedByMod.Count = 0 Then Return

        Try
            Dim Instances = Await OmniMixApiClient.GetInstancesAsync(BaseUrl)
            Dim RemovedInvalid As Integer = 0
            For Each Instance In If(Instances, New List(Of OmniMixPlaybackInstanceInfo))
                If Instance Is Nothing OrElse String.IsNullOrWhiteSpace(Instance.Id) Then Continue For
                For Each Pair In ExpectedByMod
                    If IsStaleBoundInstanceId(Instance.Id, Pair.Value) Then
                        Try
                            Await OmniMixApiClient.DeleteInstanceAsync(BaseUrl, Instance.Id)
                            RemovedInvalid += 1
                        Catch
                        End Try
                        Exit For
                    End If
                Next
            Next

            Dim Config = Await OmniMixApiClient.GetConfigAsync(BaseUrl)
            Dim ActiveId = ConfigString(Config, "active_instance", "")
            If Not String.IsNullOrWhiteSpace(ActiveId) AndAlso ExpectedByMod.Values.Any(Function(ExpectedId) IsStaleBoundInstanceId(ActiveId, ExpectedId)) Then
                Await OmniMixApiClient.SetActiveInstanceAsync(BaseUrl, "")
            End If

            If RemovedInvalid > 0 Then
                Logs.Add("  Removed stale backend instances: " & RemovedInvalid)
            End If
        Catch
        End Try

        If Logs.Count > 0 Then DeploymentLogs = Logs
    End Function

    Private Shared Function IsStaleBoundInstanceId(InstanceId As String, ExpectedId As String) As Boolean
        If String.IsNullOrWhiteSpace(InstanceId) OrElse String.IsNullOrWhiteSpace(ExpectedId) Then Return False
        If String.Equals(InstanceId, ExpectedId, StringComparison.OrdinalIgnoreCase) Then Return False
        Return InstanceId.EndsWith(ExpectedId, StringComparison.OrdinalIgnoreCase)
    End Function

    Private Shared Function BuildDefaultInstanceProfile() As Dictionary(Of String, Object)
        Dim Queue As New Dictionary(Of String, Object) From {
            {"Id", "default"},
            {"Name", "Default"},
            {"PlaylistSources", New List(Of Object)},
            {"SongUuids", New List(Of String)},
            {"HistoryUuids", New List(Of String)},
            {"Index", -1},
            {"HistoryPosition", -1},
            {"PlaylistPosition", 0},
            {"Shuffle", False},
            {"RepeatMode", "none"}
        }
        Return New Dictionary(Of String, Object) From {
            {"ActiveQueueId", "default"},
            {"Volume", 1.0},
            {"Queues", New List(Of Object) From {Queue}}
        }
    End Function

    Private Shared Function GetPortFromBaseUrl(BaseUrl As String, Fallback As Integer) As Integer
        Try
            Dim Parsed As New Uri(BaseUrl)
            If Parsed.Port > 0 Then Return Parsed.Port
        Catch
        End Try
        Return Fallback
    End Function

    Private Shared Function GetBepInExStatusText(Status As OmniMixBepInExStatus) As String
        Select Case Status
            Case OmniMixBepInExStatus.Managed
                Return "已由 OmniMix 管理"
            Case OmniMixBepInExStatus.NeedsUpdate
                Return "需要更新"
            Case OmniMixBepInExStatus.Unmanaged
                Return "已安装（非 OmniMix 管理）"
            Case Else
                Return "未安装"
        End Select
    End Function

    Private Shared Function GetModStatusText(Status As OmniMixModInstallStatus) As String
        Select Case Status
            Case OmniMixModInstallStatus.Installed
                Return "已安装"
            Case OmniMixModInstallStatus.NeedsUpdate
                Return "需要更新"
            Case Else
                Return "未安装"
        End Select
    End Function

    Private Sub RenderModules(Modules As List(Of OmniMixModuleInfo))
        Modules = If(Modules, New List(Of OmniMixModuleInfo))
        CurrentModules = Modules
        PanModulesList.Children.Clear()

        Dim LoadedCount = Modules.Where(Function(ModuleInfo) Not String.IsNullOrWhiteSpace(ModuleInfo.LoadedAt)).Count()
        Dim EnabledCount = Modules.Where(Function(ModuleInfo) ModuleInfo.Enabled).Count()
        LabModulesSummary.Text = $"已读取 {Modules.Count} 个模块；当前已加载 {LoadedCount} 个，配置为启用 {EnabledCount} 个。"

        If Modules.Count = 0 Then
            PanModulesList.Children.Add(New MyListItem With {
                .Title = "暂无模块",
                .Info = "后端已连接，但当前没有已加载的音乐源模块。",
                .Height = 42,
                .PaddingLeft = 8,
                .Margin = New Thickness(0, 0, 0, 2),
                .IsScaleAnimationEnabled = False,
                .Type = MyListItem.CheckType.Clickable
            })
            Return
        End If

        For Each ModuleInfo In Modules.OrderBy(Function(Item) Item.Priority).ThenBy(Function(Item) Item.Name)
            AddModuleItem(ModuleInfo)
        Next
    End Sub

    Private Sub RenderLaunchpad(Modules As List(Of OmniMixModuleInfo))
        Modules = If(Modules, New List(Of OmniMixModuleInfo))
        CurrentModules = Modules
        PanLaunchpadList.Children.Clear()

        Dim Links As New List(Of Tuple(Of OmniMixModuleInfo, OmniMixModuleLinkEntryInfo))
        For Each ModuleInfo In Modules.OrderBy(Function(Item) Item.Priority).ThenBy(Function(Item) Item.Name)
            For Each LinkEntry In If(ModuleInfo.LinkEntries, New List(Of OmniMixModuleLinkEntryInfo))
                Links.Add(Tuple.Create(ModuleInfo, LinkEntry))
            Next
        Next

        LabModulesSummary.Text = $"启动台：{Links.Count} 个模块快捷入口。"
        If Links.Count = 0 Then
            PanLaunchpadList.Children.Add(New MyListItem With {
                .Title = "暂无快捷入口",
                .Info = "已加载的模块暂未提供启动台入口。",
                .Width = 320,
                .Height = 48,
                .PaddingLeft = 8,
                .Margin = New Thickness(0, 0, 0, 2),
                .IsScaleAnimationEnabled = False,
                .Type = MyListItem.CheckType.Clickable
            })
            Return
        End If

        For Each Payload In Links
            AddLaunchpadItem(Payload.Item1, Payload.Item2)
        Next
    End Sub

    Private Sub AddLaunchpadItem(ModuleInfo As OmniMixModuleInfo, LinkEntry As OmniMixModuleLinkEntryInfo)
        Dim Item As New MyListItem With {
            .Title = NonEmpty(LinkEntry.Title, LinkEntry.Id),
            .Info = NonEmpty(ModuleInfo.Name, ModuleInfo.Id),
            .Width = 150,
            .Height = 74,
            .PaddingLeft = 8,
            .Logo = ModuleLinkIcon(LinkEntry),
            .LogoScale = 0.9,
            .Margin = New Thickness(0, 0, 8, 8),
            .IsScaleAnimationEnabled = False,
            .Type = MyListItem.CheckType.Clickable,
            .Tag = Tuple.Create(ModuleInfo, LinkEntry)
        }
        AddHandler Item.Click, AddressOf LaunchpadItem_Click
        PanLaunchpadList.Children.Add(Item)
    End Sub

    Private Async Sub LaunchpadItem_Click(sender As Object, e As MouseButtonEventArgs)
        Dim Payload = TryCast(CType(sender, FrameworkElement).Tag, Tuple(Of OmniMixModuleInfo, OmniMixModuleLinkEntryInfo))
        If Payload Is Nothing Then Return
        Await OpenModuleUiAsync(Payload.Item1, "link", Payload.Item2.Id, Payload.Item2.Title)
    End Sub

    Private Sub AddModuleItem(ModuleInfo As OmniMixModuleInfo)
        Dim Item As New MyListItem With {
            .Title = NonEmpty(ModuleInfo.Name, ModuleInfo.Id),
            .Info = BuildModuleInfoText(ModuleInfo),
            .Height = 58,
            .PaddingLeft = 8,
            .Margin = New Thickness(0, 0, 0, 2),
            .Logo = If(ModuleInfo.Enabled, Logo.IconButtonCheck, Logo.IconButtonStop),
            .LogoScale = 0.82,
            .IsScaleAnimationEnabled = False,
            .Type = MyListItem.CheckType.Clickable,
            .Tag = ModuleInfo
        }

        Dim Buttons As New List(Of MyIconButton)
        Dim EnableButton As New MyIconButton With {
            .Logo = If(ModuleInfo.Enabled, Logo.IconButtonCheck, Logo.IconButtonStop),
            .LogoScale = 0.9,
            .ToolTip = If(ModuleInfo.Enabled, "禁用模块", "启用模块"),
            .Theme = If(ModuleInfo.Enabled, MyIconButton.Themes.Color, MyIconButton.Themes.Red),
            .Tag = ModuleInfo
        }
        AddHandler EnableButton.Click, AddressOf ModuleEnableButton_Click
        Buttons.Add(EnableButton)

        For Each LinkEntry In If(ModuleInfo.LinkEntries, New List(Of OmniMixModuleLinkEntryInfo))
            Dim LinkButton As New MyIconButton With {
                .Logo = Logo.IconButtonOpen,
                .LogoScale = 1.05,
                .ToolTip = "打开快捷入口：" & NonEmpty(LinkEntry.Title, LinkEntry.Id),
                .Tag = Tuple.Create(ModuleInfo, LinkEntry)
            }
            AddHandler LinkButton.Click, AddressOf ModuleLinkButton_Click
            Buttons.Add(LinkButton)
        Next
        If ModuleInfo.HasSettingsUI Then
            Dim SettingsButton As New MyIconButton With {
                .Logo = Logo.IconButtonSetup,
                .LogoScale = 1.05,
                .ToolTip = "打开设置 UI",
                .Tag = ModuleInfo
            }
            AddHandler SettingsButton.Click, AddressOf ModuleSettingsButton_Click
            Buttons.Add(SettingsButton)
        End If
        Item.Buttons = Buttons

        AddHandler Item.Click, AddressOf ModuleItem_Click
        PanModulesList.Children.Add(Item)
    End Sub

    Private Async Sub ModuleItem_Click(sender As Object, e As MouseButtonEventArgs)
        Dim Item = CType(sender, MyListItem)
        Dim ModuleInfo = CType(Item.Tag, OmniMixModuleInfo)
        If ModuleInfo.HasSettingsUI Then
            Await OpenModuleUiAsync(ModuleInfo, "default")
        ElseIf ModuleInfo.LinkEntries IsNot Nothing AndAlso ModuleInfo.LinkEntries.Count > 0 Then
            Dim LinkEntry = ModuleInfo.LinkEntries(0)
            Await OpenModuleUiAsync(ModuleInfo, "link", LinkEntry.Id, LinkEntry.Title)
        Else
            Await OpenModuleUiAsync(ModuleInfo, "default")
        End If
    End Sub

    Private Async Sub ModuleSettingsButton_Click(sender As Object, e As EventArgs)
        Dim Button = CType(sender, MyIconButton)
        Dim ModuleInfo = CType(Button.Tag, OmniMixModuleInfo)
        Await OpenModuleUiAsync(ModuleInfo, "default")
    End Sub

    Private Async Sub ModuleLinkButton_Click(sender As Object, e As EventArgs)
        Dim Button = CType(sender, MyIconButton)
        Dim Payload = CType(Button.Tag, Tuple(Of OmniMixModuleInfo, OmniMixModuleLinkEntryInfo))
        Await OpenModuleUiAsync(Payload.Item1, "link", Payload.Item2.Id, Payload.Item2.Title)
    End Sub

    Private Async Function OpenModuleUiAsync(ModuleInfo As OmniMixModuleInfo, UiKind As String, Optional LinkId As String = "", Optional LinkTitle As String = "") As Task
        If ModuleInfo Is Nothing OrElse String.IsNullOrWhiteSpace(ModuleInfo.Id) Then Return
        If String.IsNullOrWhiteSpace(CurrentBaseUrl) Then Return

        PreviousModulesPaneForDetail = CurrentModulesPane
        ModuleUiRequestSerial += 1
        Dim RequestSerial = ModuleUiRequestSerial
        CurrentModuleId = ModuleInfo.Id
        CurrentUiKind = UiKind
        CurrentLinkId = If(LinkId, "")
        CurrentDetailKind = "module"
        CurrentGameDetailId = ""
        ExpandedModuleTitle = NonEmpty(LinkTitle, NonEmpty(ModuleInfo.Name, ModuleInfo.Id))
        Dim UiLabel = If(UiKind = "settings", "模块设置", If(UiKind = "link", "快捷入口", "模块 UI"))
        LabModuleUiTitle.Text = UiLabel & " - " & ExpandedModuleTitle
        CardModules.Visibility = Visibility.Collapsed
        CardModuleUi.Visibility = Visibility.Visible
        PanModuleUi.Children.Clear()
        LabModuleUiSummary.Text = "正在加载 " & NonEmpty(ModuleInfo.Name, ModuleInfo.Id) & " 的" & UiLabel & "..."
        ScrollOmniMixPageToTop()

        Try
            Await WsClient.ConnectAsync(CurrentBaseUrl)

            Dim Tree As OmniMixRawNodeData
            If UiKind = "settings" Then
                Tree = Await OmniMixApiClient.GetModuleSettingsUiAsync(CurrentBaseUrl, ModuleInfo.Id)
            ElseIf UiKind = "link" Then
                Tree = Await OmniMixApiClient.GetModuleLinkUiAsync(CurrentBaseUrl, ModuleInfo.Id, CurrentLinkId)
            Else
                Tree = Await OmniMixApiClient.GetModuleUiAsync(CurrentBaseUrl, ModuleInfo.Id)
            End If
            If RequestSerial <> ModuleUiRequestSerial Then Return
            PanModuleUi.Children.Clear()
            If Tree IsNot Nothing Then
                Dim Rendered = OmniMixRawNodeRenderer.Render(Tree, CurrentBaseUrl, AddressOf ModuleUiEvent_Dispatch)
                If IsRenderedModuleUiEmpty(Rendered) Then
                    PanModuleUi.Children.Add(CreateModuleUiEmptyItem("模块返回了空 UI", "请返回后重新进入，或在模块完成登录/导入流程后刷新。"))
                Else
                    PanModuleUi.Children.Add(Rendered)
                End If
            Else
                PanModuleUi.Children.Add(New MyListItem With {
                    .Title = "暂无可显示内容",
                    .Info = "模块没有返回可渲染的 UI。",
                    .Height = 42,
                    .PaddingLeft = 8,
                    .Margin = New Thickness(0, 0, 0, 2),
                    .IsScaleAnimationEnabled = False,
                    .Type = MyListItem.CheckType.Clickable
                })
            End If
            LabModulesSummary.Text = "已加载模块 UI：" & ExpandedModuleTitle
            LabModuleUiSummary.Text = "已加载模块 UI，交互事件将通过 WebSocket 分发到后端。"
            ScrollOmniMixPageToTop()
        Catch Ex As Exception
            If RequestSerial <> ModuleUiRequestSerial Then Return
            PanModuleUi.Children.Clear()
            PanModuleUi.Children.Add(New MyListItem With {
                .Title = "加载失败",
                .Info = Ex.Message,
                .Height = 42,
                .PaddingLeft = 8,
                .Margin = New Thickness(0, 0, 0, 2),
                .IsScaleAnimationEnabled = False,
                .Type = MyListItem.CheckType.Clickable
            })
            LabModulesSummary.Text = "模块 UI 加载失败：" & Ex.Message
            LabModuleUiSummary.Text = "模块 UI 加载失败：" & Ex.Message
            ScrollOmniMixPageToTop()
        End Try
    End Function

    Private Shared Function IsRenderedModuleUiEmpty(Element As FrameworkElement) As Boolean
        Dim Panel = TryCast(Element, Panel)
        Return Panel IsNot Nothing AndAlso Panel.Children.Count = 0
    End Function

    Private Shared Function CreateModuleUiEmptyItem(Title As String, Info As String) As MyListItem
        Return New MyListItem With {
            .Title = Title,
            .Info = Info,
            .Height = 46,
            .PaddingLeft = 8,
            .Margin = New Thickness(0, 0, 0, 2),
            .IsScaleAnimationEnabled = False,
            .Type = MyListItem.CheckType.Clickable
        }
    End Function

    Private Async Sub ModuleUiEvent_Dispatch(NodeId As String, Action As String, Value As String)
        If String.IsNullOrWhiteSpace(CurrentBaseUrl) OrElse String.IsNullOrWhiteSpace(CurrentModuleId) Then Return
        Try
            Dim NeedsRefresh = IsModuleUiRefreshEvent(NodeId, Action)
            Dim NeedsLibraryRefresh = IsModuleLibraryRefreshEvent(NodeId, Action)
            If NeedsRefresh Then LabModuleUiSummary.Text = "正在处理模块操作：" & NodeId
            Await WsClient.SendUiEventAsync(CurrentBaseUrl, CurrentModuleId, NodeId, Action, Value, CurrentUiKind, CurrentLinkId)
            If NeedsRefresh Then
                Hint(If(NeedsLibraryRefresh, "已发送导入请求，正在等待后端处理...", "已发送模块操作，正在刷新状态..."), HintType.Green, False)
                Await Task.Delay(If(NeedsLibraryRefresh, 2200, 1000))
                Await RefreshCurrentModuleUiAsync()
                If NeedsLibraryRefresh Then Await RefreshLibraryCacheAsync(CurrentBaseUrl)
                Hint(If(NeedsLibraryRefresh, "导入请求已提交，曲库状态已刷新。", "模块操作已处理，状态已刷新。"), HintType.Green, False)
            End If
            LabModulesSummary.Text = "已发送模块 UI 事件：" & NodeId & " / " & Action
            LabModuleUiSummary.Text = "已发送模块 UI 事件：" & NodeId & " / " & Action
        Catch Ex As Exception
            LabModuleUiSummary.Text = "模块 UI 事件发送失败：" & Ex.Message
        End Try
    End Sub

    Private Function IsModuleUiRefreshEvent(NodeId As String, Action As String) As Boolean
        Dim Key = If(NodeId, "")
        If Key.IndexOf("import", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
        If Key.IndexOf("playlist", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
        If Key.IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
        If Key.IndexOf("logout", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
        Return String.Equals(Action, "click", StringComparison.OrdinalIgnoreCase)
    End Function

    Private Function IsModuleLibraryRefreshEvent(NodeId As String, Action As String) As Boolean
        Dim Key = If(NodeId, "")
        If Key.IndexOf("import", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
        If Key.IndexOf("playlist", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
        Return False
    End Function

    Private Async Function RefreshCurrentModuleUiAsync() As Task
        If String.IsNullOrWhiteSpace(CurrentModuleId) Then Return
        Dim ModuleInfo = CurrentModules.FirstOrDefault(Function(Info) String.Equals(Info.Id, CurrentModuleId, StringComparison.OrdinalIgnoreCase))
        If ModuleInfo Is Nothing Then Return
        Await OpenModuleUiAsync(ModuleInfo, CurrentUiKind, CurrentLinkId, If(CurrentUiKind = "link", ExpandedModuleTitle, ""))
    End Function

    Private Sub BtnModuleUiBack_Click(sender As Object, e As EventArgs) Handles BtnModuleUiBack.Click
        CollapseModuleUi()
    End Sub

    Private Function IsExpandedModule(ModuleId As String) As Boolean
        If String.IsNullOrWhiteSpace(ModuleId) OrElse String.IsNullOrWhiteSpace(ExpandedModuleKey) Then Return False
        Return ExpandedModuleKey.StartsWith(ModuleId & "|", StringComparison.OrdinalIgnoreCase)
    End Function

    Private Shared Function BuildModulePanelKey(ModuleId As String, UiKind As String, LinkId As String) As String
        Return If(ModuleId, "") & "|" & If(UiKind, "default") & "|" & If(LinkId, "")
    End Function

    Private Function CreateExpandedModulePanel() As FrameworkElement
        Dim Border As New Border With {
            .CornerRadius = New CornerRadius(8),
            .Padding = New Thickness(14),
            .Margin = New Thickness(0, 0, 0, 8)
        }
        Border.SetResourceReference(Border.BackgroundProperty, "ColorBrushSemiTransparent")

        Dim Panel As New StackPanel()
        Dim Title As New TextBlock With {
            .Text = If(String.IsNullOrWhiteSpace(ExpandedModuleTitle), "模块 UI", ExpandedModuleTitle),
            .FontSize = 13,
            .FontWeight = FontWeights.SemiBold,
            .Margin = New Thickness(0, 0, 0, 10)
        }
        Title.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush3")
        Panel.Children.Add(Title)

        If ExpandedModuleLoading Then
            Dim LoadingText As New TextBlock With {.Text = "正在加载...", .FontSize = 12}
            LoadingText.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush2")
            Panel.Children.Add(LoadingText)
        ElseIf Not String.IsNullOrWhiteSpace(ExpandedModuleError) Then
            Dim ErrorText As New TextBlock With {.Text = "加载失败：" & ExpandedModuleError, .FontSize = 12, .TextWrapping = TextWrapping.Wrap}
            ErrorText.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushRedLight")
            Panel.Children.Add(ErrorText)
        ElseIf ExpandedModuleTree IsNot Nothing Then
            Panel.Children.Add(OmniMixRawNodeRenderer.Render(ExpandedModuleTree, CurrentBaseUrl, AddressOf ModuleUiEvent_Dispatch))
        End If

        Border.Child = Panel
        Return Border
    End Function

    Private Sub CollapseModuleUi(Optional RestorePreviousPane As Boolean = True)
        CardModuleUi.Visibility = Visibility.Collapsed
        ModuleUiRequestSerial += 1
        CardModules.Visibility = If(PageKey = "Modules", Visibility.Visible, Visibility.Collapsed)
        PanModuleUi.Children.Clear()
        CurrentModuleId = ""
        CurrentUiKind = "default"
        CurrentLinkId = ""
        CurrentDetailKind = ""
        CurrentGameDetailId = ""
        ExpandedModuleKey = ""
        ExpandedModuleTree = Nothing
        ExpandedModuleError = ""
        ExpandedModuleLoading = False
        If RestorePreviousPane AndAlso Not String.IsNullOrWhiteSpace(PreviousModulesPaneForDetail) Then
            CurrentModulesPane = PreviousModulesPaneForDetail
        End If
        UpdatePluginPaneVisibility()
        Select Case CurrentModulesPane
            Case "launchpad"
                RenderLaunchpad(CurrentModules)
            Case "game"
                If Not String.IsNullOrWhiteSpace(CurrentBaseUrl) Then
                    Dim RefreshTask = RefreshModulesAsync(CurrentBaseUrl)
                End If
            Case Else
                RenderModules(CurrentModules)
        End Select
    End Sub

    Private Shared Function ModuleLinkIcon(LinkEntry As OmniMixModuleLinkEntryInfo) As String
        Select Case If(LinkEntry?.Icon, "").Trim().ToLowerInvariant()
            Case "music_note", "headphones", "radio"
                Return IconPlay
            Case "folder"
                Return Logo.IconButtonOpen
            Case "cloud"
                Return Logo.IconButtonRefresh
            Case Else
                Return Logo.IconButtonSetup
        End Select
    End Function

    Private Async Sub ModuleEnableButton_Click(sender As Object, e As EventArgs)
        If String.IsNullOrWhiteSpace(CurrentBaseUrl) Then Return

        Dim Button = CType(sender, MyIconButton)
        Dim ModuleInfo = TryCast(Button.Tag, OmniMixModuleInfo)
        If ModuleInfo Is Nothing Then Return

        Dim PreviousValue = ModuleInfo.Enabled
        ModuleInfo.Enabled = Not ModuleInfo.Enabled
        Button.IsEnabled = False
        LabModulesSummary.Text = "正在保存模块状态：" & NonEmpty(ModuleInfo.Name, ModuleInfo.Id)

        Try
            Await OmniMixApiClient.SetModuleEnabledAsync(CurrentBaseUrl, ModuleInfo.Id, ModuleInfo.Enabled)
            LabModulesSummary.Text = "已更新模块“" & NonEmpty(ModuleInfo.Name, ModuleInfo.Id) & "”的启用状态。变更将在后端模块加载策略允许时生效。"
            RenderModules(CurrentModules)
        Catch Ex As Exception
            ModuleInfo.Enabled = PreviousValue
            Button.IsEnabled = True
            LabModulesSummary.Text = "模块启用状态保存失败：" & Ex.Message
        End Try
    End Sub

    Private Async Sub ModuleItem_Changed(sender As Object, e As RouteEventArgs)
        If Not e.RaiseByMouse Then Return
        If String.IsNullOrWhiteSpace(CurrentBaseUrl) Then Return

        Dim Item = CType(sender, MyListItem)
        Dim ModuleInfo = CType(Item.Tag, OmniMixModuleInfo)
        Dim PreviousValue = ModuleInfo.Enabled
        ModuleInfo.Enabled = Item.Checked
        Item.Info = BuildModuleInfoText(ModuleInfo) & " · 正在保存..."

        Try
            Await OmniMixApiClient.SetModuleEnabledAsync(CurrentBaseUrl, ModuleInfo.Id, ModuleInfo.Enabled)
            Item.Info = BuildModuleInfoText(ModuleInfo)
            LabModulesSummary.Text = "已更新模块“" & NonEmpty(ModuleInfo.Name, ModuleInfo.Id) & "”的启用状态。变更将在后端模块加载策略允许时生效。"
        Catch Ex As Exception
            ModuleInfo.Enabled = PreviousValue
            Item.Checked = PreviousValue
            Item.Info = BuildModuleInfoText(ModuleInfo)
            LabModulesSummary.Text = "模块启用状态保存失败：" & Ex.Message
        End Try
    End Sub

    Private Shared Function BuildModuleInfoText(ModuleInfo As OmniMixModuleInfo) As String
        Dim Parts As New List(Of String)
        If Not String.IsNullOrWhiteSpace(ModuleInfo.Version) Then Parts.Add("v" & ModuleInfo.Version)
        Parts.Add(GetModuleStatusText(ModuleInfo))
        Parts.Add("优先级 " & ModuleInfo.Priority)
        If ModuleInfo.HasSettingsUI Then Parts.Add("设置 UI")
        If ModuleInfo.HasQuickLinks Then Parts.Add($"{If(ModuleInfo.LinkEntries, New List(Of OmniMixModuleLinkEntryInfo)).Count} 个快捷入口")
        Return String.Join(" · ", Parts)
    End Function

    Private Shared Function GetModuleStatusText(ModuleInfo As OmniMixModuleInfo) As String
        Dim Loaded = Not String.IsNullOrWhiteSpace(ModuleInfo.LoadedAt)
        If Loaded AndAlso ModuleInfo.Enabled Then Return "已加载并激活"
        If Loaded AndAlso Not ModuleInfo.Enabled Then Return "已加载但下次启动将禁用"
        If Not Loaded AndAlso ModuleInfo.Enabled Then Return "下次启动时将加载"
        Return "已禁用"
    End Function

    Private Async Function RefreshLibraryAsync(BaseUrl As String) As Task
        If PageKey <> "Library" Then Return

        LabLibrarySummary.Text = "正在加载曲库..."
        Await RefreshLibraryCacheAsync(BaseUrl)
    End Function

    Private Async Function RefreshLibraryCacheAsync(BaseUrl As String) As Task
        If IsRefreshingLibraryCache Then Return
        IsRefreshingLibraryCache = True
        Try
            Dim Playlist = Await OmniMixApiClient.GetPlaylistAsync(BaseUrl)
            Playlist = If(Playlist, New OmniMixPlaylistData)
            Dim HasIncomingLibrary = If(Playlist.Tags, New List(Of OmniMixTagInfo)).Count > 0 OrElse
                If(Playlist.Albums, New List(Of OmniMixAlbumInfo)).Count > 0 OrElse
                If(Playlist.Songs, New List(Of OmniMixSongInfo)).Count > 0
            Dim HasExistingLibrary = CurrentLibraryPlaylist IsNot Nothing AndAlso (
                If(CurrentLibraryPlaylist.Tags, New List(Of OmniMixTagInfo)).Count > 0 OrElse
                If(CurrentLibraryPlaylist.Albums, New List(Of OmniMixAlbumInfo)).Count > 0 OrElse
                If(CurrentLibraryPlaylist.Playlists, New List(Of OmniMixLibraryPlaylistInfo)).Count > 0 OrElse
                If(CurrentLibraryPlaylist.Songs, New List(Of OmniMixSongInfo)).Count > 0)
            If Not HasIncomingLibrary AndAlso HasExistingLibrary Then Return

            CurrentLibraryPlaylist = Playlist
            Dim TagSongs As New Dictionary(Of String, List(Of OmniMixSongInfo))(StringComparer.OrdinalIgnoreCase)
            For Each TagInfo In If(CurrentLibraryPlaylist.Tags, New List(Of OmniMixTagInfo))
                If String.IsNullOrWhiteSpace(TagInfo.Id) Then Continue For
                Try
                    TagSongs(TagInfo.Id) = Await OmniMixApiClient.GetSongsAsync(BaseUrl, TagId:=TagInfo.Id)
                Catch
                End Try
            Next
            CurrentLibraryTagSongs = TagSongs

            If PageKey = "Library" Then RenderLibrary()
        Catch Ex As Exception
            LabLibrarySummary.Text = "曲库加载失败：" & Ex.Message
        Finally
            IsRefreshingLibraryCache = False
        End Try
    End Function

    Private Sub RenderLibrary()
        Dim Playlist = If(CurrentLibraryPlaylist, New OmniMixPlaylistData)
        Dim Albums = If(Playlist.Albums, New List(Of OmniMixAlbumInfo))
        Dim Songs = If(Playlist.Songs, New List(Of OmniMixSongInfo))
        Dim LibraryPlaylists = If(Playlist.Playlists, New List(Of OmniMixLibraryPlaylistInfo))
        Dim SelectedModuleId = If(CurrentLibraryPane, "")
        Dim HasSourceFilter = Not String.IsNullOrWhiteSpace(SelectedModuleId)

        PanLibraryList.Children.Clear()

        Dim VisibleSongs = Songs.
            Where(Function(Song) Not HasSourceFilter OrElse String.Equals(Song.ModuleId, SelectedModuleId, StringComparison.OrdinalIgnoreCase)).
            ToList()
        VisibleSongs = MergeLibraryTagSongs(VisibleSongs, SelectedModuleId)
        Dim VisibleAlbums = Albums.
            Where(Function(Album)
                      If Not HasSourceFilter Then Return True
                      If String.Equals(Album.ModuleId, SelectedModuleId, StringComparison.OrdinalIgnoreCase) Then Return True
                      Return VisibleSongs.Any(Function(Song) String.Equals(Song.AlbumId, Album.Id, StringComparison.OrdinalIgnoreCase))
                  End Function).
            ToList()
        Dim VisiblePlaylists = LibraryPlaylists.
            Where(Function(Item) Not HasSourceFilter OrElse String.Equals(Item.ModuleId, SelectedModuleId, StringComparison.OrdinalIgnoreCase)).
            ToList()
        Dim Groups = BuildLibraryPlaylistGroups(VisiblePlaylists)
        Dim PlaylistSongUuids As New HashSet(Of String)(
            Groups.SelectMany(Function(Group) Group.Songs).
                Select(Function(Song) If(Song.Uuid, "")).
                Where(Function(Uuid) Not String.IsNullOrWhiteSpace(Uuid)),
            StringComparer.OrdinalIgnoreCase)
        Groups.AddRange(BuildLibraryAlbumGroups(
            VisibleAlbums,
            VisibleSongs.Where(Function(Song) String.IsNullOrWhiteSpace(Song.Uuid) OrElse Not PlaylistSongUuids.Contains(Song.Uuid)).ToList()))

        LabLibrarySummary.Text = $"{Groups.Count} 组，{VisibleSongs.Count} 首歌曲。"

        If Groups.Count = 0 Then
            AddLibraryItem("这里还没有歌曲", "换一个来源，或等待对应模块加载完成。", 0)
            Return
        End If

        For Each Group In Groups
            AddLibraryAlbumItem(Group)
        Next
    End Sub

    Private Function MergeLibraryTagSongs(Songs As List(Of OmniMixSongInfo), SelectedModuleId As String) As List(Of OmniMixSongInfo)
        Dim Result = If(Songs, New List(Of OmniMixSongInfo)).ToList()
        Dim KnownUuids As New HashSet(Of String)(Result.Select(Function(Song) If(Song.Uuid, "")), StringComparer.OrdinalIgnoreCase)
        Dim HasSourceFilter = Not String.IsNullOrWhiteSpace(SelectedModuleId)
        Dim RelevantTagIds = If(CurrentLibraryPlaylist?.Tags, New List(Of OmniMixTagInfo)).
            Where(Function(TagInfo) Not HasSourceFilter OrElse String.Equals(TagInfo.ModuleId, SelectedModuleId, StringComparison.OrdinalIgnoreCase)).
            Select(Function(TagInfo) TagInfo.Id).
            Where(Function(TagId) Not String.IsNullOrWhiteSpace(TagId)).
            ToList()

        For Each TagId In RelevantTagIds
            Dim TagSongList As List(Of OmniMixSongInfo) = Nothing
            If Not CurrentLibraryTagSongs.TryGetValue(TagId, TagSongList) Then Continue For
            For Each SongInfo In If(TagSongList, New List(Of OmniMixSongInfo))
                If SongInfo Is Nothing OrElse String.IsNullOrWhiteSpace(SongInfo.Uuid) Then Continue For
                If KnownUuids.Contains(SongInfo.Uuid) Then Continue For
                If HasSourceFilter AndAlso Not String.IsNullOrWhiteSpace(SongInfo.ModuleId) AndAlso Not String.Equals(SongInfo.ModuleId, SelectedModuleId, StringComparison.OrdinalIgnoreCase) Then Continue For
                Result.Add(SongInfo)
                KnownUuids.Add(SongInfo.Uuid)
            Next
        Next

        Return Result
    End Function

    Private Shared Function BuildLibraryPlaylistGroups(Playlists As IEnumerable(Of OmniMixLibraryPlaylistInfo)) As List(Of LibraryAlbumGroup)
        Return If(Playlists, Enumerable.Empty(Of OmniMixLibraryPlaylistInfo)).
            Where(Function(Item) Item IsNot Nothing AndAlso Item.Songs IsNot Nothing AndAlso Item.Songs.Count > 0).
            OrderBy(Function(Item) Item.SortOrder).
            ThenBy(Function(Item) NonEmpty(Item.Name, Item.Id)).
            Select(Function(Item) New LibraryAlbumGroup With {
                .GroupId = "playlist_" & NonEmpty(Item.Id, Guid.NewGuid().ToString("N")),
                .Name = NonEmpty(Item.Name, Item.Id),
                .ModuleId = Item.ModuleId,
                .CoverPath = Item.CoverPath,
                .Songs = Item.Songs.ToList()
            }).
            ToList()
    End Function

    Private Sub AddLibraryItem(Title As String, Info As String, Level As Integer)
        PanLibraryList.Children.Add(New MyListItem With {
            .Title = Title,
            .Info = Info,
            .Height = 42,
            .PaddingLeft = 8 + Level * 24,
            .Margin = New Thickness(0, 0, 0, 2),
            .IsScaleAnimationEnabled = False,
            .Type = MyListItem.CheckType.Clickable
        })
    End Sub

    Private Function BuildLibraryAlbumGroups(Albums As List(Of OmniMixAlbumInfo), Songs As List(Of OmniMixSongInfo)) As List(Of LibraryAlbumGroup)
        Dim Groups As New List(Of LibraryAlbumGroup)
        Dim UsedSongUuids As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim SingleSongsByModule As New Dictionary(Of String, List(Of OmniMixSongInfo))(StringComparer.OrdinalIgnoreCase)

        For Each AlbumInfo In If(Albums, New List(Of OmniMixAlbumInfo))
            Dim AlbumSongs = If(Songs, New List(Of OmniMixSongInfo)).
                Where(Function(Song) String.Equals(Song.AlbumId, AlbumInfo.Id, StringComparison.OrdinalIgnoreCase)).
                OrderBy(Function(Song) NonEmpty(Song.Title, Song.Uuid)).
                ToList()
            If AlbumSongs.Count = 0 AndAlso AlbumInfo.SongCount <= 0 Then Continue For

            For Each SongInfo In AlbumSongs
                If Not String.IsNullOrWhiteSpace(SongInfo.Uuid) Then UsedSongUuids.Add(SongInfo.Uuid)
            Next

            If AlbumSongs.Count = 1 Then
                AddSingleLibrarySong(SingleSongsByModule, NonEmpty(AlbumInfo.ModuleId, AlbumSongs(0).ModuleId), AlbumSongs(0))
                Continue For
            End If

            Groups.Add(New LibraryAlbumGroup With {
                .GroupId = "album_" & NonEmpty(AlbumInfo.Id, Guid.NewGuid().ToString("N")),
                .Name = NonEmpty(AlbumInfo.Name, AlbumInfo.Id),
                .ModuleId = AlbumInfo.ModuleId,
                .CoverPath = AlbumInfo.CoverPath,
                .Songs = AlbumSongs
            })
        Next

        Dim LooseSongs = If(Songs, New List(Of OmniMixSongInfo)).
            Where(Function(Song) String.IsNullOrWhiteSpace(Song.Uuid) OrElse Not UsedSongUuids.Contains(Song.Uuid)).
            OrderBy(Function(Song) NonEmpty(Song.Title, Song.Uuid)).
            ToList()
        For Each Entry In SingleSongsByModule
            If Entry.Value.Count = 0 Then Continue For
            Groups.Add(New LibraryAlbumGroup With {
                .GroupId = "singles_" & NonEmpty(Entry.Key, "unknown"),
                .Name = "单曲",
                .ModuleId = Entry.Key,
                .CoverPath = Entry.Value.Select(Function(Song) NonEmpty(Song.CoverPath, Song.CoverUrl)).FirstOrDefault(Function(Cover) Not String.IsNullOrWhiteSpace(Cover)),
                .Songs = Entry.Value.OrderBy(Function(Song) NonEmpty(Song.Title, Song.Uuid)).ToList()
            })
        Next
        If LooseSongs.Count > 0 Then
            Groups.Add(New LibraryAlbumGroup With {
                .GroupId = "loose_" & NonEmpty(LooseSongs.First().ModuleId, "unknown"),
                .Name = "未分组",
                .ModuleId = NonEmpty(LooseSongs.First().ModuleId, ""),
                .Songs = LooseSongs
            })
        End If

        Return Groups
    End Function

    Private Shared Sub AddSingleLibrarySong(Groups As Dictionary(Of String, List(Of OmniMixSongInfo)), ModuleId As String, Song As OmniMixSongInfo)
        If Song Is Nothing Then Return
        Dim Key = NonEmpty(ModuleId, "unknown")
        Dim GroupSongs As List(Of OmniMixSongInfo) = Nothing
        If Not Groups.TryGetValue(Key, GroupSongs) Then
            GroupSongs = New List(Of OmniMixSongInfo)
            Groups(Key) = GroupSongs
        End If
        If Not GroupSongs.Any(Function(Item) String.Equals(Item.Uuid, Song.Uuid, StringComparison.OrdinalIgnoreCase)) Then GroupSongs.Add(Song)
    End Sub

    Private Sub AddLibraryAlbumItem(Group As LibraryAlbumGroup)
        Dim IsExpanded = ExpandedLibraryAlbums.Contains(Group.GroupId)
        Dim SongUuids = Group.Songs.Select(Function(Song) Song.Uuid).Where(Function(Uuid) Not String.IsNullOrWhiteSpace(Uuid)).ToList()
        Dim Item As New MyListItem With {
            .Title = NonEmpty(Group.Name, Group.GroupId),
            .Info = $"{Group.Songs.Count} 首{If(String.IsNullOrWhiteSpace(Group.ModuleId), "", " · " & Group.ModuleId)}",
            .Height = 68,
            .PaddingLeft = 8,
            .Logo = ResolveLibraryAlbumCover(Group),
            .LogoWidth = 60,
            .LogoScale = 1,
            .Margin = New Thickness(0, 0, 0, 2),
            .IsScaleAnimationEnabled = False,
            .Type = MyListItem.CheckType.Clickable
        }

        Dim ExpandButton As New MyIconButton With {
            .Logo = If(IsExpanded, IconExpandUp, IconExpandDown),
            .LogoScale = 0.8,
            .ToolTip = If(IsExpanded, "收起", "展开"),
            .Tag = Group.GroupId
        }
        AddHandler ExpandButton.Click, AddressOf LibraryAlbumExpandButton_Click

        Dim QueueButton As New MyIconButton With {
            .Logo = IconPlus,
            .LogoScale = 0.82,
            .ToolTip = "加入队列",
            .Tag = New LibraryQueueGroupPayload With {
                .GroupId = Group.GroupId,
                .Name = Group.Name,
                .Uuids = SongUuids
            }
        }
        AddHandler QueueButton.Click, AddressOf LibrarySourceButton_Click
        Item.Buttons = New List(Of MyIconButton) From {ExpandButton, QueueButton}
        PanLibraryList.Children.Add(Item)

        If IsExpanded Then
            For Each SongInfo In Group.Songs
                Dim InfoParts As New List(Of String)
                If Not String.IsNullOrWhiteSpace(SongInfo.Artist) Then InfoParts.Add(SongInfo.Artist)
                If SongInfo.Duration > 0 Then InfoParts.Add(FormatDuration(SongInfo.Duration))
                AddLibrarySongItem(SongInfo, String.Join(" · ", InfoParts), Group)
            Next
        End If
    End Sub

    Private Sub AddLibrarySongItem(SongInfo As OmniMixSongInfo, Info As String, Group As LibraryAlbumGroup)
        Dim CapturedSongInfo = SongInfo
        Dim CapturedInfo = Info
        Dim CapturedGroup = Group
        PanLibraryList.Children.Add(New MyVirtualizingElement(Of MyListItem)(
            Function() CreateLibrarySongItem(CapturedSongInfo, CapturedInfo, CapturedGroup)
        ) With {.Height = 64})
    End Sub

    Private Function CreateLibrarySongItem(SongInfo As OmniMixSongInfo, Info As String, Group As LibraryAlbumGroup) As MyListItem
        Dim Item As New MyListItem With {
            .Title = NonEmpty(SongInfo.Title, SongInfo.Uuid),
            .Info = Info,
            .Height = 64,
            .PaddingLeft = 32,
            .Logo = ResolveLibrarySongCover(SongInfo, Group),
            .LogoWidth = 58,
            .LogoScale = 1,
            .Margin = New Thickness(0, 0, 0, 2),
            .IsScaleAnimationEnabled = False,
            .Type = MyListItem.CheckType.Clickable,
            .Tag = SongInfo
        }

        Dim QueueButton As New MyIconButton With {
            .Logo = IconPlus,
            .LogoScale = 0.82,
            .ToolTip = "加入队列",
            .Tag = SongInfo
        }
        AddHandler QueueButton.Click, AddressOf LibrarySongQueueButton_Click
        Item.Buttons = New List(Of MyIconButton) From {QueueButton}
        AddHandler Item.Click, AddressOf LibrarySongItem_Click
        Return Item
    End Function

    Private Async Sub LibrarySongItem_Click(sender As Object, e As MouseButtonEventArgs)
        Dim Item = CType(sender, MyListItem)
        Dim SongInfo = TryCast(Item.Tag, OmniMixSongInfo)
        If SongInfo Is Nothing OrElse String.IsNullOrWhiteSpace(SongInfo.Uuid) Then Return
        Await PlayLibrarySongAsync(SongInfo)
    End Sub

    Private Sub LibraryAlbumExpandButton_Click(sender As Object, e As EventArgs)
        Dim Button = CType(sender, MyIconButton)
        Dim GroupId = TryCast(Button.Tag, String)
        If String.IsNullOrWhiteSpace(GroupId) Then Return

        If ExpandedLibraryAlbums.Contains(GroupId) Then
            ExpandedLibraryAlbums.Remove(GroupId)
        Else
            ExpandedLibraryAlbums.Add(GroupId)
        End If
        RenderLibrary()
    End Sub

    Private Async Sub LibrarySongQueueButton_Click(sender As Object, e As EventArgs)
        Dim Button = CType(sender, MyIconButton)
        Dim SongInfo = TryCast(Button.Tag, OmniMixSongInfo)
        If SongInfo Is Nothing OrElse String.IsNullOrWhiteSpace(SongInfo.Uuid) Then Return
        Await AddLibrarySongToQueueAsync(SongInfo)
    End Sub

    Private Async Sub LibrarySourceButton_Click(sender As Object, e As EventArgs)
        Dim Button = CType(sender, MyIconButton)
        Dim Payload = TryCast(Button.Tag, LibraryQueueGroupPayload)
        If Payload Is Nothing Then Return
        Await AddLibraryGroupToQueueAsync(Payload)
    End Sub

    Private Async Function AddLibraryGroupToQueueAsync(Payload As LibraryQueueGroupPayload) As Task
        If String.IsNullOrWhiteSpace(CurrentBaseUrl) Then Return

        Dim Uuids = If(Payload.Uuids, New List(Of String)).
            Where(Function(Uuid) Not String.IsNullOrWhiteSpace(Uuid)).
            Distinct().
            ToList()
        If Uuids.Count = 0 Then
            LabLibrarySummary.Text = "没有可加入队列的歌曲。"
            Return
        End If

        Try
            Dim Instance = Await GetTargetInstanceAsync()
            If Instance Is Nothing Then
                LabLibrarySummary.Text = "没有可控制的播放实例，暂时无法加入队列。"
                Return
            End If

            If CanUseLiveInstanceApi(Instance) Then
                Await OmniMixApiClient.AddToQueueRangeAsync(CurrentBaseUrl, Instance.Id, Uuids)
            Else
                Await AddUuidsToOfflineProfileAsync(Instance.Id, Uuids)
            End If
            Hint("Added to queue: " & Uuids.Count & " song(s).", HintType.Green, False)
            Await RefreshQueuePaneOrBackendAsync()
            LabLibrarySummary.Text = $"已加入队列：{NonEmpty(Payload.Name, Payload.GroupId)}（{Uuids.Count} 首）"
        Catch Ex As Exception
            LabLibrarySummary.Text = "加入队列失败：" & Ex.Message
        End Try
    End Function

    Private Async Function PlayLibrarySongAsync(SongInfo As OmniMixSongInfo) As Task
        If String.IsNullOrWhiteSpace(CurrentBaseUrl) Then Return

        Try
            Dim Instance = Await GetTargetInstanceAsync()
            If Instance Is Nothing Then
                LabLibrarySummary.Text = "没有可控制的播放实例，暂时无法播放歌曲。"
                Return
            End If

            If CanUseLiveInstanceApi(Instance) Then
                Await OmniMixApiClient.PlayAsync(CurrentBaseUrl, Instance.Id, SongInfo.Uuid)
            Else
                Await AddUuidsToOfflineProfileAsync(Instance.Id, New List(Of String) From {SongInfo.Uuid})
            End If
            LabLibrarySummary.Text = "已发送播放指令：" & NonEmpty(SongInfo.Title, SongInfo.Uuid)
        Catch Ex As Exception
            LabLibrarySummary.Text = "播放失败：" & Ex.Message
        End Try
    End Function

    Private Async Function AddLibrarySongToQueueAsync(SongInfo As OmniMixSongInfo) As Task
        If String.IsNullOrWhiteSpace(CurrentBaseUrl) Then Return

        Try
            Dim Instance = Await GetTargetInstanceAsync()
            If Instance Is Nothing Then
                LabLibrarySummary.Text = "没有可控制的播放实例，暂时无法加入队列。"
                Return
            End If

            If CanUseLiveInstanceApi(Instance) Then
                Await OmniMixApiClient.AddToQueueAsync(CurrentBaseUrl, Instance.Id, SongInfo.Uuid)
            Else
                Await AddUuidsToOfflineProfileAsync(Instance.Id, New List(Of String) From {SongInfo.Uuid})
            End If
            Hint("Added to queue: " & NonEmpty(SongInfo.Title, SongInfo.Uuid), HintType.Green, False)
            Await RefreshQueuePaneOrBackendAsync()
            LabLibrarySummary.Text = "已加入队列：" & NonEmpty(SongInfo.Title, SongInfo.Uuid)
        Catch Ex As Exception
            LabLibrarySummary.Text = "加入队列失败：" & Ex.Message
        End Try
    End Function

    Private Shared Function CanUseLiveInstanceApi(Instance As OmniMixPlaybackInstanceInfo) As Boolean
        Return Instance IsNot Nothing AndAlso Instance.IsServerManaged AndAlso Instance.Attached
    End Function

    Private Async Function GetTargetInstanceAsync() As Task(Of OmniMixPlaybackInstanceInfo)
        If String.IsNullOrWhiteSpace(CurrentBaseUrl) Then Return Nothing

        Dim Instances = Await OmniMixApiClient.GetInstancesAsync(CurrentBaseUrl)
        Dim Config = Await OmniMixApiClient.GetConfigAsync(CurrentBaseUrl)
        Return PickActiveInstance(Instances, ConfigString(Config, "active_instance", ""))
    End Function

    Private Async Function AddUuidsToOfflineProfileAsync(InstanceId As String, Uuids As IEnumerable(Of String)) As Task
        If String.IsNullOrWhiteSpace(CurrentBaseUrl) OrElse String.IsNullOrWhiteSpace(InstanceId) Then Return

        Dim CleanUuids = If(Uuids, Enumerable.Empty(Of String)()).
            Where(Function(Uuid) Not String.IsNullOrWhiteSpace(Uuid)).
            ToList()
        If CleanUuids.Count = 0 Then Return

        Dim ProfileJson As Dictionary(Of String, JsonElement) = Nothing
        Try
            ProfileJson = Await OmniMixApiClient.GetInstanceProfileAsync(CurrentBaseUrl, InstanceId)
        Catch
            ProfileJson = Nothing
        End Try

        Dim Profile = BuildMutableInstanceProfile(ProfileJson)
        Dim Queue = GetMutableActiveQueue(Profile)
        Dim SongUuids = GetMutableStringList(Queue, "SongUuids")
        SongUuids.AddRange(CleanUuids)

        Await OmniMixApiClient.UpdateInstanceProfileAsync(CurrentBaseUrl, InstanceId, Profile)
        ActiveInstanceId = InstanceId
    End Function

    Private Shared Function BuildMutableInstanceProfile(ProfileJson As Dictionary(Of String, JsonElement)) As Dictionary(Of String, Object)
        If ProfileJson Is Nothing OrElse ProfileJson.Count = 0 Then Return BuildDefaultInstanceProfile()

        Dim ActiveQueueId = ProfileString(ProfileJson, "ActiveQueueId", "default")
        Dim Queues As New List(Of Object)
        Dim QueuesElement As JsonElement
        If TryGetProfileElement(ProfileJson, "Queues", QueuesElement) AndAlso QueuesElement.ValueKind = JsonValueKind.Array Then
            For Each QueueElement In QueuesElement.EnumerateArray()
                If QueueElement.ValueKind <> JsonValueKind.Object Then Continue For
                Queues.Add(BuildMutableQueue(QueueElement))
            Next
        End If

        If Queues.Count = 0 Then
            Dim DefaultProfile = BuildDefaultInstanceProfile()
            Queues = DirectCast(DefaultProfile("Queues"), List(Of Object))
        End If

        If String.IsNullOrWhiteSpace(ActiveQueueId) Then
            Dim FirstQueue = TryCast(Queues.FirstOrDefault(), Dictionary(Of String, Object))
            ActiveQueueId = If(FirstQueue Is Nothing, "default", TryCast(FirstQueue("Id"), String))
            If String.IsNullOrWhiteSpace(ActiveQueueId) Then ActiveQueueId = "default"
        End If

        Return New Dictionary(Of String, Object) From {
            {"ActiveQueueId", ActiveQueueId},
            {"Volume", ProfileDouble(ProfileJson, "Volume", 1.0)},
            {"Queues", Queues}
        }
    End Function

    Private Shared Function BuildMutableQueue(QueueElement As JsonElement) As Dictionary(Of String, Object)
        Return New Dictionary(Of String, Object) From {
            {"Id", JsonString(QueueElement, "Id", "default")},
            {"Name", JsonString(QueueElement, "Name", "Default")},
            {"PlaylistSources", JsonObjectList(QueueElement, "PlaylistSources")},
            {"SongUuids", JsonStringList(QueueElement, "SongUuids")},
            {"HistoryUuids", JsonStringList(QueueElement, "HistoryUuids")},
            {"Index", JsonInteger(QueueElement, "Index", -1)},
            {"HistoryPosition", JsonInteger(QueueElement, "HistoryPosition", -1)},
            {"PlaylistPosition", JsonInteger(QueueElement, "PlaylistPosition", 0)},
            {"Shuffle", JsonBoolean(QueueElement, "Shuffle", False)},
            {"RepeatMode", JsonString(QueueElement, "RepeatMode", "none")}
        }
    End Function

    Private Shared Function GetMutableActiveQueue(Profile As Dictionary(Of String, Object)) As Dictionary(Of String, Object)
        Dim Queues = TryCast(Profile("Queues"), List(Of Object))
        If Queues Is Nothing Then
            Queues = New List(Of Object)
            Profile("Queues") = Queues
        End If

        Dim ActiveQueueId = TryCast(Profile("ActiveQueueId"), String)
        For Each QueueObject In Queues
            Dim Queue = TryCast(QueueObject, Dictionary(Of String, Object))
            If Queue IsNot Nothing AndAlso String.Equals(TryCast(Queue("Id"), String), ActiveQueueId, StringComparison.OrdinalIgnoreCase) Then Return Queue
        Next

        Dim FirstQueue = TryCast(Queues.FirstOrDefault(), Dictionary(Of String, Object))
        If FirstQueue IsNot Nothing Then
            Profile("ActiveQueueId") = TryCast(FirstQueue("Id"), String)
            Return FirstQueue
        End If

        Dim CreatedQueue = DirectCast(BuildDefaultInstanceProfile()("Queues"), List(Of Object)).
            OfType(Of Dictionary(Of String, Object))().
            First()
        Queues.Add(CreatedQueue)
        Profile("ActiveQueueId") = "default"
        Return CreatedQueue
    End Function

    Private Shared Function GetMutableStringList(Queue As Dictionary(Of String, Object), Key As String) As List(Of String)
        Dim Value As Object = Nothing
        If Queue IsNot Nothing AndAlso Queue.TryGetValue(Key, Value) Then
            Dim Existing = TryCast(Value, List(Of String))
            If Existing IsNot Nothing Then Return Existing
        End If

        Dim Created As New List(Of String)
        If Queue IsNot Nothing Then Queue(Key) = Created
        Return Created
    End Function

    Private Async Function LoadOfflineProfileQueueItemsAsync(BaseUrl As String, InstanceId As String, IsHistory As Boolean) As Task(Of List(Of OmniMixQueueItemInfo))
        Dim ProfileJson = Await OmniMixApiClient.GetInstanceProfileAsync(BaseUrl, InstanceId)
        Dim Profile = BuildMutableInstanceProfile(ProfileJson)
        Dim Queue = GetMutableActiveQueue(Profile)
        Dim Uuids = GetMutableStringList(Queue, If(IsHistory, "HistoryUuids", "SongUuids"))
        Dim Result As New List(Of OmniMixQueueItemInfo)

        For Index = 0 To Uuids.Count - 1
            Dim Uuid = Uuids(Index)
            Dim SongInfo = FindLibrarySongByUuid(Uuid)
            Dim Item As New OmniMixQueueItemInfo With {
                .Uuid = Uuid,
                .Index = Index,
                .Title = If(SongInfo Is Nothing, Uuid, NonEmpty(SongInfo.Title, Uuid)),
                .Artist = If(SongInfo Is Nothing, "", SongInfo.Artist),
                .AlbumId = If(SongInfo Is Nothing, "", SongInfo.AlbumId),
                .Duration = If(SongInfo Is Nothing, 0, SongInfo.Duration),
                .ModuleId = If(SongInfo Is Nothing, "", SongInfo.ModuleId),
                .CoverPath = If(SongInfo Is Nothing, "", SongInfo.CoverPath),
                .CoverUrl = If(SongInfo Is Nothing, "", SongInfo.CoverUrl),
                .ImageUrl = If(SongInfo Is Nothing, "", SongInfo.ImageUrl)
            }
            Result.Add(Item)
        Next

        Return Result
    End Function

    Private Function FindLibrarySongByUuid(Uuid As String) As OmniMixSongInfo
        If String.IsNullOrWhiteSpace(Uuid) Then Return Nothing

        If CurrentLibraryPlaylist IsNot Nothing AndAlso CurrentLibraryPlaylist.Songs IsNot Nothing Then
            Dim DirectSong = CurrentLibraryPlaylist.Songs.FirstOrDefault(Function(SongInfo) String.Equals(SongInfo.Uuid, Uuid, StringComparison.OrdinalIgnoreCase))
            If DirectSong IsNot Nothing Then Return DirectSong
        End If

        For Each Pair In If(CurrentLibraryTagSongs, New Dictionary(Of String, List(Of OmniMixSongInfo))(StringComparer.OrdinalIgnoreCase))
            If Pair.Value Is Nothing Then Continue For
            Dim TagSong = Pair.Value.FirstOrDefault(Function(SongInfo) String.Equals(SongInfo.Uuid, Uuid, StringComparison.OrdinalIgnoreCase))
            If TagSong IsNot Nothing Then Return TagSong
        Next

        Return Nothing
    End Function

    Private Shared Function TryGetProfileElement(ProfileJson As Dictionary(Of String, JsonElement), Key As String, ByRef Value As JsonElement) As Boolean
        If ProfileJson Is Nothing Then Return False
        For Each Pair In ProfileJson
            If String.Equals(Pair.Key, Key, StringComparison.OrdinalIgnoreCase) Then
                Value = Pair.Value
                Return True
            End If
        Next
        Return False
    End Function

    Private Shared Function TryGetJsonProperty(Element As JsonElement, Key As String, ByRef Value As JsonElement) As Boolean
        If Element.ValueKind <> JsonValueKind.Object Then Return False
        For Each PropertyInfo In Element.EnumerateObject()
            If String.Equals(PropertyInfo.Name, Key, StringComparison.OrdinalIgnoreCase) Then
                Value = PropertyInfo.Value
                Return True
            End If
        Next
        Return False
    End Function

    Private Shared Function ProfileString(ProfileJson As Dictionary(Of String, JsonElement), Key As String, Fallback As String) As String
        Dim Value As JsonElement
        If Not TryGetProfileElement(ProfileJson, Key, Value) Then Return Fallback
        Return JsonElementString(Value, Fallback)
    End Function

    Private Shared Function ProfileDouble(ProfileJson As Dictionary(Of String, JsonElement), Key As String, Fallback As Double) As Double
        Dim Value As JsonElement
        If Not TryGetProfileElement(ProfileJson, Key, Value) Then Return Fallback
        Return JsonElementDouble(Value, Fallback)
    End Function

    Private Shared Function JsonString(Element As JsonElement, Key As String, Fallback As String) As String
        Dim Value As JsonElement
        If Not TryGetJsonProperty(Element, Key, Value) Then Return Fallback
        Return JsonElementString(Value, Fallback)
    End Function

    Private Shared Function JsonInteger(Element As JsonElement, Key As String, Fallback As Integer) As Integer
        Dim Value As JsonElement
        If Not TryGetJsonProperty(Element, Key, Value) Then Return Fallback
        Select Case Value.ValueKind
            Case JsonValueKind.Number
                Dim Parsed As Integer
                If Value.TryGetInt32(Parsed) Then Return Parsed
            Case JsonValueKind.String
                Dim Parsed As Integer
                If Integer.TryParse(Value.GetString(), Parsed) Then Return Parsed
        End Select
        Return Fallback
    End Function

    Private Shared Function JsonBoolean(Element As JsonElement, Key As String, Fallback As Boolean) As Boolean
        Dim Value As JsonElement
        If Not TryGetJsonProperty(Element, Key, Value) Then Return Fallback
        Select Case Value.ValueKind
            Case JsonValueKind.True
                Return True
            Case JsonValueKind.False
                Return False
            Case JsonValueKind.String
                Dim Parsed As Boolean
                If Boolean.TryParse(Value.GetString(), Parsed) Then Return Parsed
        End Select
        Return Fallback
    End Function

    Private Shared Function JsonStringList(Element As JsonElement, Key As String) As List(Of String)
        Dim Result As New List(Of String)
        Dim Value As JsonElement
        If Not TryGetJsonProperty(Element, Key, Value) OrElse Value.ValueKind <> JsonValueKind.Array Then Return Result
        For Each Item In Value.EnumerateArray()
            Dim Text = JsonElementString(Item, "")
            If Not String.IsNullOrWhiteSpace(Text) Then Result.Add(Text)
        Next
        Return Result
    End Function

    Private Shared Function JsonObjectList(Element As JsonElement, Key As String) As List(Of Object)
        Dim Result As New List(Of Object)
        Dim Value As JsonElement
        If Not TryGetJsonProperty(Element, Key, Value) OrElse Value.ValueKind <> JsonValueKind.Array Then Return Result
        For Each Item In Value.EnumerateArray()
            Result.Add(JsonElementToObject(Item))
        Next
        Return Result
    End Function

    Private Shared Function JsonElementString(Element As JsonElement, Fallback As String) As String
        Select Case Element.ValueKind
            Case JsonValueKind.String
                Return NonEmpty(Element.GetString(), Fallback)
            Case JsonValueKind.Number, JsonValueKind.True, JsonValueKind.False
                Return Element.ToString()
            Case Else
                Return Fallback
        End Select
    End Function

    Private Shared Function JsonElementDouble(Element As JsonElement, Fallback As Double) As Double
        Select Case Element.ValueKind
            Case JsonValueKind.Number
                Dim Parsed As Double
                If Element.TryGetDouble(Parsed) Then Return Parsed
            Case JsonValueKind.String
                Dim Parsed As Double
                If Double.TryParse(Element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, Parsed) Then Return Parsed
        End Select
        Return Fallback
    End Function

    Private Shared Function JsonElementToObject(Element As JsonElement) As Object
        Select Case Element.ValueKind
            Case JsonValueKind.Object
                Dim Result As New Dictionary(Of String, Object)(StringComparer.OrdinalIgnoreCase)
                For Each PropertyInfo In Element.EnumerateObject()
                    Result(PropertyInfo.Name) = JsonElementToObject(PropertyInfo.Value)
                Next
                Return Result
            Case JsonValueKind.Array
                Dim Result As New List(Of Object)
                For Each Item In Element.EnumerateArray()
                    Result.Add(JsonElementToObject(Item))
                Next
                Return Result
            Case JsonValueKind.String
                Return Element.GetString()
            Case JsonValueKind.Number
                Dim LongValue As Long
                If Element.TryGetInt64(LongValue) Then Return LongValue
                Dim DoubleValue As Double
                If Element.TryGetDouble(DoubleValue) Then Return DoubleValue
                Return Element.ToString()
            Case JsonValueKind.True
                Return True
            Case JsonValueKind.False
                Return False
            Case Else
                Return Nothing
        End Select
    End Function

    Private Async Function GetControllableInstanceAsync() As Task(Of OmniMixPlaybackInstanceInfo)
        If String.IsNullOrWhiteSpace(CurrentBaseUrl) Then Return Nothing

        Dim Instances = Await OmniMixApiClient.GetInstancesAsync(CurrentBaseUrl)
        Dim Config = Await OmniMixApiClient.GetConfigAsync(CurrentBaseUrl)
        Dim ActiveId = ConfigString(Config, "active_instance", "")
        Dim Active = If(String.IsNullOrWhiteSpace(ActiveId), Nothing, Instances.FirstOrDefault(Function(Instance) String.Equals(Instance.Id, ActiveId, StringComparison.OrdinalIgnoreCase) AndAlso CanUseLiveInstanceApi(Instance)))
        If Active IsNot Nothing Then Return Active

        Dim OnlineServer = If(Instances, New List(Of OmniMixPlaybackInstanceInfo)).
            FirstOrDefault(Function(Instance) CanUseLiveInstanceApi(Instance))
        If OnlineServer IsNot Nothing Then Return OnlineServer

        Return Nothing
    End Function

    Private Function PromptEqualizerNumber(Title As String, Description As String, DefaultValue As Double, MinValue As Double, MaxValue As Double, ByRef Result As Double) As Boolean
        Dim Text = MyMsgBoxInput(
            Title,
            Description & vbCrLf & "范围：" & MinValue.ToString("0.###", CultureInfo.InvariantCulture) & " - " & MaxValue.ToString("0.###", CultureInfo.InvariantCulture),
            DefaultValue.ToString("0.###", CultureInfo.InvariantCulture),
            HintText:="数字")
        If Text Is Nothing Then Return False

        If Not TryParseEqualizerNumber(Text, Result) Then
            LabEqualizerSummary.Text = "输入的数字无效。"
            Return False
        End If
        Result = Clamp(Result, MinValue, MaxValue)
        Return True
    End Function

    Private Function PromptEqualizerType(DefaultType As String) As String
        Dim Text = MyMsgBoxInput(
            "滤波类型",
            "输入滤波类型：Peaking、LowShelf、HighShelf、LowPass、HighPass。",
            NonEmpty(NormalizeEqualizerType(DefaultType), "Peaking"),
            HintText:="Peaking / LowShelf / HighShelf / LowPass / HighPass")
        If Text Is Nothing Then Return Nothing

        Dim TypeValue = NormalizeEqualizerType(Text)
        If String.IsNullOrWhiteSpace(TypeValue) Then
            LabEqualizerSummary.Text = "滤波类型无效。"
            Return Nothing
        End If
        Return TypeValue
    End Function

    Private Shared Function TryParseEqualizerNumber(Text As String, ByRef Value As Double) As Boolean
        If Double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, Value) Then Return True
        Return Double.TryParse(Text, NumberStyles.Float, CultureInfo.CurrentCulture, Value)
    End Function

    Private Shared Function NormalizeEqualizerState(State As OmniMixEqualizerStateInfo) As OmniMixEqualizerStateInfo
        State = If(State, New OmniMixEqualizerStateInfo)
        State.Points = If(State.Points, New List(Of OmniMixEqualizerPointInfo))
        For Each Point In State.Points
            If String.IsNullOrWhiteSpace(Point.Id) Then Point.Id = "vb_" & Guid.NewGuid().ToString("N")
            Point.Frequency = Clamp(Point.Frequency, 20, 22000)
            Point.GainDb = Clamp(Point.GainDb, -24, 24)
            Point.Q = Clamp(Point.Q, 0.1, 20)
            Dim TypeValue = NormalizeEqualizerType(Point.Type)
            Point.Type = If(String.IsNullOrWhiteSpace(TypeValue), "Peaking", TypeValue)
        Next
        State.GlobalGainDb = Clamp(State.GlobalGainDb, -24, 24)
        Return State
    End Function

    Private Shared Function CloneEqualizerState(State As OmniMixEqualizerStateInfo) As OmniMixEqualizerStateInfo
        State = NormalizeEqualizerState(State)
        Return New OmniMixEqualizerStateInfo With {
            .Enabled = State.Enabled,
            .GlobalGainDb = State.GlobalGainDb,
            .SoftClipEnabled = State.SoftClipEnabled,
            .Points = State.Points.Select(Function(Point) New OmniMixEqualizerPointInfo With {
                .Id = Point.Id,
                .Frequency = Point.Frequency,
                .GainDb = Point.GainDb,
                .Q = Point.Q,
                .Type = Point.Type
            }).ToList()
        }
    End Function

    Private Shared Function BuildEqualizerStateInfo(State As OmniMixEqualizerStateInfo) As String
        State = NormalizeEqualizerState(State)
        Return $"{If(State.Enabled, "启用", "禁用")} · 全局增益 {FormatDb(State.GlobalGainDb)} · {If(State.SoftClipEnabled, "软削波开启", "软削波关闭")} · {State.Points.Count} 个控制点"
    End Function

    Private Shared Function NormalizeEqualizerType(Value As String) As String
        Dim Key = If(Value, "").Trim().Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant()
        Select Case Key
            Case "peaking", "peak", "bell"
                Return "Peaking"
            Case "lowshelf", "bass"
                Return "LowShelf"
            Case "highshelf", "treble"
                Return "HighShelf"
            Case "lowpass", "lp"
                Return "LowPass"
            Case "highpass", "hp"
                Return "HighPass"
            Case Else
                Return ""
        End Select
    End Function

    Private Shared Function GetEqualizerTypeLabel(TypeValue As String) As String
        Select Case NormalizeEqualizerType(TypeValue)
            Case "LowShelf"
                Return "低架"
            Case "HighShelf"
                Return "高架"
            Case "LowPass"
                Return "低通"
            Case "HighPass"
                Return "高通"
            Case Else
                Return "峰值"
        End Select
    End Function

    Private Shared Function FormatFrequency(Frequency As Double) As String
        If Frequency >= 1000 Then Return (Frequency / 1000).ToString("0.#", CultureInfo.InvariantCulture) & " kHz"
        Return Frequency.ToString("0", CultureInfo.InvariantCulture) & " Hz"
    End Function

    Private Shared Function FormatDb(Value As Double) As String
        Dim Prefix = If(Value > 0, "+", "")
        Return Prefix & Value.ToString("0.0", CultureInfo.InvariantCulture) & " dB"
    End Function

    Private Shared Function Clamp(Value As Double, MinValue As Double, MaxValue As Double) As Double
        Return Math.Max(MinValue, Math.Min(MaxValue, Value))
    End Function

    Private Shared Function NonEmpty(Value As String, Fallback As String) As String
        Return If(String.IsNullOrWhiteSpace(Value), Fallback, Value)
    End Function

    Private Function ResolveQueueItemCover(Track As OmniMixTrackInfo) As String
        If Track Is Nothing Then Return ""
        If Not String.IsNullOrWhiteSpace(Track.Uuid) AndAlso Not String.IsNullOrWhiteSpace(CurrentBaseUrl) Then
            Return CurrentBaseUrl.TrimEnd("/"c) & "/api/track/cover?uuid=" & Uri.EscapeDataString(Track.Uuid)
        End If
        For Each Candidate In {Track.CoverPath, Track.CoverUrl, Track.ImageUrl}
            If String.IsNullOrWhiteSpace(Candidate) Then Continue For
            If Candidate.StartsWith("/") AndAlso Not String.IsNullOrWhiteSpace(CurrentBaseUrl) Then
                Return CurrentBaseUrl.TrimEnd("/"c) & Candidate
            End If
            Return Candidate
        Next
        Return ""
    End Function

    Private Function ResolveLibraryAlbumCover(Group As LibraryAlbumGroup) As String
        If Group Is Nothing Then Return ""
        For Each SongInfo In If(Group.Songs, New List(Of OmniMixSongInfo))
            Dim SongCover = ResolveLibrarySongCover(SongInfo, Group)
            If Not String.IsNullOrWhiteSpace(SongCover) Then Return SongCover
        Next
        Return ResolveCoverCandidate(Group.CoverPath)
    End Function

    Private Function ResolveLibrarySongCover(SongInfo As OmniMixSongInfo, Group As LibraryAlbumGroup) As String
        If SongInfo Is Nothing Then Return ""
        If Not String.IsNullOrWhiteSpace(SongInfo.Uuid) AndAlso Not String.IsNullOrWhiteSpace(CurrentBaseUrl) Then
            Return CurrentBaseUrl.TrimEnd("/"c) & "/api/track/cover?uuid=" & Uri.EscapeDataString(SongInfo.Uuid)
        End If

        For Each Candidate In {SongInfo.CoverPath, SongInfo.CoverUrl, SongInfo.ImageUrl, If(Group Is Nothing, "", Group.CoverPath)}
            Dim Resolved = ResolveCoverCandidate(Candidate)
            If Not String.IsNullOrWhiteSpace(Resolved) Then Return Resolved
        Next
        Return ""
    End Function

    Private Function ResolveCoverCandidate(Candidate As String) As String
        If String.IsNullOrWhiteSpace(Candidate) Then Return ""
        If Candidate.StartsWith("/") AndAlso Not String.IsNullOrWhiteSpace(CurrentBaseUrl) Then
            Return CurrentBaseUrl.TrimEnd("/"c) & Candidate
        End If
        Return Candidate
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

    Private Shared Function ConfigBoolean(Config As Dictionary(Of String, JsonElement), Key As String, Fallback As Boolean) As Boolean
        If Config Is Nothing OrElse Not Config.ContainsKey(Key) Then Return Fallback
        Dim Value = Config(Key)
        Select Case Value.ValueKind
            Case JsonValueKind.True
                Return True
            Case JsonValueKind.False
                Return False
            Case JsonValueKind.String
                Dim Parsed As Boolean
                If Boolean.TryParse(Value.GetString(), Parsed) Then Return Parsed
                Return Fallback
            Case Else
                Return Fallback
        End Select
    End Function

    Private Shared Function ArchiveTicks(ArchivedAt As String) As Long
        Dim Parsed As DateTimeOffset
        If DateTimeOffset.TryParse(ArchivedAt, Parsed) Then Return Parsed.UtcTicks
        Return 0
    End Function

    Private Shared Function FormatArchiveTime(ArchivedAt As String) As String
        Dim Parsed As DateTimeOffset
        If DateTimeOffset.TryParse(ArchivedAt, Parsed) Then Return Parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
        Return ArchivedAt
    End Function

    Private Shared Function FormatDuration(Seconds As Double) As String
        If Seconds <= 0 Then Return ""
        Dim TotalSeconds = CInt(Math.Floor(Seconds))
        Return $"{TotalSeconds \ 60}:{(TotalSeconds Mod 60).ToString().PadLeft(2, "0"c)}"
    End Function
End Class
