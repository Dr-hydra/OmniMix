Imports System.ComponentModel
Imports System.Windows.Interop

Public Class FormMain

    Private Const DefaultBackgroundImagePath As String = "Images/Backgrounds/default.png"
    Private Const BackgroundModeDefault As String = "__default__"
    Private Const BackgroundModeSolid As String = "__solid__"

    Private FrmOmniMixHome As PageOmniMixRight
    Private FrmOmniMixLibrary As PageOmniMixRight
    Private FrmOmniMixModules As PageOmniMixRight
    Private FrmOmniMixSettings As PageOmniMixRight
    Private FrmOmniMixAbout As PageOmniMixRight
    Private FrmOmniMixLeft As PageOmniMixLeft
    Private OmniMixFloatingPlaybackWindow As FloatingPlaybackWindow
    Private OmniMixTrayIcon As System.Windows.Forms.NotifyIcon
    Private OmniMixTrayMenu As System.Windows.Forms.ContextMenuStrip
    Private IsRestoringFromTray As Boolean = False
    Private IsWindowLoadFinished As Boolean = False
    Private OmniMixBaseUrl As String = ""

    Public Enum PageType
        Launch = 0
        Download = 1
        Link = 2
        Setup = 3
        Other = 4
    End Enum

    Public Class PageStackData
        Public Page As PageType
        Public Additional As Object

        Public Overrides Function Equals(other As Object) As Boolean
            If other Is Nothing Then Return False
            If TypeOf other Is PageStackData Then Return Page = DirectCast(other, PageStackData).Page
            If TypeOf other Is Integer Then Return CInt(Page) = CInt(other)
            Return False
        End Function

        Public Shared Operator =(left As PageStackData, right As PageStackData) As Boolean
            Return EqualityComparer(Of PageStackData).Default.Equals(left, right)
        End Operator

        Public Shared Operator <>(left As PageStackData, right As PageStackData) As Boolean
            Return Not left = right
        End Operator

        Public Shared Widening Operator CType(Value As PageType) As PageStackData
            Return New PageStackData With {.Page = Value}
        End Operator

        Public Shared Widening Operator CType(Value As PageStackData) As PageType
            Return Value.Page
        End Operator
    End Class

    Public PageCurrent As PageStackData = PageType.Launch
    Public PageLast As PageStackData = PageType.Launch
    Public PageLeft As MyPageLeft
    Public PageRight As MyPageRight

    Public Sub New()
        ApplicationStartTick = GetTimeMs()
        FrmMain = Me
        FrmOmniMixLeft = New PageOmniMixLeft
        FrmOmniMixHome = PageOmniMixRight.Create("Home")
        FrmOmniMixLibrary = PageOmniMixRight.Create("Library")
        FrmOmniMixModules = PageOmniMixRight.Create("Modules")
        FrmOmniMixSettings = PageOmniMixRight.Create("Settings")
        FrmOmniMixAbout = PageOmniMixRight.Create("About")

        ThemeCheckAll(False)
        ThemeRefresh(Settings.Get(Of Integer)("UiLauncherTheme"))
        [AddHandler](DragDrop.DragEnterEvent, New DragEventHandler(AddressOf HandleDrag), handledEventsToo:=True)
        [AddHandler](DragDrop.DragOverEvent, New DragEventHandler(AddressOf HandleDrag), handledEventsToo:=True)

        InitializeComponent()
        Opacity = 0

        If SystemUtils.HasAdminRole() Then
            Static Helper As New DragHelper
            AddHandler SourceInitialized,
            Sub()
                Dim WpfHelper As New WindowInteropHelper(Me)
                Helper.HwndIntPtrSource = HwndSource.FromHwnd(WpfHelper.Handle)
                Helper.AddHook()
            End Sub
            AddHandler Closing, Sub() Helper.RemoveDragHook()
            AddHandler Helper.DragDrop, Sub() FileDrag(Helper.DropFilePaths)
        End If

        PanMainLeft.Child = FrmOmniMixLeft
        PageLeft = FrmOmniMixLeft
        PanMainRight.Child = FrmOmniMixHome
        PageRight = FrmOmniMixHome
        ConfigureOmniMixLeft(PageType.Launch)
        FrmOmniMixHome.PageState = MyPageRight.PageStates.ContentStay

        If BuildType = BuildTypes.Debug Then Hint("[开发者模式] OmniMix GUI 正在使用迁移版 WPF UI 框架运行。", HintType.Red)
        If ModeDebug Then Hint("[调试模式] OmniMix GUI 正以调试模式运行，这可能会导致性能下降。")
        Logger.Info($"第二阶段加载用时：{GetTimeMs() - ApplicationStartTick} ms")
    End Sub

    Private Sub FormMain_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        ApplicationStartTick = GetTimeMs()
        Handle = New WindowInteropHelper(Me).Handle
        UpdateBackgroundAndTitleBar()

        BtnExtraBack.ShowCheck = AddressOf BtnExtraBack_ShowCheck

        Dim Resizer As New MyResizer(Me)
        Resizer.addResizerDown(ResizerB)
        Resizer.addResizerLeft(ResizerL)
        Resizer.addResizerLeftDown(ResizerLB)
        Resizer.addResizerLeftUp(ResizerLT)
        Resizer.addResizerRight(ResizerR)
        Resizer.addResizerRightDown(ResizerRB)
        Resizer.addResizerRightUp(ResizerRT)
        Resizer.addResizerUp(ResizerT)

        ThemeRefreshMain()
        Try
            Height = Math.Max(Settings.Get(Of Integer)("WindowHeight"), 650)
            Width = Math.Max(Settings.Get(Of Integer)("WindowWidth"), 900)
        Catch ex As Exception
            Logger.Error(ex, "读取窗口默认大小失败", LogBehavior.Toast)
            Height = MinHeight + 100
            Width = MinWidth + 100
        End Try

        Topmost = False
        If FrmStart IsNot Nothing Then FrmStart.Close(New TimeSpan(0, 0, 0, 0, 400 / AniSpeed))
        Top = (GetWPFSize(My.Computer.Screen.WorkingArea.Height) - Height) / 2
        Left = (GetWPFSize(My.Computer.Screen.WorkingArea.Width) - Width) / 2
        IsSizeSaveable = True
        ShowWindowToTop()
        EnsureOmniMixTrayIcon()

        Dim HwndSource As Interop.HwndSource = PresentationSource.FromVisual(Me)
        HwndSource.AddHook(New Interop.HwndSourceHook(AddressOf WndProc))
        OmniMixSmtcService.Initialize(AddressOf SendSystemMediaCommand)
        AniStart({
            AaCode(Sub() AniControlEnabled -= 1, 50),
            AaOpacity(Me, GetWindowOpacity(), 250, 100),
            AaDouble(Sub(i) TransformPos.Y += i, -TransformPos.Y, 600, 100, New AniEaseOutBack(AniEasePower.Weak)),
            AaDouble(Sub(i) TransformRotate.Angle += i, -TransformRotate.Angle, 500, 100, New AniEaseOutBack(AniEasePower.Weak)),
            AaCode(
            Sub()
                PanBack.RenderTransform = Nothing
                IsWindowLoadFinished = True
                Logger.Info($"DPI：{DPI}，系统版本：{Environment.OSVersion.VersionString}，OmniMix GUI 位置：{PathExe}")
            End Sub, , True)
        }, "Form Show")

        AniStart()
        TimerMainStart()
        ShowOmniMixFloatingPlaybackWindow(False)
        Logger.Info("OmniMix GUI 已启动。")
        Logger.Info($"第三阶段加载用时：{GetTimeMs() - ApplicationStartTick} ms")

        ' 启动后延迟 3 秒，检查是否有游戏集成需要更新并提示
        Task.Run(Async Function()
                     Await Task.Delay(3000)
                     CheckAndPromptGameIntegrationUpdate()
                 End Function)
    End Sub

    Public Sub EndProgram(SendWarning As Boolean)
        If IsProgramEnding Then Return
        IsProgramEnding = True
        CloseOmniMixFloatingPlaybackWindow()
        OmniMixSmtcService.Shutdown()
        OmniMixDesktopPlayerService.DisconnectAndWait()
        StopOmniMixBackendOnExit()
        DisposeOmniMixTrayIcon()
        RunInUiWait(
        Sub()
            IsHitTestVisible = False
            If PanBack.RenderTransform Is Nothing Then
                Dim TransformPos As New TranslateTransform(0, 0)
                Dim TransformRotate As New RotateTransform(0)
                Dim TransformScale As New ScaleTransform(1, 1)
                PanBack.RenderTransform = New TransformGroup() With {.Children = New TransformCollection({TransformRotate, TransformPos, TransformScale})}
                AniStart({
                    AaOpacity(Me, -Opacity, 140, 40, New AniEaseOutFluent(AniEasePower.Weak)),
                    AaDouble(
                    Sub(i)
                        TransformScale.ScaleX += i
                        TransformScale.ScaleY += i
                    End Sub, 0.88 - TransformScale.ScaleX, 180),
                    AaDouble(Sub(i) TransformPos.Y += i, 20 - TransformPos.Y, 180, 0, New AniEaseOutFluent(AniEasePower.Weak)),
                    AaDouble(Sub(i) TransformRotate.Angle += i, 0.6 - TransformRotate.Angle, 180, 0, New AniEaseInoutFluent(AniEasePower.Weak)),
                    AaCode(
                    Sub()
                        IsHitTestVisible = False
                        Top = -10000
                        ShowInTaskbar = False
                    End Sub, 210),
                    AaCode(AddressOf EndProgramForce, 230)
                }, "Form Close")
            Else
                EndProgramForce()
            End If
            Logger.Info("收到关闭指令")
        End Sub)
    End Sub

    Private Shared Sub StopOmniMixBackendOnExit()
        Try
            If Not Settings.Get(Of Boolean)("OmniMixCloseBackendWithGui") Then Return
            Logger.Info("正在随 OmniMix GUI 退出停止后端")
            Dim StopTask = System.Threading.Tasks.Task.Run(
            Async Function()
                Try
                    Dim Status = Await OmniMixApiClient.DiscoverAsync()
                    If Status Is Nothing OrElse Not Status.IsOnline OrElse String.IsNullOrWhiteSpace(Status.BaseUrl) Then
                        Logger.Info("关闭 OmniMix GUI 时未发现在线后端，无需停止")
                        Return
                    End If
                    Await OmniMixApiClient.StopBackendAsync(Status.BaseUrl)
                    Logger.Info("已请求 OmniMix 后端随 GUI 退出")
                Catch ex As Exception
                    Logger.Warn(ex, "关闭 OmniMix GUI 时停止后端失败")
                End Try
            End Function)
            If Not StopTask.Wait(1800) Then Logger.Warn("关闭 OmniMix GUI 时停止后端超时，继续退出")
        Catch ex As Exception
            Logger.Warn(ex, "关闭 OmniMix GUI 时停止后端失败")
        End Try
    End Sub

    Public Shared Sub EndProgramForce(Optional ReturnCode As ProcessReturnValues = ProcessReturnValues.Success)
        On Error Resume Next
        IsProgramEnding = True
        FrmMain?.DisposeOmniMixTrayIcon()
        AniControlEnabled += 1
        Logger.Info($"程序已退出，返回值：{ReturnCode}")
        Logger.Instance.Flush()
        If ReturnCode <> ProcessReturnValues.Success Then Environment.Exit(ReturnCode)
        Process.GetCurrentProcess.Kill()
    End Sub

    Private Sub BtnTitleClose_Click(sender As Object, e As RoutedEventArgs) Handles BtnTitleClose.Click
        EndProgram(True)
    End Sub

    Private Sub FormDragMove(sender As Object, e As MouseButtonEventArgs) Handles PanTitle.MouseLeftButtonDown, PanMsg.MouseLeftButtonDown
        On Error Resume Next
        If sender.IsMouseDirectlyOver Then DragMove()
    End Sub

    Public IsSizeSaveable As Boolean = False
    Private Sub FormMain_SizeChanged() Handles Me.SizeChanged, Me.Loaded
        If IsSizeSaveable Then
            Settings.Set("WindowHeight", Height)
            Settings.Set("WindowWidth", Width)
        End If
        RectForm.Rect = New Rect(0, 0, BorderForm.ActualWidth, BorderForm.ActualHeight)
        PanForm.Width = BorderForm.ActualWidth + 0.001
        PanForm.Height = BorderForm.ActualHeight + 0.001
        PanMain.Width = PanForm.Width
        PanMain.Height = Math.Max(0, PanForm.Height - PanTitle.ActualHeight)
        If WindowState = WindowState.Maximized Then WindowState = WindowState.Normal
    End Sub

    Private Sub BtnTitleMin_Click() Handles BtnTitleMin.Click
        WindowState = WindowState.Minimized
    End Sub

    Private Sub FormMain_StateChanged(sender As Object, e As EventArgs) Handles Me.StateChanged
        If IsRestoringFromTray OrElse IsProgramEnding OrElse WindowState <> WindowState.Minimized Then Return
        HideOmniMixToTray()
    End Sub

    Private Sub EnsureOmniMixTrayIcon()
        If OmniMixTrayIcon IsNot Nothing Then Return
        OmniMixTrayMenu = New System.Windows.Forms.ContextMenuStrip()
        Dim ShowItem = New System.Windows.Forms.ToolStripMenuItem("显示主窗口")
        AddHandler ShowItem.Click, Sub() ShowOmniMixFromTray()
        Dim FloatingItem = New System.Windows.Forms.ToolStripMenuItem("显示播放悬浮窗")
        AddHandler FloatingItem.Click, Sub() ToggleOmniMixFloatingPlaybackWindow()
        Dim ExitItem = New System.Windows.Forms.ToolStripMenuItem("退出 OmniMix")
        AddHandler ExitItem.Click, Sub() EndProgram(True)
        OmniMixTrayMenu.Items.Add(ShowItem)
        OmniMixTrayMenu.Items.Add(FloatingItem)
        OmniMixTrayMenu.Items.Add(New System.Windows.Forms.ToolStripSeparator())
        OmniMixTrayMenu.Items.Add(ExitItem)
        OmniMixTrayIcon = New System.Windows.Forms.NotifyIcon With {
            .Text = "OmniMix Player",
            .Icon = LoadOmniMixTrayIcon(),
            .ContextMenuStrip = OmniMixTrayMenu,
            .Visible = True
        }
        AddHandler OmniMixTrayIcon.DoubleClick, Sub() ShowOmniMixFromTray()
    End Sub

    Private Sub ToggleOmniMixFloatingPlaybackWindow()
        RunInUi(
        Sub()
            If OmniMixFloatingPlaybackWindow IsNot Nothing AndAlso OmniMixFloatingPlaybackWindow.IsVisible Then
                OmniMixFloatingPlaybackWindow.Hide()
            Else
                ShowOmniMixFloatingPlaybackWindow(True)
            End If
        End Sub)
    End Sub

    Private Sub ShowOmniMixFloatingPlaybackWindow(Optional ActivateWindow As Boolean = True)
        If OmniMixFloatingPlaybackWindow Is Nothing Then
            OmniMixFloatingPlaybackWindow = New FloatingPlaybackWindow()
            AddHandler OmniMixFloatingPlaybackWindow.Closed, Sub() OmniMixFloatingPlaybackWindow = Nothing
        End If
        OmniMixFloatingPlaybackWindow.ApplyAppearance()
        OmniMixFloatingPlaybackWindow.Show()
        OmniMixFloatingPlaybackWindow.EnsureTopmost()
        If ActivateWindow Then OmniMixFloatingPlaybackWindow.Activate()
    End Sub

    Public Sub UpdateOmniMixFloatingPlaybackWindowAppearance()
        Try
            OmniMixFloatingPlaybackWindow?.ApplyAppearance()
        Catch ex As Exception
            Logger.Warn(ex, "更新 OmniMix 播放悬浮窗外观失败")
        End Try
    End Sub

    Private Sub CloseOmniMixFloatingPlaybackWindow()
        Try
            If OmniMixFloatingPlaybackWindow IsNot Nothing Then
                OmniMixFloatingPlaybackWindow.Close()
                OmniMixFloatingPlaybackWindow = Nothing
            End If
        Catch ex As Exception
            Logger.Warn(ex, "关闭 OmniMix 播放悬浮窗失败")
        End Try
    End Sub

    Private Shared Function LoadOmniMixTrayIcon() As System.Drawing.Icon
        Try
            Dim ResourceInfo = System.Windows.Application.GetResourceStream(New Uri("pack://application:,,,/Images/icon.ico", UriKind.Absolute))
            If ResourceInfo IsNot Nothing AndAlso ResourceInfo.Stream IsNot Nothing Then
                Using ResourceStream = ResourceInfo.Stream
                    Using ResourceIcon As New System.Drawing.Icon(ResourceStream)
                        Return DirectCast(ResourceIcon.Clone(), System.Drawing.Icon)
                    End Using
                End Using
            End If
        Catch ex As Exception
            Logger.Warn(ex, "读取 OmniMix 托盘图标资源失败")
        End Try
        Return System.Drawing.SystemIcons.Application
    End Function

    Private Sub HideOmniMixToTray()
        EnsureOmniMixTrayIcon()
        WindowState = WindowState.Minimized
        Hidden = True
        Logger.Info("OmniMix GUI 已最小化到托盘")
    End Sub

    Private Sub ShowOmniMixFromTray()
        RunInUi(
        Sub()
            IsRestoringFromTray = True
            Hidden = False
            ShowWindowToTop()
            IsRestoringFromTray = False
        End Sub)
    End Sub

    Private Sub DisposeOmniMixTrayIcon()
        If OmniMixTrayIcon IsNot Nothing Then
            OmniMixTrayIcon.Visible = False
            OmniMixTrayIcon.Dispose()
            OmniMixTrayIcon = Nothing
        End If
        If OmniMixTrayMenu IsNot Nothing Then
            OmniMixTrayMenu.Dispose()
            OmniMixTrayMenu = Nothing
        End If
    End Sub

    Public Shared Sub UpdateBackgroundAndTitleBar(Value)
        If FrmMain Is Nothing OrElse Not FrmMain.IsLoaded Then Return
        FrmMain.UpdateBackgroundAndTitleBar()
    End Sub

    Public Sub UpdateBackgroundAndTitleBar()
        Logger.Info("从设置更新背景图片与标题栏样式")
        LoadBackgroundImage()
        UpdateWindowOpacity()
        UpdateControlOpacity()

        ShapeTitleLogo.Visibility = Visibility.Collapsed
        LabTitleLogo.Visibility = Visibility.Visible
        PanOmniMixConnectionStatus.Visibility = Visibility.Visible
        ImageTitleLogo.Visibility = Visibility.Collapsed
        PanTitleSelect.Visibility = Visibility.Visible
        LabTitleLogo.Text = "OmniMix"
        If String.IsNullOrWhiteSpace(LabOmniMixConnectionStatus.Text) Then SetOmniMixConnectionStatus(False)
        PanTitleMain.ColumnDefinitions(0).Width = New GridLength(1, GridUnitType.Star)
    End Sub

    Public Sub LoadBackgroundImage()
        Dim OpacityValue = NormalizePercentSetting(Settings.Get(Of Integer)("UiBackgroundOpacity"), 1000)
        ImgBack.Opacity = OpacityValue / 100.0
        ImgBack.Visibility = If(OpacityValue <= 0, Visibility.Collapsed, Visibility.Visible)

        Dim BlurRadius As Double
        If Settings.HasSaved("UiBackgroundClarity") Then
            Dim Clarity = NormalizePercentSetting(Settings.Get(Of Integer)("UiBackgroundClarity"), 100)
            BlurRadius = (100 - Clarity) * 0.3
        Else
            BlurRadius = Settings.Get(Of Integer)("UiBackgroundBlur")
        End If
        ImgBack.Effect = If(BlurRadius <= 0, Nothing, New Effects.BlurEffect With {.Radius = BlurRadius})
        ImgBack.Margin = New Thickness(-Math.Max(1, BlurRadius + 1) / 1.8)

        Dim ImagePath = Settings.Get(Of String)("UiBackgroundImagePath")
        Dim ImageUri = GetBackgroundImageUri(ImagePath)
        If ImageUri IsNot Nothing Then
            Try
                Dim Image As New BitmapImage()
                Image.BeginInit()
                Image.CacheOption = BitmapCacheOption.OnLoad
                Image.UriSource = ImageUri
                Image.EndInit()
                Image.Freeze()
                ImgBackImage.Source = Image
                ImgBackImage.Visibility = Visibility.Visible
            Catch ex As Exception
                Logger.Warn(ex, "加载自定义背景图失败")
                ImgBackImage.Source = Nothing
                ImgBackImage.Visibility = Visibility.Collapsed
            End Try
        Else
            ImgBackImage.Source = Nothing
            ImgBackImage.Visibility = Visibility.Collapsed
        End If
    End Sub

    Private Shared Function GetBackgroundImageUri(ImagePath As String) As Uri
        If String.IsNullOrWhiteSpace(ImagePath) OrElse ImagePath = BackgroundModeDefault Then
            Return New Uri(DefaultBackgroundImagePath, UriKind.Relative)
        End If
        If ImagePath = BackgroundModeSolid Then Return Nothing
        If File.Exists(ImagePath) Then Return New Uri(ImagePath, UriKind.Absolute)
        Return New Uri(DefaultBackgroundImagePath, UriKind.Relative)
    End Function

    Public Sub UpdateWindowOpacity()
        Opacity = GetWindowOpacity()
    End Sub

    Public Sub UpdateControlOpacity()
        Dim OpacityValue = NormalizePercentSetting(Settings.Get(Of Integer)("UiControlOpacity"), 100)
        Dim AlphaInt = Math.Max(0, Math.Min(255, CInt(OpacityValue / 100.0 * 255)))
        Dim Alpha As Byte = CByte(AlphaInt)
        Dim SidebarBrush As New SolidColorBrush(Color.FromArgb(Alpha, 255, 255, 255))
        SidebarBrush.Freeze()
        Application.Current.Resources("ColorBrushBackgroundTransparentSidebar") = SidebarBrush

        Dim CardBrush As New SolidColorBrush(Color.FromArgb(Alpha, 255, 255, 255))
        CardBrush.Freeze()
        Application.Current.Resources("ColorBrushCardBackground") = CardBrush
    End Sub

    Private Function GetWindowOpacity() As Double
        Dim Percent = NormalizePercentSetting(Settings.Get(Of Integer)("UiLauncherTransparent"), 600)
        Return Percent / 100.0 * 0.6 + 0.4
    End Function

    Private Shared Function NormalizePercentSetting(Value As Integer, LegacyMax As Integer) As Integer
        If Value > 100 AndAlso LegacyMax > 100 Then Value = CInt(Math.Round(Value / LegacyMax * 100.0))
        Return Math.Max(0, Math.Min(100, Value))
    End Function

    Public Sub SetOmniMixConnectionStatus(IsOnline As Boolean, Optional BaseUrl As String = "")
        If LabOmniMixConnectionStatus Is Nothing Then Return
        OmniMixBaseUrl = If(IsOnline, BaseUrl, "")
        LabOmniMixConnectionStatus.Text = If(IsOnline, "● 已连接", "● 未连接")
        LabOmniMixConnectionStatus.ToolTip = If(IsOnline AndAlso Not String.IsNullOrWhiteSpace(BaseUrl), "OmniMix 后端：" & BaseUrl, "OmniMix 后端未连接")
        LabOmniMixConnectionStatus.Opacity = If(IsOnline, 0.88, 0.62)
        If Not IsOnline Then
            OmniMixDesktopPlayerService.Disconnect()
            UpdateOmniMixInstanceMenu(New List(Of OmniMixPlaybackInstanceInfo), "")
        End If
        FrmOmniMixLeft?.SetBackendStatus(IsOnline, BaseUrl)
    End Sub

    Public Sub UpdateOmniMixInstanceMenu(Instances As List(Of OmniMixPlaybackInstanceInfo), ActiveInstanceId As String)
        If PanOmniMixInstanceMenu Is Nothing Then Return
        Instances = If(Instances, New List(Of OmniMixPlaybackInstanceInfo))
        If Not String.IsNullOrWhiteSpace(OmniMixBaseUrl) Then OmniMixDesktopPlayerService.ReconcileWithInstances(OmniMixBaseUrl, Instances)
        PanOmniMixInstanceMenu.Visibility = If(Instances.Count > 0, Visibility.Visible, Visibility.Collapsed)
        Dim ActiveInstance = Instances.FirstOrDefault(Function(Item) String.Equals(Item.Id, ActiveInstanceId, StringComparison.OrdinalIgnoreCase))
        If ActiveInstance Is Nothing Then ActiveInstance = Instances.FirstOrDefault(Function(Item) Item.Attached)
        If ActiveInstance Is Nothing Then ActiveInstance = Instances.FirstOrDefault()
        UpdateOmniMixInstanceMenuLabel(ActiveInstance)

        Dim Menu As New ContextMenu
        For Each Instance In Instances.OrderByDescending(Function(Item) Item.Attached).ThenBy(Function(Item) GetOmniMixInstanceDisplayName(Item))
            Dim Header As New StackPanel With {.Orientation = Orientation.Horizontal}
            Header.Children.Add(New TextBlock With {
                .Text = "●",
                .Foreground = If(Instance.Attached, New SolidColorBrush(Color.FromRgb(78, 201, 120)), New SolidColorBrush(Color.FromRgb(232, 91, 91))),
                .Margin = New Thickness(0, 0, 7, 0)
            })
            Header.Children.Add(New TextBlock With {.Text = GetOmniMixInstanceDisplayName(Instance)})

            Dim Item As New MenuItem With {
                .Header = Header,
                .Tag = Instance,
                .IsCheckable = True,
                .IsChecked = String.Equals(Instance.Id, ActiveInstanceId, StringComparison.OrdinalIgnoreCase)
            }
            AddHandler Item.Click, AddressOf OmniMixInstanceMenuItem_Click
            Menu.Items.Add(Item)
        Next
        PanOmniMixInstanceMenu.ContextMenu = Menu
    End Sub

    Private Shared Function GetOmniMixInstanceDisplayName(Instance As OmniMixPlaybackInstanceInfo) As String
        If Instance Is Nothing Then Return "未知实例"
        If Not String.IsNullOrWhiteSpace(Instance.GameName) Then Return Instance.GameName
        If Not String.IsNullOrWhiteSpace(Instance.Id) Then Return Instance.Id
        If Not String.IsNullOrWhiteSpace(Instance.ClientId) Then Return Instance.ClientId
        Return "未知实例"
    End Function

    Private Sub UpdateOmniMixInstanceMenuLabel(Instance As OmniMixPlaybackInstanceInfo)
        LabOmniMixInstanceMenu.Inlines.Clear()
        If Instance Is Nothing Then Return
        LabOmniMixInstanceMenu.Inlines.Add(New Run("●") With {
            .Foreground = If(Instance.Attached, New SolidColorBrush(Color.FromRgb(78, 201, 120)), New SolidColorBrush(Color.FromRgb(232, 91, 91)))
        })
        Dim DisplayName = GetOmniMixInstanceDisplayName(Instance)
        LabOmniMixInstanceMenu.Inlines.Add(New Run(" " & DisplayName & " ▾"))
        LabOmniMixInstanceMenu.ToolTip = DisplayName & If(Instance.Attached, "（在线）", "（离线）")
    End Sub

    Private Shared Function GetOmniMixGameAbbreviation(DisplayName As String) As String
        If String.IsNullOrWhiteSpace(DisplayName) Then Return "GAME"
        Dim Matches = Text.RegularExpressions.Regex.Matches(DisplayName, "[A-Za-z]+|\d+")
        If Matches.Count > 0 Then
            Dim Result As New Text.StringBuilder
            For Each Match As Text.RegularExpressions.Match In Matches
                Result.Append(If(Char.IsDigit(Match.Value(0)), Match.Value, Match.Value(0).ToString().ToUpperInvariant()))
            Next
            If Result.Length > 0 Then Return Result.ToString()
        End If
        Return New String(DisplayName.Where(Function(Character) Not Char.IsWhiteSpace(Character)).Take(3).ToArray())
    End Function

    Private Sub PanOmniMixInstanceMenu_MouseLeftButtonUp(sender As Object, e As MouseButtonEventArgs)
        If PanOmniMixInstanceMenu.ContextMenu Is Nothing OrElse PanOmniMixInstanceMenu.ContextMenu.Items.Count = 0 Then Return
        PanOmniMixInstanceMenu.ContextMenu.PlacementTarget = PanOmniMixInstanceMenu
        PanOmniMixInstanceMenu.ContextMenu.IsOpen = True
        e.Handled = True
    End Sub

    Private Async Sub OmniMixInstanceMenuItem_Click(sender As Object, e As RoutedEventArgs)
        Dim Item = TryCast(sender, MenuItem)
        Dim Instance = If(Item Is Nothing, Nothing, TryCast(Item.Tag, OmniMixPlaybackInstanceInfo))
        If Instance Is Nothing OrElse String.IsNullOrWhiteSpace(OmniMixBaseUrl) OrElse String.IsNullOrWhiteSpace(Instance.Id) Then Return
        Try
            Await OmniMixApiClient.SetActiveInstanceAsync(OmniMixBaseUrl, Instance.Id)
            UpdateOmniMixInstanceMenuLabel(Instance)
            For Each MenuItem In PanOmniMixInstanceMenu.ContextMenu.Items.OfType(Of MenuItem)
                Dim MenuInstance = TryCast(MenuItem.Tag, OmniMixPlaybackInstanceInfo)
                MenuItem.IsChecked = MenuInstance IsNot Nothing AndAlso String.Equals(MenuInstance.Id, Instance.Id, StringComparison.OrdinalIgnoreCase)
            Next
        Catch Ex As Exception
            Hint("切换运行实例失败：" & Ex.Message, HintType.Red)
        End Try
    End Sub

    Private Sub FormMain_KeyDown(sender As Object, e As KeyEventArgs) Handles Me.KeyDown
        If e.IsRepeat Then Return
        If e.SystemKey = Key.LeftAlt OrElse e.SystemKey = Key.RightAlt Then e.Handled = True
        If PanMsg.Children.Any Then
            If e.Key = Key.Enter Then
                CType(PanMsg.Children(0), Object).Btn1_Click()
            ElseIf e.Key = Key.Escape Then
                Dim Msg As Object = PanMsg.Children(0)
                If TypeOf Msg IsNot MyMsgInput AndAlso TypeOf Msg IsNot MyMsgSelect AndAlso Msg.Btn3.Visibility = Visibility.Visible Then
                    Msg.Btn3_Click()
                ElseIf Msg.Btn2.Visibility = Visibility.Visible Then
                    Msg.Btn2_Click()
                Else
                    Msg.Btn1_Click()
                End If
            End If
            Return
        End If
        If e.Key = Key.Escape Then PageBack()
        If e.Key = Key.F5 Then
            If TypeOf PageLeft Is IRefreshable Then CType(PageLeft, IRefreshable).Refresh()
            If TypeOf PageRight Is IRefreshable Then CType(PageRight, IRefreshable).Refresh()
        End If
    End Sub

    Private Sub FormMain_MouseDown(sender As Object, e As MouseButtonEventArgs) Handles Me.MouseDown
        If FrmMain.PanMsg.Children.Count > 0 OrElse WaitingMyMsgBox.Any Then Return
        If e.ChangedButton = MouseButton.XButton1 OrElse e.ChangedButton = MouseButton.XButton2 Then PageBack()
    End Sub

    Private Sub HandleDrag(sender As Object, e As DragEventArgs)
        e.Handled = True
        e.Effects = If(e.Data.GetDataPresent(DataFormats.FileDrop), DragDropEffects.Copy, DragDropEffects.None)
    End Sub

    Private Sub FrmMain_Drop(sender As Object, e As DragEventArgs) Handles Me.Drop
        If Not e.Data.GetDataPresent(DataFormats.FileDrop) Then Return
        Dim FilePathRaw = e.Data.GetData(DataFormats.FileDrop)
        If FilePathRaw Is Nothing Then Return
        FileDrag(CType(FilePathRaw, IEnumerable(Of String)))
    End Sub

    Private Sub FileDrag(FilePathList As IEnumerable(Of String))
        Dim FirstPath = FilePathList.FirstOrDefault()
        If String.IsNullOrWhiteSpace(FirstPath) Then Return
        Logger.Info("收到文件拖放：" & FirstPath)
        Hint("当前版本暂未为拖放文件绑定操作。")
    End Sub

    Private Function WndProc(hwnd As IntPtr, msg As Integer, wParam As IntPtr, lParam As IntPtr, ByRef handled As Boolean) As IntPtr
        Select Case msg
            Case 400 * 16 + 2
                If IsWindowLoadFinished Then
                    ShowWindowToTop()
                    handled = True
                End If
        End Select
        Return IntPtr.Zero
    End Function

    Private Function SendSystemMediaCommand(Command As String) As Boolean
        If FrmOmniMixLeft Is Nothing Then Return False
        Logger.Info("发送系统媒体传输控件 (SMTC) 命令：" & Command)
        FrmOmniMixLeft.HandleMediaCommand(Command)
        Return True
    End Function

    Private _Hidden As Boolean = False
    Public Property Hidden As Boolean
        Get
            Return _Hidden
        End Get
        Set(value As Boolean)
            If _Hidden = value Then Return
            _Hidden = value
            If value Then
                Left -= 10000
                ShowInTaskbar = False
                Visibility = Visibility.Hidden
                Logger.Info($"窗口已隐藏，位置：({Left},{Top})")
            Else
                If Left < -2000 Then Left += 10000
                ShowWindowToTop()
            End If
        End Set
    End Property

    Public Sub ShowWindowToTop()
        RunInUi(
        Sub()
            Visibility = Visibility.Visible
            ShowInTaskbar = True
            WindowState = WindowState.Normal
            Hidden = False
            Topmost = True
            Topmost = False
            SetForegroundWindow(Handle)
            Focus()
            Logger.Info($"窗口已置顶，位置：({Left}, {Top}), {Width} x {Height}")
        End Sub)
    End Sub

    Public Sub PageChange(Stack As PageStackData)
        If PageCurrent = Stack Then Return
        PageChangeActual(Stack)
    End Sub

    Private Sub BtnTitleSelect_Click(sender As MyRadioButton, raiseByMouse As Boolean) Handles BtnTitleSelect0.Check, BtnTitleSelect1.Check, BtnTitleSelect2.Check, BtnTitleSelect3.Check, BtnTitleSelect4.Check
        PageChangeActual(CType(Val(sender.Tag), PageType))
    End Sub

    Public Sub PageBack() Handles BtnTitleInner.Click
        PageChange(PageType.Launch)
    End Sub

    Private Sub PageChangeActual(Stack As PageStackData)
        AniControlEnabled += 1
        Try
            PageLast = PageCurrent
            PageCurrent = Stack
            PrepareOmniMixPagesForSwitch(Stack.Page)
            ConfigureOmniMixLeft(Stack.Page)
            Select Case Stack.Page
                Case PageType.Launch
                    PageChangeAnimOmniMix(FrmOmniMixHome)
                Case PageType.Download
                    PageChangeAnimOmniMix(FrmOmniMixLibrary)
                Case PageType.Link
                    PageChangeAnimOmniMix(FrmOmniMixModules)
                Case PageType.Setup
                    PageChangeAnimOmniMix(FrmOmniMixSettings)
                Case PageType.Other
                    PageChangeAnimOmniMix(FrmOmniMixAbout)
            End Select
            Logger.Info($"切换 OmniMix 页面：{Stack.Page}")
        Catch ex As Exception
            Logger.Error(ex, $"切换 OmniMix 页面失败（ID {PageCurrent.Page}）")
        Finally
            AniControlEnabled -= 1
        End Try
    End Sub

    Private Sub PrepareOmniMixPagesForSwitch(TargetPage As PageType)
        FrmOmniMixModules?.PrepareForOmniMixPageSwitch(TargetPage)
    End Sub

    Private Sub PageChangeAnimOmniMix(TargetRight As MyPageRight)
        If TargetRight Is Nothing Then Return
        If PanMainRight Is Nothing Then
            PageRight = TargetRight
            Return
        End If
        AniStop("FrmMain LeftChange")
        AniStop("PageLeft PageChange")
        AniControlEnabled += 1
        If TargetRight.Parent IsNot Nothing Then TargetRight.SetValue(ContentPresenter.ContentProperty, Nothing)
        PageRight = TargetRight
        Dim NewRight = TargetRight
        If PanMainRight.Child IsNot Nothing AndAlso TypeOf PanMainRight.Child Is MyPageRight Then
            CType(PanMainRight.Child, MyPageRight).PageOnExit()
        End If
        AniControlEnabled -= 1
        AniStart({
            AaCode(
            Sub()
                AniControlEnabled += 1
                If PanMainRight.Child IsNot Nothing AndAlso TypeOf PanMainRight.Child Is MyPageRight Then
                    CType(PanMainRight.Child, MyPageRight).PageOnForceExit()
                End If
                PanMainRight.Child = NewRight
                NewRight.Opacity = 0
                PanMainRight.Background = Nothing
                AniControlEnabled -= 1
                If BtnExtraBack IsNot Nothing Then RunInUi(Sub() BtnExtraBack.ShowRefresh(), True)
            End Sub, 110),
            AaCode(
            Sub()
                NewRight.Opacity = 1
                NewRight.PageOnEnter()
            End Sub, 30, True)
        }, "FrmMain PageChangeRight")
    End Sub

    Private Sub ConfigureOmniMixLeft(Page As PageType)
        If FrmOmniMixLeft Is Nothing Then Return
        Select Case Page
            Case PageType.Launch
                FrmOmniMixLeft.Configure(Page, FrmOmniMixHome)
            Case PageType.Download
                FrmOmniMixLeft.Configure(Page, FrmOmniMixLibrary)
            Case PageType.Link
                FrmOmniMixLeft.Configure(Page, FrmOmniMixModules)
            Case PageType.Setup
                FrmOmniMixLeft.Configure(Page, FrmOmniMixSettings)
            Case PageType.Other
                FrmOmniMixLeft.Configure(Page, FrmOmniMixAbout)
        End Select
    End Sub

    Private Sub PanMainLeft_SizeChanged(sender As Object, e As SizeChangedEventArgs) Handles PanMainLeft.SizeChanged
        If Not e.WidthChanged Then Return
        PanMainLeft_Resize(e.NewSize.Width)
    End Sub

    Private Sub PanMainLeft_Resize(NewWidth As Double)
        RectLeftBackground.Width = NewWidth
        RectLeftShadow.Opacity = If(NewWidth > 0, 1, 0)
        PanMainLeft.IsHitTestVisible = True
    End Sub

    Public Sub DragTick()
        If DragControl Is Nothing Then Return
        If Not Mouse.LeftButton = MouseButtonState.Pressed Then DragStop()
    End Sub

    Public Sub DragDoing() Handles PanBack.MouseMove
        If DragControl Is Nothing Then Return
        If Mouse.LeftButton = MouseButtonState.Pressed Then
            DragControl.DragDoing()
        Else
            DragStop()
        End If
    End Sub

    Public Sub DragStop()
        RunInUi(Sub()
                    If DragControl Is Nothing Then Return
                    Dim Control = DragControl
                    DragControl = Nothing
                    Control.DragStop()
                End Sub)
    End Sub

    Private Sub BtnExtraMusic_Click(sender As Object, e As EventArgs) Handles BtnExtraMusic.Click
        MusicControlPause()
    End Sub

    Private Sub BtnExtraMusic_RightClick(sender As Object, e As EventArgs) Handles BtnExtraMusic.RightClick
        MusicControlNext()
    End Sub

    Public Sub BackToTop() Handles BtnExtraBack.Click
        Dim RealScroll As MyScrollViewer = BtnExtraBack_GetRealChild()
        If RealScroll IsNot Nothing Then
            RealScroll.PerformVerticalOffsetDelta(-RealScroll.VerticalOffset)
        Else
            Logger.Error("无法返回顶部，未找到合适的 RealScroll", LogBehavior.Toast)
        End If
    End Sub

    Private Function BtnExtraBack_ShowCheck() As Boolean
        Dim RealScroll As MyScrollViewer = BtnExtraBack_GetRealChild()
        Return RealScroll IsNot Nothing AndAlso RealScroll.Visibility = Visibility.Visible AndAlso RealScroll.VerticalOffset > Height + If(BtnExtraBack.Show, 0, 700)
    End Function

    Private Function BtnExtraBack_GetRealChild() As MyScrollViewer
        If PanMainRight.Child Is Nothing OrElse TypeOf PanMainRight.Child IsNot MyPageRight Then Return Nothing
        Dim Page As MyPageRight = PanMainRight.Child
        Return Page.FindName(Page.PanScroll)
    End Function

    Private Sub CheckAndPromptGameIntegrationUpdate()
        Try
            Dim GameId = "forza_horizon_6"
            Dim Game = OmniMixModDeploymentService.GetGame(GameId)
            Dim ModInfo = OmniMixModDeploymentService.GetPrimaryMod(Game)
            If Game Is Nothing OrElse ModInfo Is Nothing Then
                Logger.Info("[FH6-Update] 整合配置或 Mod 信息为空，跳过检查")
                Return
            End If

            Dim GamePath = OmniMixModDeploymentService.LoadGamePath(GameId)
            Logger.Info($"[FH6-Update] 已配置的游戏路径: '{GamePath}'")
            If String.IsNullOrWhiteSpace(GamePath) Then Return

            Dim IsValidPath = OmniMixModDeploymentService.VerifyGameDirectory(GamePath, Game)
            Logger.Info($"[FH6-Update] 游戏目录有效性验证结果: {IsValidPath}")
            If Not IsValidPath Then Return

            Dim ModStatus = OmniMixModDeploymentService.CheckModStatus(GamePath, ModInfo)
            
            Dim MarkerDir = OmniMixModDeploymentService.ResolveGameMarkerDir(GamePath, ModInfo)
            Dim MarkerPath = System.IO.Path.Combine(MarkerDir, ".omnimix_mods", ModInfo.Id & ".managed")
            Dim InstalledVersion = "未发现标记"
            If System.IO.File.Exists(MarkerPath) Then
                InstalledVersion = System.IO.File.ReadAllText(MarkerPath).Trim()
            End If
            
            Logger.Info($"[FH6-Update] 集成状态: {ModStatus}, 已安装标记版本: {InstalledVersion}, 本地随附最新版本: {ModInfo.Version}")

            If ModStatus = OmniMixModInstallStatus.NeedsUpdate Then
                RunInUi(Sub()
                            Dim Title = "集成更新提示"
                            Dim MsgText = $"检测到您的游戏【{Game.Name}】的集成 Mod 有可用更新。{vbCrLf}更新可优化控制体验，是否现在进行一键更新？"
                            If MyMsgBox(MsgText, Title, "立刻更新", "暂不更新") = 1 Then
                                Task.Run(
                                    Async Sub()
                                        Try
                                            Dim Processes = System.Diagnostics.Process.GetProcessesByName("forzahorizon6")
                                            If Processes.Length > 0 Then
                                                RunInUi(Sub() Hint("游戏正在运行，请先关闭游戏后再更新集成。", HintType.Red))
                                                Exit Sub
                                            End If

                                            Dim Logs As New List(Of String)
                                            Dim BackendPort = PageOmniMixRight.GetPortFromBaseUrl(OmniMixBaseUrl, 17890)
                                            Dim InstanceId = Await Task.Run(Function() OmniMixModDeploymentService.DeployMod(GamePath, ModInfo, BackendPort, Logs))
                                            
                                            If String.IsNullOrWhiteSpace(InstanceId) Then
                                                RunInUi(Sub() Hint("一键更新失败，请在[游戏集成]页手动重试。", HintType.Red))
                                            Else
                                                Dim SavedVal = Settings.Get(Of String)("OmniMixFh6RaceStartPlayback")
                                                If String.IsNullOrWhiteSpace(SavedVal) Then SavedVal = "ignore"
                                                OmniMixModDeploymentService.WriteFh6RaceStartPlaybackConfig(GamePath, SavedVal)
                                                
                                                RunInUi(Sub() Hint("地平线 6 集成已一键更新至最新版本！", HintType.Green))
                                            End If
                                        Catch Ex As Exception
                                            RunInUi(Sub() Hint("更新发生异常：" & Ex.Message, HintType.Red))
                                        End Try
                                    End Sub)
                            End If
                        End Sub)
            End If
        Catch Ex As Exception
            Logger.Error(Ex, "[FH6-Update] 异步检查时发生异常")
        End Try
    End Sub

    Public Sub ApplyOmniMixFloatingPlaybackWindowSettings()
        If IsProgramEnding Then Return
        RunInUi(Sub()
                    Dim Visible = Settings.Get(Of Boolean)("OmniMixFloatingWindowVisible")
                    If Visible Then
                        ShowOmniMixFloatingPlaybackWindow(True)
                    Else
                        CloseOmniMixFloatingPlaybackWindow()
                    End If
                End Sub)
    End Sub

End Class
