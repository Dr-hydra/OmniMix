Public Class Settings

    Public Shared ReadOnly Entries As Dictionary(Of String, Setting) = (New List(Of Setting) From {
        New Setting("Identify", "", Source:=Sources.Registry),
        New Setting("WindowHeight", 650),
        New Setting("WindowWidth", 900),
        New Setting("SystemDebugMode", False, Source:=Sources.Registry, OnChanged:=Sub() Logger.Instance.MinLevel = If(ModeDebug, LogLevel.Trace, LogLevel.Warn)),
        New Setting("SystemDebugAnim", 9, Source:=Sources.Registry),
        New Setting("SystemDebugDelay", False, Source:=Sources.Registry),
        New Setting("SystemDebugSkipCopy", False, Source:=Sources.Registry),
        New Setting("SystemSystemCache", "", Source:=Sources.Registry),
        New Setting("OmniMixCacheMaxBytes", 2147483648L, Source:=Sources.Registry),
        New Setting("SystemSystemTelemetry", True, Source:=Sources.Registry),
        New Setting("ToolDownloadThread", 63, Source:=Sources.Registry),
        New Setting("ToolDownloadSpeed", 42, Source:=Sources.Registry, OnChanged:=AddressOf ModNet.UpdateNetTaskSpeedLimitHigh),
        New Setting("UiLauncherTransparent", 100, OnChanged:=Sub(Value As Integer) If FrmMain IsNot Nothing Then FrmMain.UpdateWindowOpacity()),
        New Setting("UiLanguage", "auto", Source:=Sources.Registry),
        New Setting("UiLauncherLogo", False),
        New Setting("UiLauncherTheme", 5, OnChanged:=AddressOf ThemeRefresh),
        New Setting("UiLauncherThemeHue", 210),
        New Setting("UiLauncherThemeSat", 85),
        New Setting("UiLauncherThemeLight", 20),
        New Setting("UiBackgroundColorful", False, OnChanged:=Sub() ThemeRefresh()),
        New Setting("UiControlOpacity", 80, OnChanged:=Sub(Value As Integer) If FrmMain IsNot Nothing Then FrmMain.UpdateControlOpacity()),
        New Setting("OmniMixFloatingWindowOpacity", 100, OnChanged:=Sub(Value As Integer) If FrmMain IsNot Nothing Then FrmMain.UpdateOmniMixFloatingPlaybackWindowAppearance()),
        New Setting("OmniMixFloatingWindowBackgroundOpacity", 88, OnChanged:=Sub(Value As Integer) If FrmMain IsNot Nothing Then FrmMain.UpdateOmniMixFloatingPlaybackWindowAppearance()),
        New Setting("OmniMixFloatingWindowScale", 100, OnChanged:=Sub(Value As Integer) If FrmMain IsNot Nothing Then FrmMain.UpdateOmniMixFloatingPlaybackWindowAppearance()),
        New Setting("OmniMixFloatingWindowVisible", True, OnChanged:=Sub(Value As Boolean) If FrmMain IsNot Nothing Then FrmMain.ApplyOmniMixFloatingPlaybackWindowSettings()),
        New Setting("OmniMixFloatingWindowLeft", 0.0),
        New Setting("OmniMixFloatingWindowTop", 0.0),
        New Setting("OmniMixFloatingWindowMonitor", ""),
        New Setting("OmniMixFloatingWindowStyle", 0, OnChanged:=Sub(Value As Integer) If FrmMain IsNot Nothing Then FrmMain.UpdateOmniMixFloatingPlaybackWindowAppearance()),
        New Setting("OmniMixFloatingWindowTheme", 15, OnChanged:=Sub(Value As Integer) If FrmMain IsNot Nothing Then FrmMain.UpdateOmniMixFloatingPlaybackWindowAppearance()),
        New Setting("OmniMixFloatingWindowThemeHue", 210, OnChanged:=Sub(Value As Integer) If FrmMain IsNot Nothing Then FrmMain.UpdateOmniMixFloatingPlaybackWindowAppearance()),
        New Setting("OmniMixFloatingWindowThemeSat", 85, OnChanged:=Sub(Value As Integer) If FrmMain IsNot Nothing Then FrmMain.UpdateOmniMixFloatingPlaybackWindowAppearance()),
        New Setting("OmniMixFloatingWindowThemeLight", 20, OnChanged:=Sub(Value As Integer) If FrmMain IsNot Nothing Then FrmMain.UpdateOmniMixFloatingPlaybackWindowAppearance()),
        New Setting("OmniMixFloatingWindowBackgroundTheme", 5, OnChanged:=Sub(Value As Integer) If FrmMain IsNot Nothing Then FrmMain.UpdateOmniMixFloatingPlaybackWindowAppearance()),
        New Setting("OmniMixFloatingWindowBackgroundThemeHue", 215, OnChanged:=Sub(Value As Integer) If FrmMain IsNot Nothing Then FrmMain.UpdateOmniMixFloatingPlaybackWindowAppearance()),
        New Setting("OmniMixFloatingWindowBackgroundThemeSat", 18, OnChanged:=Sub(Value As Integer) If FrmMain IsNot Nothing Then FrmMain.UpdateOmniMixFloatingPlaybackWindowAppearance()),
        New Setting("OmniMixFloatingWindowBackgroundThemeLight", 20, OnChanged:=Sub(Value As Integer) If FrmMain IsNot Nothing Then FrmMain.UpdateOmniMixFloatingPlaybackWindowAppearance()),
        New Setting("UiBackgroundOpacity", 100, OnChanged:=AddressOf FormMain.UpdateBackgroundAndTitleBar),
        New Setting("UiBackgroundBlur", 0, OnChanged:=AddressOf FormMain.UpdateBackgroundAndTitleBar),
        New Setting("UiBackgroundClarity", 100, OnChanged:=AddressOf FormMain.UpdateBackgroundAndTitleBar),
        New Setting("UiBackgroundImagePath", "__default__", OnChanged:=AddressOf FormMain.UpdateBackgroundAndTitleBar),
        New Setting("UiBackgroundSuit", 0, OnChanged:=AddressOf FormMain.UpdateBackgroundAndTitleBar),
        New Setting("UiMusicVolume", 500),
        New Setting("UiMusicStop", False),
        New Setting("UiMusicStart", False),
        New Setting("UiMusicRandom", True),
        New Setting("UiMusicAuto", True),
        New Setting("HintCustomCommand", False),
        New Setting("OmniMixBackendPath", ""),
        New Setting("OmniMixCloseBackendWithGui", True),
        New Setting("OmniMixDesktopPlayerEnabled", True),
        New Setting("OmniMixFh6RaceStartPlayback", "ignore"),
        New Setting("OmniMixFh6DjEnabled", False),
        New Setting("OmniMixFh6DjHost", 1),
        New Setting("OmniMixFh6DjScope", "fh6_instances"),
        New Setting("OmniMixFh6DjContent", "smart"),
        New Setting("OmniMixFh6DjFrequency", 1)
    }).ToDictionary(Function(e) e.Key)

    Public Enum Sources
        Normal
        Registry
    End Enum

    Public Class Setting
        Public Key As String
        Public Encrypted As Boolean
        Public DefaultValue As Object
        Public Source As Sources
        Public OnChanged As Action(Of Object)
        Public Type As Type
        Public ValueCache As Object = Nothing
        Public HasCache As Boolean = False

        Public Sub New(Key As String, Value As Object, Optional Source As Sources = Sources.Normal, Optional Encrypted As Boolean = False, Optional OnChanged As Action(Of Object) = Nothing)
            Me.Key = Key
            Me.DefaultValue = Value
            Me.Encrypted = Encrypted
            Me.Source = Source
            Me.Type = If(Value, "").GetType
            Me.OnChanged = OnChanged
        End Sub

        Public Sub Save()
            Dim Value As String = If(ValueCache, "").ToString()
            Logger.Trace($"保存设置：{Key} = {Value}")
            If Encrypted Then
                Try
                    Value = CryptographyUtils.DesEncrypt(Value, "OmniMix" & Identify)
                Catch ex As Exception
                    Logger.Warn(ex, $"加密设置失败：{Key}")
                End Try
            End If
            Select Case Source
                Case Sources.Normal
                    WriteIni("Setup", Key, Value)
                Case Sources.Registry
                    WriteReg(Key, Value)
            End Select
        End Sub
    End Class

    Public Shared Sub [Set](Key As String, Value As Object)
        Dim Entry As Setting = Nothing
        If Not Entries.TryGetValue(Key, Entry) Then Throw New KeyNotFoundException("未找到设置项：" & Key)
        Try
            Value = CTypeDynamic(Value, Entry.Type)
            If Not Entry.HasCache Then [Get](Key)
            If Entry.ValueCache = Value Then Return
            Entry.ValueCache = Value
            Entry.HasCache = True
            Entry.Save()
            If Entry.OnChanged IsNot Nothing Then Entry.OnChanged.Invoke(Value)
        Catch ex As Exception
            Logger.Error(ex, $"设置设置项时出错（{Key}, {Value}）")
        End Try
    End Sub

    Public Shared Sub SetSafe(Key As String, Value As Object)
        Dim Entry As Setting = Nothing
        If Not Entries.TryGetValue(Key, Entry) Then Throw New KeyNotFoundException("未找到设置项：" & Key)
        If Entry.Encrypted Then Throw New InvalidOperationException("禁止写入加密设置项：" & Key)
        [Set](Key, Value)
    End Sub

    Public Shared Function [Get](Key As String)
        Dim Entry As Setting = Nothing
        If Not Entries.TryGetValue(Key, Entry) Then Throw New KeyNotFoundException("未找到设置项：" & Key)
        If Entry.HasCache Then Return Entry.ValueCache
        Try
            Dim GotValue As String
            Dim DefaultValue As String = If(Entry.Encrypted, CryptographyUtils.DesEncrypt(Entry.DefaultValue, "OmniMix" & Identify), Entry.DefaultValue)
            Select Case Entry.Source
                Case Sources.Normal
                    GotValue = ReadIni("Setup", Key, DefaultValue)
                Case Sources.Registry
                    GotValue = ReadReg(Key, DefaultValue)
                Case Else
                    GotValue = DefaultValue
            End Select
            If Entry.Encrypted AndAlso GotValue <> DefaultValue Then
                Try
                    GotValue = CryptographyUtils.DesDecrypt(GotValue, "OmniMix" & Identify)
                Catch ex As Exception
                    Logger.Warn(ex, $"解密设置失败：{Key}")
                    GotValue = Entry.DefaultValue
                End Try
            ElseIf Entry.Encrypted Then
                GotValue = Entry.DefaultValue
            End If
            Entry.ValueCache = CTypeDynamic(GotValue, Entry.Type)
            Entry.HasCache = True
        Catch ex As Exception
            Logger.Error(ex, $"读取设置失败：{Key}", LogBehavior.Toast)
            Entry.ValueCache = CTypeDynamic(Entry.DefaultValue, Entry.Type)
            Entry.HasCache = True
        End Try
        Return Entry.ValueCache
    End Function

    Public Shared Function [Get](Of T)(Key As String) As T
        Return [Get](Key)
    End Function

    Public Shared Function GetSafe(Key As String)
        Dim Entry As Setting = Nothing
        If Not Entries.TryGetValue(Key, Entry) Then Throw New KeyNotFoundException("未找到设置项：" & Key)
        If Entry.Encrypted Then Throw New InvalidOperationException("禁止读取加密设置项：" & Key)
        Return [Get](Key)
    End Function

    Public Shared Sub Reset(Key As String)
        Dim Entry As Setting = Nothing
        If Not Entries.TryGetValue(Key, Entry) Then Throw New KeyNotFoundException("未找到设置项：" & Key)
        Try
            Entry.ValueCache = Entry.DefaultValue
            Entry.HasCache = True
            Select Case Entry.Source
                Case Sources.Normal
                    DeleteIniKey("Setup", Key)
                Case Sources.Registry
                    DeleteReg(Key)
            End Select
            If Entry.OnChanged IsNot Nothing Then Entry.OnChanged.Invoke(Entry.DefaultValue)
        Catch ex As Exception
            Logger.Error(ex, $"重置设置项时出错（{Key}）")
        End Try
    End Sub

    Public Shared Function GetDefault(Key As String) As String
        Return Entries(Key).DefaultValue
    End Function

    Public Shared Function HasSaved(Key As String) As Boolean
        Select Case Entries(Key).Source
            Case Sources.Normal
                Return HasIniKey("Setup", Key)
            Case Sources.Registry
                Return HasReg(Key)
            Case Else
                Return False
        End Select
    End Function

End Class
