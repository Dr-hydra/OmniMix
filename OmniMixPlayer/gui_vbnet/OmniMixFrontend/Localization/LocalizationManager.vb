Imports System.Globalization
Imports System.ComponentModel
Imports System.Text.RegularExpressions

Public Module LocalizationManager

    Private IsElementTranslationRegistered As Boolean
    Private ReadOnly TranslationWatcherProperty As DependencyProperty =
        DependencyProperty.RegisterAttached("TranslationWatcher", GetType(Boolean), GetType(LocalizationManager), New PropertyMetadata(False))
    Private SourcePrefixTranslations As New List(Of KeyValuePair(Of String, String))
    Private ReadOnly LocalizablePropertyNames As String() = {"Text", "Title", "Info", "HintText"}

    Public Const AutomaticLanguage As String = "auto"
    Public Const SimplifiedChineseLanguage As String = "zh-CN"
    Public Const EnglishLanguage As String = "en-US"

    Public ReadOnly Property CurrentLanguage As String

    Public ReadOnly Property IsEnglish As Boolean
        Get
            Return String.Equals(CurrentLanguage, EnglishLanguage, StringComparison.OrdinalIgnoreCase)
        End Get
    End Property

    Public Sub Initialize()
        Dim ConfiguredLanguage = Settings.Get(Of String)("UiLanguage")
        _CurrentLanguage = ResolveLanguage(ConfiguredLanguage, CultureInfo.CurrentUICulture)
        Lang = _CurrentLanguage.Replace("-", "_")

        Dim UiCulture As New CultureInfo(_CurrentLanguage)
        CultureInfo.DefaultThreadCurrentUICulture = UiCulture
        Thread.CurrentThread.CurrentUICulture = UiCulture
        LoadResourceDictionary(_CurrentLanguage)
        RegisterElementTranslation()
    End Sub

    Public Function ResolveLanguage(ConfiguredLanguage As String, SystemCulture As CultureInfo) As String
        If String.Equals(ConfiguredLanguage, SimplifiedChineseLanguage, StringComparison.OrdinalIgnoreCase) Then Return SimplifiedChineseLanguage
        If String.Equals(ConfiguredLanguage, EnglishLanguage, StringComparison.OrdinalIgnoreCase) Then Return EnglishLanguage

        Dim CultureName = If(SystemCulture?.Name, "")
        If CultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase) Then Return SimplifiedChineseLanguage
        Return EnglishLanguage
    End Function

    Public Function Tr(Key As String, Optional Fallback As String = Nothing) As String
        Dim ResourceKey = If(Key.StartsWith("Loc.", StringComparison.Ordinal), Key, "Loc." & Key)
        Dim Value = Application.Current?.TryFindResource(ResourceKey)
        If Value IsNot Nothing Then Return Value.ToString()
        Return If(Fallback, Key)
    End Function

    Public Function TrFormat(Key As String, ParamArray Arguments As Object()) As String
        Return String.Format(CultureInfo.CurrentCulture, Tr(Key), Arguments)
    End Function

    Public Function TrSource(SourceText As String) As String
        If Not IsEnglish OrElse String.IsNullOrEmpty(SourceText) Then Return SourceText
        Dim Value = Application.Current?.TryFindResource("Loc.Source." & SourceText)
        If Value IsNot Nothing Then Return Value.ToString()

        For Each PrefixTranslation In SourcePrefixTranslations
            If Not SourceText.StartsWith(PrefixTranslation.Key, StringComparison.Ordinal) Then Continue For
            Return PrefixTranslation.Value & TrSource(SourceText.Substring(PrefixTranslation.Key.Length))
        Next
        Return TranslateStructuredSource(SourceText)
    End Function

    Private Function TranslateStructuredSource(SourceText As String) As String
        Dim Match = Regex.Match(SourceText, "^(队列|历史)为空。$")
        If Match.Success Then Return String.Format(Tr("Pattern.CollectionEmpty"), TrSource(Match.Groups(1).Value))

        Match = Regex.Match(SourceText, "^(队列|历史)中有 (\d+) 首曲目。$")
        If Match.Success Then Return String.Format(Tr("Pattern.CollectionCount"), TrSource(Match.Groups(1).Value), Match.Groups(2).Value)

        Match = Regex.Match(SourceText, "^已读取 (\d+) 个播放实例；在线 (\d+) 个，可由后端控制 (\d+) 个。$")
        If Match.Success Then Return String.Format(Tr("Pattern.InstanceCount"), Match.Groups(1).Value, Match.Groups(2).Value, Match.Groups(3).Value)

        Match = Regex.Match(SourceText, "^归档：(\d+) 个已保存实例归档。$")
        If Match.Success Then Return String.Format(Tr("Pattern.ArchiveCount"), Match.Groups(1).Value)

        Match = Regex.Match(SourceText, "^已读取 (\d+) 个模块；当前已加载 (\d+) 个，配置为启用 (\d+) 个。$")
        If Match.Success Then Return String.Format(Tr("Pattern.ModuleCount"), Match.Groups(1).Value, Match.Groups(2).Value, Match.Groups(3).Value)

        Match = Regex.Match(SourceText, "^启动台：(\d+) 个模块快捷入口。$")
        If Match.Success Then Return String.Format(Tr("Pattern.LaunchpadCount"), Match.Groups(1).Value)

        Match = Regex.Match(SourceText, "^(\d+) 组，(\d+) 首歌曲。$")
        If Match.Success Then Return String.Format(Tr("Pattern.LibraryCount"), Match.Groups(1).Value, Match.Groups(2).Value)

        Match = Regex.Match(SourceText, "^搜索""(.*)""：(\d+) 组，(\d+) 首歌曲。$")
        If Match.Success Then Return String.Format(Tr("Pattern.SearchCount"), Match.Groups(1).Value, Match.Groups(2).Value, Match.Groups(3).Value)

        Return SourceText
    End Function

    Private Sub RegisterElementTranslation()
        If IsElementTranslationRegistered OrElse Not IsEnglish Then Return
        IsElementTranslationRegistered = True
        EventManager.RegisterClassHandler(GetType(FrameworkElement), FrameworkElement.LoadedEvent,
                                          New RoutedEventHandler(AddressOf Element_Loaded), True)
    End Sub

    Private Sub Element_Loaded(Sender As Object, e As RoutedEventArgs)
        Dim Element = TryCast(Sender, FrameworkElement)
        If Element Is Nothing Then Return
        PrepareElement(Element)
    End Sub

    Public Sub TranslateVisualTree(Root As DependencyObject)
        If Not IsEnglish OrElse Root Is Nothing Then Return
        Dim Pending As New Stack(Of DependencyObject)()
        Dim Visited As New HashSet(Of DependencyObject)()
        Pending.Push(Root)

        While Pending.Count > 0
            Dim Current = Pending.Pop()
            If Current Is Nothing OrElse Not Visited.Add(Current) Then Continue While

            Dim Element = TryCast(Current, FrameworkElement)
            If Element IsNot Nothing Then
                If TypeOf Element Is Control Then DirectCast(Element, Control).ApplyTemplate()
                PrepareElement(Element)
            End If

            For Each Child In LogicalTreeHelper.GetChildren(Current).OfType(Of DependencyObject)()
                Pending.Push(Child)
            Next
            Try
                For Index = 0 To VisualTreeHelper.GetChildrenCount(Current) - 1
                    Pending.Push(VisualTreeHelper.GetChild(Current, Index))
                Next
            Catch Ex As InvalidOperationException
                ' Content elements can appear in the logical tree without being visual nodes.
            End Try
        End While
    End Sub

    Private Sub PrepareElement(Element As FrameworkElement)
        TranslateElement(Element)
        If Not CBool(Element.GetValue(TranslationWatcherProperty)) Then
            Element.SetValue(TranslationWatcherProperty, True)
            If TypeOf Element Is TextBlock Then
                Dim Descriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, GetType(TextBlock))
                Descriptor?.AddValueChanged(Element, Sub() TranslateTextBlock(DirectCast(Element, TextBlock)))
            End If
            Dim ToolTipDescriptor = DependencyPropertyDescriptor.FromProperty(FrameworkElement.ToolTipProperty, GetType(FrameworkElement))
            ToolTipDescriptor?.AddValueChanged(Element, Sub() TranslateToolTip(Element))
        End If
    End Sub

    Private Sub TranslateElement(Element As FrameworkElement)
        TranslateStringProperties(Element)
        If TypeOf Element Is TextBlock Then TranslateTextBlock(DirectCast(Element, TextBlock))
        If TypeOf Element Is Window Then
            Dim Target = DirectCast(Element, Window)
            Target.Title = TrSource(Target.Title)
        End If
        If TypeOf Element Is HeaderedContentControl Then
            Dim Target = DirectCast(Element, HeaderedContentControl)
            If TypeOf Target.Header Is String Then Target.Header = TrSource(Target.Header.ToString())
        End If
        If TypeOf Element Is ContentControl Then
            Dim Target = DirectCast(Element, ContentControl)
            If TypeOf Target.Content Is String Then Target.Content = TrSource(Target.Content.ToString())
        End If
        TranslateToolTip(Element)
    End Sub

    Private Sub TranslateStringProperties(Element As FrameworkElement)
        For Each PropertyName In LocalizablePropertyNames
            Try
                Dim TargetProperty = Element.GetType().GetProperty(PropertyName)
                If TargetProperty Is Nothing OrElse TargetProperty.PropertyType IsNot GetType(String) OrElse
                   Not TargetProperty.CanRead OrElse Not TargetProperty.CanWrite OrElse TargetProperty.GetIndexParameters().Length > 0 Then Continue For

                Dim CurrentText = TryCast(TargetProperty.GetValue(Element), String)
                Dim Translated = TrSource(CurrentText)
                If Not String.Equals(Translated, CurrentText, StringComparison.Ordinal) Then TargetProperty.SetValue(Element, Translated)
            Catch Ex As Exception
                ' A custom control may expose a transient or template-backed property; its inner TextBlock is translated separately.
            End Try
        Next
    End Sub

    Private Sub TranslateTextBlock(Target As TextBlock)
        Dim Translated = TrSource(Target.Text)
        If Not String.Equals(Translated, Target.Text, StringComparison.Ordinal) Then Target.Text = Translated
    End Sub

    Private Sub TranslateToolTip(Target As FrameworkElement)
        If TypeOf Target.ToolTip IsNot String Then Return
        Dim Translated = TrSource(Target.ToolTip.ToString())
        If Not String.Equals(Translated, Target.ToolTip.ToString(), StringComparison.Ordinal) Then Target.ToolTip = Translated
    End Sub

    Private Sub LoadResourceDictionary(Language As String)
        Dim Dictionaries = Application.Current.Resources.MergedDictionaries
        For Index = Dictionaries.Count - 1 To 0 Step -1
            Dim SourceText = Dictionaries(Index).Source?.OriginalString
            If SourceText IsNot Nothing AndAlso SourceText.Contains("Localization/Strings.", StringComparison.OrdinalIgnoreCase) Then
                Dictionaries.RemoveAt(Index)
            End If
        Next

        Dim LocalizationDictionary As New ResourceDictionary With {
            .Source = New Uri($"Localization/Strings.{Language}.xaml", UriKind.Relative)
        }
        Dictionaries.Insert(0, LocalizationDictionary)
        SourcePrefixTranslations = LocalizationDictionary.Keys.Cast(Of Object)().
            Select(Function(Key) Key.ToString()).
            Where(Function(Key) Key.StartsWith("Loc.Prefix.", StringComparison.Ordinal)).
            Select(Function(Key) New KeyValuePair(Of String, String)(Key.Substring("Loc.Prefix.".Length), LocalizationDictionary(Key).ToString())).
            OrderByDescending(Function(Item) Item.Key.Length).
            ToList()
    End Sub

End Module
