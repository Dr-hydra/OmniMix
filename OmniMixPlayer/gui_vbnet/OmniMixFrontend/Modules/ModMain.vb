Imports System.Windows.Interop
Imports System.Windows.Threading

Public Module ModMain

#Region "弹出提示"

    ''' <summary>
    ''' 提示信息的种类。
    ''' 该枚举在自定义事件中使用，是公开 API 的一部分。
    ''' </summary>
    Public Enum HintType
        Blue
        Green
        Red
    End Enum
    Private Structure HintMessage
        Public Text As String
        Public Type As HintType
        Public Log As Boolean
    End Structure

    ''' <summary>
    ''' 等待弹出的提示列表。以 {String, HintType, Log As Boolean} 形式存储为数组。
    ''' </summary>
    Private HintWaiting As ConcurrentList(Of HintMessage) = If(HintWaiting, New ConcurrentList(Of HintMessage))
    ''' <summary>
    ''' 在窗口左下角弹出提示文本。
    ''' </summary>
    Public Sub Hint(Text As String, Optional Type As HintType = HintType.Blue, Optional Log As Boolean = True)
        If HintWaiting Is Nothing Then HintWaiting = New ConcurrentList(Of HintMessage)
        HintWaiting.Add(New HintMessage With {.Text = If(Text, ""), .Type = Type, .Log = Log})
    End Sub

    Private Sub HintTick()
        Try

            'Tag 存储了：{ 是否可以重用, Uuid }
            If Not HintWaiting.Any() Then Return
            Do While HintWaiting.Any
                ''清除空提示
                'If IsNothing(HintWaiting(0)) OrElse IsNothing(HintWaiting(0)(0)) Then
                '    HintWaiting.RemoveAt(0)
                '    Continue Do
                'End If
                Dim CurrentHint = HintWaiting(0)
                '去回车
                CurrentHint.Text = CurrentHint.Text.ReplaceLineEndings(" ", mergeMultiple:=True)
                '超量提示直接忽略
                If FrmMain.PanHint.Children.Count >= 20 Then GoTo EndHint
                '检查是否有重复提示
                Dim DoubleStack As Border = Nothing
                For Each stack As Border In FrmMain.PanHint.Children
                    If stack.Tag(0) AndAlso CType(stack.Child, TextBlock).Text = CurrentHint.Text Then DoubleStack = stack
                Next
                '获取渐变颜色
                Dim TargetColor0, TargetColor1 As MyColor
                Dim Percent As Double = 0.3
                Select Case CurrentHint.Type
                    Case HintType.Blue
                        TargetColor0 = New MyColor(215, 37, 155, 252)
                        TargetColor1 = New MyColor(215, 10, 142, 252)
                    Case HintType.Green
                        TargetColor0 = New MyColor(215, 33, 177, 33)
                        TargetColor1 = New MyColor(215, 29, 160, 29)
                    Case Else 'HintType.Red
                        TargetColor0 = New MyColor(215, 255, 53, 11)
                        TargetColor1 = New MyColor(215, 255, 43, 0)
                End Select
                If Not IsNothing(DoubleStack) Then
                    '有重复提示，且该提示的进入动画已播放
                    If Not AniIsRun("Hint Show " & DoubleStack.Tag(1)) Then
                        AniStop("Hint Hide " & DoubleStack.Tag(1))
                        Dim Delay As Double = (800 + CurrentHint.Text.Length.Clamp(5, 23) * 180) * AniSpeed
                        AniStart({
                            AaX(DoubleStack, -12 - DoubleStack.Margin.Left, 50,, New AniEaseOutFluent),
                            AaX(DoubleStack, -8, 50, 50, New AniEaseInFluent),
                            AaX(DoubleStack, 8, 50, 100, New AniEaseOutFluent),
                            AaX(DoubleStack, -8, 50, 150, New AniEaseInFluent),
                            AaDouble(Sub(i)
                                         Percent += i
                                         Dim Gradient As LinearGradientBrush = DoubleStack.Background
                                         Gradient.GradientStops(0).Color = TargetColor0 * Percent + New MyColor(255, 255, 255) * (1 - Percent)
                                         Gradient.GradientStops(1).Color = TargetColor1 * Percent + New MyColor(255, 255, 255) * (1 - Percent)
                                     End Sub, 0.7, 250),
                            AaX(DoubleStack, -50, 200, Delay, New AniEaseInFluent),
                            AaOpacity(DoubleStack, -1, 150, Delay),
                            AaCode(Sub() DoubleStack.Tag(0) = False, Delay),
                            AaHeight(DoubleStack, -26, 100,, New AniEaseOutFluent, True),
                            AaCode(Sub() FrmMain.PanHint.Children.Remove(DoubleStack), , True)
                      }, "Hint Hide " & DoubleStack.Tag(1))
                    End If
                Else
                    '准备控件
                    Dim NewHintControl As New Border With {.Tag = {True, GetUuid()}, .Margin = New Thickness(-70, 0, 20, 0), .Opacity = 0, .Height = 0, .HorizontalAlignment = HorizontalAlignment.Left, .CornerRadius = New CornerRadius(0, 6, 6, 0)}
                    NewHintControl.Background = New LinearGradientBrush(New GradientStopCollection(New List(Of GradientStop) From {
                        New GradientStop(TargetColor0 * Percent + New MyColor(255, 255, 255) * (1 - Percent), 0),
                        New GradientStop(TargetColor1 * Percent + New MyColor(255, 255, 255) * (1 - Percent), 1)}), 90)
                    NewHintControl.Child = New TextBlock With {.TextTrimming = TextTrimming.CharacterEllipsis, .FontSize = 13, .Text = CurrentHint.Text, .Foreground = New MyColor(255, 255, 255), .Margin = New Thickness(33, 5, 8, 5)}
                    'AddHandler NewHintControl.MouseLeftButtonDown, AddressOf HideAllHint
                    FrmMain.PanHint.Children.Add(NewHintControl)
                    '控件动画
                    Dim Animations As New List(Of AniData)
                    If FrmMain.PanHint.Children.Count > 1 Then
                        '已有提示
                        Animations.Add(AaHeight(NewHintControl, 26, 150, , New AniEaseOutFluent))
                    Else
                        '是唯一提示
                        NewHintControl.Height = 26
                    End If
                    '开始动画
                    Animations.AddRange({
                        AaX(NewHintControl, 30, 400, , New AniEaseOutElastic(AniEasePower.Weak)),
                        AaX(NewHintControl, 20, 200, , New AniEaseOutFluent),
                        AaOpacity(NewHintControl, 1, 100),
                        AaDouble(Sub(i)
                                     Percent += i
                                     Dim Gradient As LinearGradientBrush = NewHintControl.Background
                                     Gradient.GradientStops(0).Color = TargetColor0 * Percent + New MyColor(255, 255, 255) * (1 - Percent)
                                     Gradient.GradientStops(1).Color = TargetColor1 * Percent + New MyColor(255, 255, 255) * (1 - Percent)
                                 End Sub, 0.7, 250, 100)
                    })
                    AniStart(Animations, "Hint Show " & NewHintControl.Tag(1))
                    '结束动画
                    Dim Delay As Double = (800 + CurrentHint.Text.Length.Clamp(5, 23) * 180) * AniSpeed
                    AniStart({
                        AaX(NewHintControl, -50, 200, Delay, New AniEaseInFluent),
                        AaOpacity(NewHintControl, -1, 150, Delay),
                        AaCode(Sub() NewHintControl.Tag(0) = False, Delay),
                        AaHeight(NewHintControl, -26, 100,, New AniEaseOutFluent, True),
                        AaCode(Sub() FrmMain.PanHint.Children.Remove(NewHintControl), , True)
                    }, "Hint Hide " & NewHintControl.Tag(1))
                End If
                '结束处理
EndHint:
                If CurrentHint.Log Then Logger.Info($"弹出提示：{CurrentHint.Text}")
                HintWaiting.RemoveAt(0)
            Loop
        Catch ex As Exception
            Logger.Info(ex, "显示弹出提示失败")
        End Try
    End Sub
    Private Sub HideAllHint()
        For Each Control As Border In FrmMain.PanHint.Children
            Control.IsHitTestVisible = False
            AniStart({
                AaX(Control, -50, 200, , New AniEaseInFluent),
                AaOpacity(Control, -1, 150, , New AniEaseInFluent),
                AaCode(Sub() Control.Tag(0) = False),
                AaHeight(Control, -26, 100,, New AniEaseOutFluent, True),
                AaCode(Sub() FrmMain.PanHint.Children.Remove(Control), , True)
            }, "Hint Hide " & Control.Tag(1))
        Next
    End Sub

#End Region

#Region "弹窗"

    ''' <summary>
    ''' 存储弹窗信息的转换器。
    ''' </summary>
    Public Class MyMsgBoxConverter
        Public Type As MyMsgBoxType
        Public Title As String
        Public Text As String
        ''' <summary>
        ''' 输入模式：文本框的文本。
        ''' 选择模式：需要放进去的 IEnumberable(Of IMyRadio)。
        ''' 登录模式：登录步骤 1 中返回的 JSON。
        ''' </summary>
        Public Content As Object
        ''' <summary>
        ''' 输入模式：输入验证规则。
        ''' </summary>
        Public ValidateRules As ObjectModel.Collection(Of Validate)
        ''' <summary>
        ''' 输入模式：提示文本。
        ''' </summary>
        Public HintText As String = ""
        ''' <summary>
        ''' 有多个按钮时，是否给第一个按钮加高亮。
        ''' </summary>
        Public HighLight As Boolean
        Public Button1 As String = "确定"
        Public Button2 As String = ""
        Public Button3 As String = ""
        ''' <summary>
        ''' 点击第一个按钮将执行该方法，不关闭弹窗。
        ''' </summary>
        Public Button1Action As Action = Nothing
        ''' <summary>
        ''' 点击第二个按钮将执行该方法，不关闭弹窗。
        ''' </summary>
        Public Button2Action As Action = Nothing
        ''' <summary>
        ''' 点击第三个按钮将执行该方法，不关闭弹窗。
        ''' </summary>
        Public Button3Action As Action = Nothing
        Public IsWarn As Boolean = False
        Public ForceWait As Boolean = False
        Public WaitFrame As New DispatcherFrame(True)
        ''' <summary>
        ''' 弹窗是否已经关闭。
        ''' </summary>
        Public IsExited As Boolean = False
        ''' <summary>
        ''' 输入模式：输入的文本。若点击了 非 第一个按钮，则为 Nothing。
        ''' 选择模式：点击的按钮编号，从 1 开始。
        ''' 登录模式：字符串数组 {AccessToken, RefreshToken} 或一个 Exception。
        ''' </summary>
        Public Result As Object
    End Class
    Public Enum MyMsgBoxType
        Text
        [Select]
        Input
        Login
    End Enum

    ''' <summary>
    ''' 显示弹窗，返回点击按钮的编号（从 1 开始）。
    ''' </summary>
    ''' <param name="Title">弹窗的标题。</param>
    ''' <param name="Caption">弹窗的内容。</param>
    ''' <param name="Button1">显示的第一个按钮，默认为“确定”。</param>
    ''' <param name="Button2">显示的第二个按钮，默认为空。</param>
    ''' <param name="Button3">显示的第三个按钮，默认为空。</param>
    ''' <param name="Button1Action">点击第一个按钮将执行该方法，不关闭弹窗。</param>
    ''' <param name="Button2Action">点击第二个按钮将执行该方法，不关闭弹窗。</param>
    ''' <param name="Button3Action">点击第三个按钮将执行该方法，不关闭弹窗。</param>
    ''' <param name="IsWarn">是否为警告弹窗，若为 True，弹窗配色和背景会变为红色。</param>
    Public Function MyMsgBox(Caption As String, Optional Title As String = "提示",
                             Optional Button1 As String = "确定", Optional Button2 As String = "", Optional Button3 As String = "",
                             Optional IsWarn As Boolean = False, Optional HighLight As Boolean = True, Optional ForceWait As Boolean = False,
                             Optional Button1Action As Action = Nothing, Optional Button2Action As Action = Nothing, Optional Button3Action As Action = Nothing) As Integer
        '将弹窗列入队列
        Dim Converter As New MyMsgBoxConverter With {.Type = MyMsgBoxType.Text, .Button1 = Button1, .Button2 = Button2, .Button3 = Button3, .Text = Caption, .IsWarn = IsWarn, .Title = Title, .HighLight = HighLight, .ForceWait = True, .Button1Action = Button1Action, .Button2Action = Button2Action, .Button3Action = Button3Action}
        WaitingMyMsgBox.Add(Converter)
        If Button2.Length > 0 OrElse ForceWait Then
            '若有多个按钮则开始等待
            If FrmMain Is Nothing OrElse FrmMain.PanMsg Is Nothing AndAlso RunInUi() Then
                '主窗体尚未加载，用老土的弹窗来替代
                WaitingMyMsgBox.Remove(Converter)
                If Button2.Length > 0 Then
                    Dim RawResult As MsgBoxResult = MsgBox(Caption, If(Button3.Length > 0, MsgBoxStyle.YesNoCancel, MsgBoxStyle.YesNo) + If(IsWarn, MsgBoxStyle.Critical, MsgBoxStyle.Question), Title)
                    Select Case RawResult
                        Case MsgBoxResult.Yes
                            Converter.Result = 1
                        Case MsgBoxResult.No
                            Converter.Result = 2
                        Case MsgBoxResult.Cancel
                            Converter.Result = 3
                    End Select
                Else
                    MsgBox(Caption, MsgBoxStyle.OkOnly + If(IsWarn, MsgBoxStyle.Critical, MsgBoxStyle.Question), Title)
                    Converter.Result = 1
                End If
                Logger.Warn($"主窗体加载完成前出现意料外的等待弹窗：{Button1},{Button2},{Button3}")
            Else
                Try
                    FrmMain.DragStop()
                    If RunInUi() Then MyMsgBoxTick()
                    ComponentDispatcher.PushModal()
                    Dispatcher.PushFrame(Converter.WaitFrame)
                Finally
                    ComponentDispatcher.PopModal()
                End Try
            End If
            Logger.Info($"普通弹框返回：{If(Converter.Result, "null")}")
            Return Converter.Result
        Else
            '不进行等待，直接返回
            Return 1
        End If
    End Function
    ''' <summary>
    ''' 显示输入框并返回输入的文本。若点击第二个按钮，则返回 Nothing。
    ''' </summary>
    ''' <param name="Title">弹窗的标题。</param>
    ''' <param name="ValidateRules">文本框的输入检测。</param>
    ''' <param name="Text">弹窗的介绍文本。</param>
    ''' <param name="DefaultInput">文本框的默认内容。</param>
    ''' <param name="HintText">文本框的提示内容。</param>
    ''' <param name="Button1">显示的第一个按钮，默认为“确定”。</param>
    ''' <param name="Button2">显示的第二个按钮，默认为“取消”。</param>
    ''' <param name="IsWarn">是否为警告弹窗，若为 True，弹窗配色和背景会变为红色。</param>
    Public Function MyMsgBoxInput(Title As String, Optional Text As String = Nothing, Optional DefaultInput As String = Nothing, Optional ValidateRules As ObjectModel.Collection(Of Validate) = Nothing, Optional HintText As String = "", Optional Button1 As String = "确定", Optional Button2 As String = "取消", Optional IsWarn As Boolean = False) As String
        '将弹窗列入队列
        Dim Converter As New MyMsgBoxConverter With {.Text = If(Text, ""), .HintText = If(HintText, ""), .Type = MyMsgBoxType.Input, .ValidateRules = If(ValidateRules, New ObjectModel.Collection(Of Validate)), .Button1 = Button1, .Button2 = Button2, .Content = DefaultInput, .IsWarn = IsWarn, .Title = Title}
        WaitingMyMsgBox.Add(Converter)
        '虽然我也不知道这是啥但是能用就成了 :)
        Try
            If FrmMain IsNot Nothing Then FrmMain.DragStop()
            If RunInUi() Then MyMsgBoxTick()
            ComponentDispatcher.PushModal()
            Dispatcher.PushFrame(Converter.WaitFrame)
        Finally
            ComponentDispatcher.PopModal()
        End Try
        Logger.Info($"输入弹框返回：{If(Converter.Result, "null")}")
        Return Converter.Result
    End Function
    ''' <summary>
    ''' 显示选择框并返回选择的第几项（从 0 开始）。若点击第二个按钮，则返回 Nothing。
    ''' </summary>
    ''' <param name="Title">弹窗的标题。</param>
    ''' <param name="Button1">显示的第一个按钮，默认为 “确定”。</param>
    ''' <param name="Button2">显示的第二个按钮，默认为空。</param>
    ''' <param name="IsWarn">是否为警告弹窗，若为 True，弹窗配色和背景会变为红色。</param>
    Public Function MyMsgBoxSelect(Selections As IEnumerable(Of IMyRadio), Optional Title As String = "提示", Optional Button1 As String = "确定", Optional Button2 As String = "", Optional IsWarn As Boolean = False) As Integer?
        '将弹窗列入队列
        Dim Converter As New MyMsgBoxConverter With {.Type = MyMsgBoxType.Select, .Button1 = Button1, .Button2 = Button2, .Content = Selections, .IsWarn = IsWarn, .Title = Title}
        WaitingMyMsgBox.Add(Converter)
        '虽然我也不知道这是啥但是能用就成了 :)
        Try
            If FrmMain IsNot Nothing Then FrmMain.DragStop()
            If RunInUi() Then MyMsgBoxTick()
            ComponentDispatcher.PushModal()
            Dispatcher.PushFrame(Converter.WaitFrame)
        Finally
            ComponentDispatcher.PopModal()
        End Try
        Logger.Info($"选择弹框返回：{If(Converter.Result, "null")}")
        Return Converter.Result
    End Function

    ''' <summary>
    ''' 等待显示的弹窗。
    ''' </summary>
    Public WaitingMyMsgBox As List(Of MyMsgBoxConverter) = If(WaitingMyMsgBox, New List(Of MyMsgBoxConverter))
    Public Sub MyMsgBoxTick()
        Try
            If FrmMain Is Nothing OrElse FrmMain.PanMsg Is Nothing OrElse FrmMain.WindowState = WindowState.Minimized Then Return
            If FrmMain.PanMsg.Children.Count > 0 Then
                '弹窗中
                FrmMain.PanMsg.Visibility = Visibility.Visible
            ElseIf WaitingMyMsgBox.Any Then
                '没有弹窗，显示一个等待的弹窗
                FrmMain.PanMsg.Visibility = Visibility.Visible
                Select Case WaitingMyMsgBox(0).Type
                    Case MyMsgBoxType.Input
                        FrmMain.PanMsg.Children.Add(New MyMsgInput(WaitingMyMsgBox(0)))
                    Case MyMsgBoxType.Select
                        FrmMain.PanMsg.Children.Add(New MyMsgSelect(WaitingMyMsgBox(0)))
                    Case MyMsgBoxType.Text
                        FrmMain.PanMsg.Children.Add(New MyMsgText(WaitingMyMsgBox(0)))
                    Case MyMsgBoxType.Login
                        WaitingMyMsgBox(0).Type = MyMsgBoxType.Text
                        FrmMain.PanMsg.Children.Add(New MyMsgText(WaitingMyMsgBox(0)))
                End Select
                WaitingMyMsgBox.RemoveAt(0)
            Else
                '没有弹窗，没有等待的弹窗
                If FrmMain.PanMsg.Visibility <> Visibility.Collapsed Then FrmMain.PanMsg.Visibility = Visibility.Collapsed
            End If
        Catch ex As Exception
            Logger.Error(ex, "处理等待中的弹窗失败")
        End Try
    End Sub

#End Region

#Region "页面声明"
    '在最后进行页面声明，避免颜色尚未加载完毕

    '窗体声明
    Public FrmMain As FormMain
    Public FrmStart As SplashScreen

#End Region

#Region "愚人节"

    Private _IsAprilEnabled As Boolean? = Nothing
    Public ReadOnly Property IsAprilEnabled As Boolean
        Get
            Return False
        End Get
    End Property
    Public IsAprilGiveup As Boolean = False
    Private AprilDifficultiedDistance As Integer = 0
    Private Sub TimerFool()
        '旧启动页彩蛋已移除。
    End Sub

#End Region

#Region "系统"

    ''' <summary>
    ''' 把某个 OmniMix 窗口拖到最前面。
    ''' </summary>
    Public Sub ShowWindowToTop(Handle As IntPtr)
        Try
            PostMessage(Handle, 400 * 16 + 2, 0, 0)
            SetForegroundWindow(Handle) '不在这里放不行，神秘 WinAPI，建议别动
        Catch ex As Exception
            Logger.Error(ex, "设置窗口置顶失败", LogBehavior.Toast)
        End Try
    End Sub
    Public Declare Function FindWindow Lib "user32" Alias "FindWindowA" (ClassName As String, WindowName As String) As IntPtr
    Public Declare Function SetForegroundWindow Lib "user32" (hWnd As IntPtr) As Integer
    Private Declare Function PostMessage Lib "user32" Alias "PostMessageA" (hWnd As IntPtr, msg As UInteger, wParam As Long, lParam As Long) As Boolean

    ''' <summary>
    ''' 将特定程序设置为使用高性能显卡启动。
    ''' 如果失败，则抛出异常。
    ''' </summary>
    Public Sub SetGPUPreference(Executeable As String)
        Const REG_KEY As String = "Software\Microsoft\DirectX\UserGpuPreferences"
        Const REG_VALUE As String = "GpuPreference=2;"
        '查看现有设置
        Using ReadOnlyKey = My.Computer.Registry.CurrentUser.OpenSubKey(REG_KEY, False)
            If ReadOnlyKey IsNot Nothing Then
                If REG_VALUE = ReadOnlyKey.GetValue(Executeable)?.ToString() Then
                    Logger.Info($"无需调整显卡设置：{Executeable}")
                    Return
                End If
            Else
                '创建父级键
                Logger.Info($"需要创建显卡设置的父级键")
                My.Computer.Registry.CurrentUser.CreateSubKey(REG_KEY)
            End If
        End Using
        '写入新设置
        Using WriteKey = My.Computer.Registry.CurrentUser.OpenSubKey(REG_KEY, True)
            WriteKey.SetValue(Executeable, REG_VALUE)
            Logger.Info($"已调整显卡设置：{Executeable}")
        End Using
    End Sub

    ''' <summary>
    ''' 对替换标记进行处理。会对替换内容使用 EscapeHandler 进行转义。
    ''' </summary>
    Public Function ArgumentReplace(Text As String, Optional EscapeHandler As Func(Of String, String) = Nothing, Optional ReplaceTime As Boolean = True) As String
        If Text Is Nothing OrElse Not Text.Contains("{") Then Return Text
        '预处理（注意，文件夹必须以 \ 结尾）
        Static Replacer As Func(Of String, String) =
        Function(s As String) As String
            If s Is Nothing Then Return ""
            If EscapeHandler Is Nothing Then Return s
            If s.Contains(":\") Then
                Dim IsFolder = s.EndsWithF("\") OrElse s.EndsWithF("/")
                s = PathUtils.ToShortPath(s)
                If IsFolder Then s = PathUtils.AddSlashSuffix(s)
            End If
            Return EscapeHandler(s)
        End Function
        '基础
        Text = Text.Replace("{pcl_version}", Replacer(VersionBaseName))
        Text = Text.Replace("{pcl_version_code}", Replacer(VersionCode))
        Text = Text.Replace("{pcl_version_branch}", Replacer(BuildTypeDisplay))
        Text = Text.Replace("{pcl_build_type}", Replacer(BuildType.ToString))
        Text = Text.Replace("{pcl_branch}", Replacer(VersionBranchMain))
        Text = Text.Replace("{omnimix_version}", Replacer(VersionBaseName))
        Text = Text.Replace("{omnimix_version_code}", Replacer(VersionCode))
        Text = Text.Replace("{omnimix_build_type}", Replacer(BuildType.ToString))
        Text = Text.Replace("{identify}", Replacer(Identify))
        Text = Text.Replace("{path}", Replacer(PathUtils.CurrentFolder))
        Text = Text.Replace("{path_with_name}", Replacer(PathExe))
        Text = Text.Replace("{path_temp}", Replacer(PathTemp))
        Text = Text.Replace("{pcl_md5}", Function() Replacer(CryptographyUtils.ComputeFileHash(PathExe, CryptographyUtils.HashMethod.Md5)))
        Text = Text.Replace("{pcl_sha1}", Function() Replacer(CryptographyUtils.ComputeFileHash(PathExe, CryptographyUtils.HashMethod.Sha1)))
        Text = Text.Replace("{omnimix_md5}", Function() Replacer(CryptographyUtils.ComputeFileHash(PathExe, CryptographyUtils.HashMethod.Md5)))
        Text = Text.Replace("{omnimix_sha1}", Function() Replacer(CryptographyUtils.ComputeFileHash(PathExe, CryptographyUtils.HashMethod.Sha1)))
        '时间
        If ReplaceTime Then '在窗口标题中，时间会被后续动态替换，所以此时不应该替换
            Text = Text.Replace("{date}", Replacer(Date.Now.ToString("yyyy'/'M'/'d")))
            Text = Text.Replace("{time}", Replacer(Date.Now.ToString("HH':'mm':'ss")))
        End If
        '旧 Minecraft / 登录占位符在 OmniMix 前端中保留为空值，避免旧自定义内容崩溃。
        For Each Key In {"{java}", "{minecraft}", "{version_path}", "{verpath}", "{version_indie}", "{verindie}", "{name}", "{version}", "{user}", "{uuid}", "{login}"}
            Text = Text.Replace(Key, Replacer(Nothing))
        Next
        '高级
        Text = Text.RegexReplace("\{hint\}", Function() Replacer(""))
        Text = Text.RegexReplace("\{cave\}", Function() Replacer(""))
        Text = Text.RegexReplace("\{setup:([a-zA-Z0-9]+)\}", Function(m) Replacer(Settings.GetSafe(m.Groups(1).Value)))
        Text = Text.RegexReplace("\{varible:([^:\}]+)(?::([^\}]+))?\}", Function(m) Replacer(ReadReg("CustomEvent" & m.Groups(1).Value, m.Groups(2).Value)))
        Text = Text.RegexReplace("\{variable:([^:\}]+)(?::([^\}]+))?\}", Function(m) Replacer(ReadReg("CustomEvent" & m.Groups(1).Value, m.Groups(2).Value)))
        Return Text
    End Function

#End Region

#Region "任务缓存"

    Private IsTaskTempCleared As Boolean = False
    Private IsTaskTempClearing As Boolean = False

    ''' <summary>
    ''' 尝试清理任务缓存文件夹。
    ''' 在整次运行中只会实际清理一次。
    ''' </summary>
    Public Sub TryClearTaskTemp()
        If Not IsTaskTempCleared Then
            IsTaskTempCleared = True
            IsTaskTempClearing = True
            Try
                Logger.Info("开始清理任务缓存文件夹")
                DirectoryUtils.Delete($"{OsDrive}ProgramData\OmniMixPlayer\TaskTemp\")
                DirectoryUtils.Delete($"{PathTemp}TaskTemp\")
                Logger.Info("已清理任务缓存文件夹")
            Catch ex As Exception
                Logger.Warn(ex, "清理任务缓存文件夹失败")
            Finally
                IsTaskTempClearing = False
            End Try
        ElseIf IsTaskTempClearing Then
            '等待另一个清理步骤完成
            Do While IsTaskTempClearing
                Thread.Sleep(1)
            Loop
        End If
    End Sub

    ''' <summary>
    ''' 申请一个可用于任务缓存的临时文件夹，以 \ 结尾。这些文件夹无需进行后续清理。
    ''' 若所有缓存位置均没有权限，会抛出异常。
    ''' </summary>
    ''' <param name="RequireNonSpace">是否要求路径不包含空格。</param>
    Public Function RequestTaskTempFolder(Optional RequireNonSpace As Boolean = False) As String
        TryClearTaskTemp()
        Dim ResultFolder As String
        Try
            ResultFolder = $"{PathTemp}TaskTemp\{GetUuid()}-{RandomInteger(0, 1000000)}\"
            If RequireNonSpace AndAlso ResultFolder.Contains(" ") Then Exit Try '带空格
            DirectoryUtils.Create(ResultFolder)
            CheckPermissionWithException(ResultFolder)
            Return ResultFolder
        Catch
        End Try
        '使用备用路径
        ResultFolder = $"{OsDrive}ProgramData\OmniMixPlayer\TaskTemp\{GetUuid()}-{RandomInteger(0, 1000000)}\"
        DirectoryUtils.Create(ResultFolder)
        CheckPermission(ResultFolder)
        Return ResultFolder
    End Function

#End Region

#Region "反馈与遥测"

    '反馈
    Public Sub Feedback(Optional ShowMsgbox As Boolean = True, Optional ForceOpenLog As Boolean = False)
        On Error Resume Next
        FeedbackInfo()
        If ForceOpenLog OrElse (ShowMsgbox AndAlso MyMsgBox("若你在汇报一个 Bug，请上传 Log(1~5).txt 中包含错误信息的文件。" & vbCrLf &
                                                           "你也可以加入 QQ 群 851586605 进行反馈。", "反馈提交提醒", "打开文件夹", "不需要") = 1) Then
            OpenExplorer(PathExeFolder & "OmniMixPlayer\Log1.txt")
        End If
        OpenWebsite("https://github.com/Dr-hydra/OmniMix/issues/")
    End Sub
    ''' <summary>
    ''' 在日志中输出系统诊断信息。
    ''' </summary>
    Public Sub FeedbackInfo()
        On Error Resume Next
        Logger.Warn($"诊断信息：{vbCrLf}" &
            "操作系统：" & My.Computer.Info.OSFullName & "（32 位：" & Is32BitSystem & "）" & vbCrLf &
            "剩余内存：" & Int(My.Computer.Info.AvailablePhysicalMemory / 1024 / 1024) & " M / " & Int(My.Computer.Info.TotalPhysicalMemory / 1024 / 1024) & " M" & vbCrLf &
            "DPI：" & DPI & "（" & Math.Round(DPI / 96, 2) * 100 & "%）" & vbCrLf &
            "文件位置：" & PathExeFolder)
    End Sub

    '遥测
    Public Sub Telemetry([Event] As String, ParamArray Datas As String())
        If BuildType = BuildTypes.Debug Then Return '开发版不上传遥测
        If Not Settings.Get(Of Boolean)("SystemSystemTelemetry") Then Return '用户关闭了遥测
        If Not ClsBaseUrl.StartsWithF("http") Then Return '开源版没有设置遥测地址
        RunInNewThread(
        Sub()
            Try
                Logger.Info($"匿名数据上报：{[Event]}")
                Dim Url As String = $"{ClsBaseUrl}&Event={EscapeUtils.UrlEscape([Event])}"
                For i = 0 To Datas.Length - 1 Step 2
                    Url &= "&" & EscapeUtils.UrlEscape(Datas(i)) & "=" & EscapeUtils.UrlEscape(Datas(i + 1).ReplaceLineEndings(vbLf, mergeMultiple:=True))
                Next
                NetRequestByClient(Url, MakeLog:=False)
            Catch ex As Exception
                Logger.Warn(ex, "匿名数据上报失败")
            End Try
        End Sub, "Telemetry", ThreadPriority.Lowest)
    End Sub

#End Region

    Public DragControl = Nothing
    Private Timer4Count As Integer = 0
    Private Timer150Count As Integer = 0
    Private Sub TimerMain()
        Try
#Region "每 50ms 执行一次的代码"
            HintTick()
            MyMsgBoxTick()
            FrmMain.DragTick()
            LoaderTaskbarProgressRefresh()
            If ThemeDontClick = 2 Then ThemeRefresh()
#End Region
        Catch ex As Exception
            Logger.Error(ex, "短程主时钟执行异常", LogBehavior.AlertThenCrash)
        End Try
        Timer4Count += 1
        If Timer4Count = 4 Then
            Timer4Count = 0
            Try
#Region "每 250ms 执行一次的代码"
                If ThemeNow = 12 Then ThemeRefresh()
#End Region
            Catch ex As Exception
                Logger.Warn(ex, "中程主时钟执行异常")
            End Try
        End If
        Timer150Count += 1
        If Timer150Count = 150 Then
            Timer150Count = 0
            Try
#Region "每 7.5s 执行一次的代码"
                '以未知原因窗口被丢到一边去的修复（Top、Left = -25600），还有 #745
                RunInUi(
                Sub()
                    If Not FrmMain.Hidden Then
                        If FrmMain.Top < -9000 Then FrmMain.Top = 100
                        If FrmMain.Left < -9000 Then FrmMain.Left = 100 '窗口拉至最大时 Left = -18.8
                    End If
                End Sub)
#End Region
            Catch ex As Exception
                Logger.Error(ex, "长程主时钟执行异常", LogBehavior.AlertThenCrash)
            End Try
        End If
    End Sub
    Public Sub TimerMainStart()
        RunInNewThread(
        Sub()
            Try
                Do While True
                    RunInUiWait(AddressOf TimerMain)
                    Thread.Sleep(50 * 0.98)
                Loop
            Catch ex As Exception
                Logger.Error(ex, "程序主时钟出错")
            End Try
        End Sub, "Timer Main")
    End Sub

End Module

