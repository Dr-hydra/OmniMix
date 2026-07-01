Imports System.Globalization
Imports System.Text.RegularExpressions

Public Class OmniMixLyricInfo
    Public Property Uuid As String = ""
    Public Property ModuleId As String = ""
    Public Property Lrc As String = ""
    Public Property Tlyric As String = ""
    Public Property Rlyric As String = ""
End Class

Public Class OmniMixLyricLineInfo
    Public Property TimeSeconds As Double
    Public Property Text As String = ""
    Public Property Translation As String = ""
End Class

Public Module OmniMixLyricHelper

    Private ReadOnly TimeTagRegex As New Regex("\[(\d{1,2}):(\d{2})(?:[\.:](\d{1,3}))?\]", RegexOptions.Compiled)

    Public Function ParseLrc(Lrc As String) As List(Of OmniMixLyricLineInfo)
        Dim Result As New List(Of OmniMixLyricLineInfo)
        If String.IsNullOrWhiteSpace(Lrc) Then Return Result

        For Each RawLine In Lrc.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf).Split({vbLf}, StringSplitOptions.None)
            Dim Matches = TimeTagRegex.Matches(RawLine)
            If Matches.Count = 0 Then Continue For

            Dim Text = TimeTagRegex.Replace(RawLine, "").Trim()
            If String.IsNullOrWhiteSpace(Text) Then Continue For

            For Each Match As Match In Matches
                Dim Seconds = ParseTimeTag(Match)
                If Seconds < 0 Then Continue For
                Result.Add(New OmniMixLyricLineInfo With {
                    .TimeSeconds = Seconds,
                    .Text = Text
                })
            Next
        Next

        Return Result.
            OrderBy(Function(Line) Line.TimeSeconds).
            ToList()
    End Function

    Public Function ParseCombinedLyrics(Lrc As String, Tlyric As String, Rlyric As String) As List(Of OmniMixLyricLineInfo)
        Dim Primary = MergeSameTimestampLyrics(ParseLrc(Lrc))
        Dim Translation = MergeSameTimestampLyrics(ParseLrc(If(String.IsNullOrWhiteSpace(Tlyric), Rlyric, Tlyric)))

        If Primary.Count = 0 Then Return Translation
        If Translation.Count = 0 Then Return Primary

        For Each Line In Primary
            Dim Match = Translation.
                OrderBy(Function(TransLine) Math.Abs(TransLine.TimeSeconds - Line.TimeSeconds)).
                FirstOrDefault()
            If Match Is Nothing OrElse Math.Abs(Match.TimeSeconds - Line.TimeSeconds) > 0.35 Then Continue For
            If String.Equals(Line.Text.Trim(), Match.Text.Trim(), StringComparison.OrdinalIgnoreCase) Then Continue For
            AddTranslation(Line, Match.Text.Trim())
        Next

        Return Primary
    End Function

    Public Function FormatLyricLine(Line As OmniMixLyricLineInfo, Optional Multiline As Boolean = True) As String
        If Line Is Nothing Then Return ""
        Dim Text = If(Line.Text, "").Trim()
        Dim Translation = If(Line.Translation, "").Trim()
        If String.IsNullOrWhiteSpace(Translation) Then Return Text
        If String.IsNullOrWhiteSpace(Text) Then Return Translation
        Return Text & If(Multiline, vbLf, " / ") & Translation
    End Function

    Public Function GetCurrentLineIndex(Lines As IList(Of OmniMixLyricLineInfo), PositionSeconds As Double) As Integer
        If Lines Is Nothing OrElse Lines.Count = 0 Then Return -1
        For Index = Lines.Count - 1 To 0 Step -1
            If PositionSeconds + 0.05 >= Lines(Index).TimeSeconds Then Return Index
        Next
        Return -1
    End Function

    Private Function ParseTimeTag(Match As Match) As Double
        Try
            Dim Minutes = Integer.Parse(Match.Groups(1).Value, CultureInfo.InvariantCulture)
            Dim Seconds = Integer.Parse(Match.Groups(2).Value, CultureInfo.InvariantCulture)
            Dim Milliseconds = 0
            If Match.Groups(3).Success Then
                Dim Fraction = Match.Groups(3).Value
                Select Case Fraction.Length
                    Case 1
                        Milliseconds = Integer.Parse(Fraction, CultureInfo.InvariantCulture) * 100
                    Case 2
                        Milliseconds = Integer.Parse(Fraction, CultureInfo.InvariantCulture) * 10
                    Case Else
                        Milliseconds = Integer.Parse(Fraction.Substring(0, Math.Min(3, Fraction.Length)), CultureInfo.InvariantCulture)
                End Select
            End If
            Return Minutes * 60 + Seconds + Milliseconds / 1000.0
        Catch
            Return -1
        End Try
    End Function

    Private Function MergeSameTimestampLyrics(Lines As List(Of OmniMixLyricLineInfo)) As List(Of OmniMixLyricLineInfo)
        If Lines Is Nothing OrElse Lines.Count = 0 Then Return New List(Of OmniMixLyricLineInfo)

        Dim Result As New List(Of OmniMixLyricLineInfo)
        For Each Group In Lines.
            GroupBy(Function(Line) CInt(Math.Round(Line.TimeSeconds * 100))).
            OrderBy(Function(GroupItem) GroupItem.Key)

            Dim OrderedLines = Group.
                OrderBy(Function(Line) Line.TimeSeconds).
                ToList()
            Dim FirstLine = OrderedLines.First()
            Dim Merged As New OmniMixLyricLineInfo With {
                .TimeSeconds = FirstLine.TimeSeconds,
                .Text = If(FirstLine.Text, "").Trim()
            }

            For Each ExtraLine In OrderedLines.Skip(1)
                AddTranslation(Merged, If(ExtraLine.Text, "").Trim())
            Next
            Result.Add(Merged)
        Next

        Return Result
    End Function

    Private Sub AddTranslation(Line As OmniMixLyricLineInfo, Text As String)
        If Line Is Nothing OrElse String.IsNullOrWhiteSpace(Text) Then Return
        Dim CleanText = Text.Trim()
        If String.Equals(If(Line.Text, "").Trim(), CleanText, StringComparison.OrdinalIgnoreCase) Then Return

        Dim Existing = If(Line.Translation, "").Trim()
        If String.IsNullOrWhiteSpace(Existing) Then
            Line.Translation = CleanText
            Return
        End If
        If Existing.Split({"/"}, StringSplitOptions.None).
            Any(Function(Item) String.Equals(Item.Trim(), CleanText, StringComparison.OrdinalIgnoreCase)) Then Return
        Line.Translation = Existing & " / " & CleanText
    End Sub
End Module
