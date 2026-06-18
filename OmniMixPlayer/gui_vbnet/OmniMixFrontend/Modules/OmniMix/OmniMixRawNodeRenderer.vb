Public Module OmniMixRawNodeRenderer

    Public Delegate Sub RawNodeEventHandler(NodeId As String, Action As String, Value As String)

    Public Function Render(Node As OmniMixRawNodeData, BaseUrl As String, OnEvent As RawNodeEventHandler) As FrameworkElement
        If Node Is Nothing Then Return CreateEmptyPanel()

        Select Case If(Node.NodeType, "").Trim().ToLowerInvariant()
            Case "container"
                Return RenderContainer(Node, BaseUrl, OnEvent)
            Case "text"
                Return RenderText(Node)
            Case "input"
                Return RenderInput(Node, OnEvent)
            Case "button"
                Return RenderButton(Node, OnEvent)
            Case "switch"
                Return RenderSwitch(Node, OnEvent)
            Case "image"
                Return RenderImage(Node, BaseUrl)
            Case "select"
                Return RenderSelect(Node, OnEvent)
            Case "list"
                Return RenderList(Node, BaseUrl, OnEvent)
            Case Else
                Return RenderText(New OmniMixRawNodeData With {.Text = If(String.IsNullOrWhiteSpace(Node.Text), "暂不支持的节点：" & Node.NodeType, Node.Text)})
        End Select
    End Function

    Private Function RenderContainer(Node As OmniMixRawNodeData, BaseUrl As String, OnEvent As RawNodeEventHandler) As FrameworkElement
        Dim Panel As New StackPanel With {
            .Orientation = If(String.Equals(Node.Direction, "Horizontal", StringComparison.OrdinalIgnoreCase), Orientation.Horizontal, Orientation.Vertical),
            .Margin = New Thickness(0, 0, 0, 4)
        }

        For Each Child In If(Node.Children, New List(Of OmniMixRawNodeData))
            Dim Element = Render(Child, BaseUrl, OnEvent)
            Element.Margin = AddSpacing(Element.Margin, Node.Spacing, Panel.Orientation)
            Panel.Children.Add(Element)
        Next

        If Panel.Children.Count = 0 Then Return CreateEmptyPanel()
        If Panel.Orientation = Orientation.Horizontal Then Return Panel
        If Node.Padding <= 0 Then Return Panel

        Return WrapPanel(Panel, Math.Max(12, Node.Padding - 6), 10)
    End Function

    Private Function RenderList(Node As OmniMixRawNodeData, BaseUrl As String, OnEvent As RawNodeEventHandler) As FrameworkElement
        Dim Panel As New StackPanel With {.Orientation = Orientation.Vertical}
        For Each Item In If(Node.Items, New List(Of OmniMixRawNodeData))
            Dim Element = Render(Item, BaseUrl, OnEvent)
            Element.Margin = AddSpacing(Element.Margin, Node.Spacing, Orientation.Vertical)
            Panel.Children.Add(Element)
        Next
        If Panel.Children.Count = 0 Then Return CreateEmptyPanel()
        Return Panel
    End Function

    Private Function RenderText(Node As OmniMixRawNodeData) As FrameworkElement
        Dim IsTitle = Node.FontSize >= 16
        Dim Text As New TextBlock With {
            .Text = If(Node.Text, ""),
            .TextWrapping = TextWrapping.Wrap,
            .FontSize = If(Node.FontSize <= 0, 14, Node.FontSize),
            .FontWeight = If(IsTitle, FontWeights.SemiBold, FontWeights.Normal),
            .Margin = If(IsTitle, New Thickness(0, 4, 0, 8), New Thickness(0, 2, 0, 4))
        }
        Text.SetResourceReference(TextBlock.ForegroundProperty, If(IsTitle, "ColorBrush3", "ColorBrush2"))
        Return Text
    End Function

    Private Function RenderInput(Node As OmniMixRawNodeData, OnEvent As RawNodeEventHandler) As FrameworkElement
        Dim Row = CreateSettingRow(Node.Text)
        Dim TextBox As New MyTextBox With {
            .Text = If(Node.Value, ""),
            .HintText = If(Node.Text, ""),
            .MinWidth = 220,
            .MaxWidth = 520,
            .HorizontalAlignment = HorizontalAlignment.Left
        }
        Dim LastSentText As String = If(Node.Value, "")
        Dim CommitValue As Action =
            Sub()
                If TextBox.Text = LastSentText Then Return
                LastSentText = TextBox.Text
                OnEvent?.Invoke(Node.Id, "change", TextBox.Text)
            End Sub
        AddHandler TextBox.KeyDown, Sub(sender, e)
                                        If e.Key = System.Windows.Input.Key.Enter Then
                                            CommitValue()
                                            e.Handled = True
                                        End If
                                    End Sub
        AddHandler TextBox.LostKeyboardFocus, Sub()
                                                  CommitValue()
                                              End Sub
        AddHandler TextBox.LostFocus, Sub()
                                          CommitValue()
                                      End Sub
        Dim InputPanel As New StackPanel With {
            .Orientation = Orientation.Horizontal,
            .HorizontalAlignment = HorizontalAlignment.Left
        }
        InputPanel.Children.Add(TextBox)

        Dim CommitButton As New MyButton With {
            .Text = "确定",
            .Width = 54,
            .Height = 30,
            .Margin = New Thickness(8, 0, 0, 0),
            .ColorType = MyButton.ColorState.Highlight
        }
        AddHandler CommitButton.Click, Sub()
                                           CommitValue()
                                       End Sub
        InputPanel.Children.Add(CommitButton)

        Grid.SetColumn(InputPanel, 1)
        Row.Children.Add(InputPanel)
        Return WrapSettingRow(Row)
    End Function

    Private Function RenderButton(Node As OmniMixRawNodeData, OnEvent As RawNodeEventHandler) As FrameworkElement
        Dim Button As New MyButton With {
            .Text = If(String.IsNullOrWhiteSpace(Node.Text), "按钮", Node.Text),
            .Padding = New Thickness(13, 0, 13, 0),
            .MinWidth = 120,
            .Height = 34,
            .HorizontalAlignment = HorizontalAlignment.Left,
            .ColorType = If(String.Equals(Node.ButtonVariant, "danger", StringComparison.OrdinalIgnoreCase), MyButton.ColorState.Red, MyButton.ColorState.Highlight),
            .Margin = New Thickness(0, 5, 12, 5)
        }
        AddHandler Button.Click, Sub(sender, e)
                                     System.Windows.Input.Keyboard.ClearFocus()
                                     Button.Dispatcher.BeginInvoke(Async Sub()
                                                                       Await Task.Delay(850)
                                                                       OnEvent?.Invoke(Node.Id, "click", If(Node.Value, ""))
                                                                   End Sub, System.Windows.Threading.DispatcherPriority.ContextIdle)
                                 End Sub
        Return Button
    End Function

    Private Function RenderSwitch(Node As OmniMixRawNodeData, OnEvent As RawNodeEventHandler) As FrameworkElement
        Dim Row = CreateSettingRow("")
        Dim Check As New MyCheckBox With {
            .Text = If(Node.Text, ""),
            .Checked = Node.Checked,
            .Height = 28
        }
        AddHandler Check.Change, Sub(sender, user)
                                     If user Then OnEvent?.Invoke(Node.Id, "toggle", Check.Checked.ToString().ToLowerInvariant())
                                 End Sub
        Grid.SetColumn(Check, 1)
        Row.Children.Add(Check)
        Return WrapSettingRow(Row)
    End Function

    Private Function RenderSelect(Node As OmniMixRawNodeData, OnEvent As RawNodeEventHandler) As FrameworkElement
        Dim Row = CreateSettingRow(Node.Text)
        Dim Combo As New MyComboBox With {
            .MinWidth = 220,
            .MaxWidth = 520,
            .HorizontalAlignment = HorizontalAlignment.Left
        }
        Dim IsInitializing = True
        For Each OptionItem In If(Node.Options, New List(Of OmniMixRawOptionData))
            Dim Item As New MyComboBoxItem With {
                .Content = If(String.IsNullOrWhiteSpace(OptionItem.Label), OptionItem.Value, OptionItem.Label),
                .Tag = OptionItem.Value
            }
            Combo.Items.Add(Item)
            If OptionItem.Value = Node.SelectedValue Then Combo.SelectedItem = Item
        Next
        AddHandler Combo.SelectionChanged, Sub()
                                               If IsInitializing Then Return
                                               If TypeOf Combo.SelectedItem Is MyComboBoxItem Then
                                                   OnEvent?.Invoke(Node.Id, "change", CType(Combo.SelectedItem, MyComboBoxItem).Tag?.ToString())
                                               End If
                                           End Sub
        IsInitializing = False
        Grid.SetColumn(Combo, 1)
        Row.Children.Add(Combo)
        Return WrapSettingRow(Row)
    End Function

    Private Function RenderImage(Node As OmniMixRawNodeData, BaseUrl As String) As FrameworkElement
        Dim Source = If(Node.Source, "")
        If Source.StartsWith("/") AndAlso Not String.IsNullOrWhiteSpace(BaseUrl) Then Source = BaseUrl.TrimEnd("/"c) & Source
        Dim Image As New MyImage With {
            .Source = Source,
            .Width = If(Node.ImageWidth <= 0, 200, Node.ImageWidth),
            .Height = If(Node.ImageHeight <= 0, 200, Node.ImageHeight),
            .Stretch = If(Node.ImageFit = "cover", Stretch.UniformToFill, If(Node.ImageFit = "fill", Stretch.Fill, Stretch.Uniform)),
            .Margin = New Thickness(0, 3, 0, 3)
        }
        Dim Border As New Border With {
            .CornerRadius = New CornerRadius(6),
            .Padding = New Thickness(12),
            .Margin = New Thickness(0, 6, 0, 10),
            .HorizontalAlignment = HorizontalAlignment.Left,
            .Child = Image
        }
        Border.SetResourceReference(Border.BackgroundProperty, "ColorBrushSemiTransparent")
        Return Border
    End Function

    Private Function CreateSettingRow(Label As String) As Grid
        Dim Row As New Grid With {.MinHeight = 36}
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(150)})
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        If Not String.IsNullOrWhiteSpace(Label) Then
            Dim Text As New TextBlock With {
                .Text = Label,
                .VerticalAlignment = VerticalAlignment.Center,
                .TextWrapping = TextWrapping.Wrap,
                .Margin = New Thickness(0, 0, 18, 0),
                .FontSize = 13
            }
            Text.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush2")
            Row.Children.Add(Text)
        End If
        Return Row
    End Function

    Private Function WrapSettingRow(Row As Grid) As FrameworkElement
        Dim Border As New Border With {
            .CornerRadius = New CornerRadius(6),
            .Padding = New Thickness(12, 8, 12, 8),
            .Margin = New Thickness(0, 0, 0, 8),
            .Child = Row
        }
        Border.SetResourceReference(Border.BackgroundProperty, "ColorBrushSemiTransparent")
        Return Border
    End Function

    Private Function WrapPanel(Child As UIElement, Padding As Double, BottomMargin As Double) As FrameworkElement
        Dim Border As New Border With {
            .CornerRadius = New CornerRadius(6),
            .Padding = New Thickness(Padding),
            .Margin = New Thickness(0, 0, 0, BottomMargin),
            .Child = Child
        }
        Border.SetResourceReference(Border.BackgroundProperty, "ColorBrushSemiTransparent")
        Return Border
    End Function

    Private Function CreateEmptyPanel() As FrameworkElement
        Return New StackPanel()
    End Function

    Private Function AddSpacing(Margin As Thickness, Spacing As Double, Orientation As Orientation) As Thickness
        If Spacing <= 0 Then Return Margin
        If Orientation = Orientation.Horizontal Then
            Return New Thickness(Margin.Left, Margin.Top, Margin.Right + Spacing, Margin.Bottom)
        End If
        Return New Thickness(Margin.Left, Margin.Top, Margin.Right, Margin.Bottom + Spacing)
    End Function

End Module
