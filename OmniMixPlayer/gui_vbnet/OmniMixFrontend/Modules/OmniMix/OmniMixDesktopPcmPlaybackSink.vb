Imports System.Runtime.InteropServices
Imports NAudio.Wave

Public Class OmniMixDesktopPcmPlaybackSink
    Implements IDisposable

    Private ReadOnly Provider As SharedPcmWaveProvider
    Private Output As WaveOutEvent
    Private IsDisposed As Boolean = False

    Public Sub New(MapName As String)
        Provider = New SharedPcmWaveProvider(MapName)
    End Sub

    Public Sub Start()
        If IsDisposed Then Return
        Output = New WaveOutEvent With {.DesiredLatency = 120, .NumberOfBuffers = 3}
        Output.Init(Provider)
        Output.Play()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        If IsDisposed Then Return
        IsDisposed = True
        Try
            Output?.Stop()
        Catch
        End Try
        Output?.Dispose()
        Provider.Dispose()
    End Sub

    Private NotInheritable Class SharedPcmWaveProvider
        Implements IWaveProvider, IDisposable

        Private Const DefaultSampleRate As Integer = 44100
        Private Const DefaultChannels As Integer = 2
        Private ReadOnly MapName As String
        Private Handle As IntPtr = IntPtr.Zero
        Private ReadOnly BufferLock As New Object()
        Private FloatBuffer As Single() = Array.Empty(Of Single)()
        Private CurrentAudibleFrame As Long = 0
        Private LastOpenFailureTick As Long = 0
        Private LastLogTick As Long = 0
        Private IsDisposed As Boolean = False

        Public Sub New(MapName As String)
            Me.MapName = If(MapName, "")
        End Sub

        Public ReadOnly Property WaveFormat As WaveFormat Implements IWaveProvider.WaveFormat
            Get
                Return WaveFormat.CreateIeeeFloatWaveFormat(DefaultSampleRate, DefaultChannels)
            End Get
        End Property

        Public Function Read(Buffer As Byte(), Offset As Integer, Count As Integer) As Integer Implements IWaveProvider.Read
            Array.Clear(Buffer, Offset, Count)
            If IsDisposed Then Return Count

            SyncLock BufferLock
                If Not EnsureOpen() OrElse Not BindCurrentStream() OrElse OmniPcm_IsFormatReady(Handle) = 0 Then Return Count

                Dim FramesToRead = Count \ (DefaultChannels * 4)
                Dim SamplesToRead = FramesToRead * DefaultChannels
                If FloatBuffer.Length < SamplesToRead Then ReDim FloatBuffer(SamplesToRead - 1)

                Dim FramesRead = OmniPcm_ReadFrames(Handle, FloatBuffer, FramesToRead)
                If FramesRead <= 0 Then Return Count

                Dim BytesRead = CInt(Math.Min(FramesRead * DefaultChannels * 4L, Count))
                System.Buffer.BlockCopy(FloatBuffer, 0, Buffer, Offset, BytesRead)
                CurrentAudibleFrame += FramesRead
                OmniPcm_SetAudibleCursor(Handle, CurrentAudibleFrame, 0)
            End SyncLock

            Return Count
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            IsDisposed = True
            If Handle <> IntPtr.Zero Then
                OmniPcm_Close(Handle)
                Handle = IntPtr.Zero
            End If
        End Sub

        Private Function EnsureOpen() As Boolean
            If Handle <> IntPtr.Zero AndAlso OmniPcm_IsOpen(Handle) <> 0 Then Return True

            Dim NowTick = Environment.TickCount64
            If NowTick - LastOpenFailureTick < 2000 Then Return False

            TryLoadNativeLibrary()
            Handle = OmniPcm_OpenUtf8(If(String.IsNullOrWhiteSpace(MapName), Nothing, MapName))
            If Handle <> IntPtr.Zero AndAlso OmniPcm_IsOpen(Handle) <> 0 Then Return True

            LastOpenFailureTick = NowTick
            LogThrottled("OmniMix 桌面播放器未能打开 OmniPcmShared。")
            Return False
        End Function

        Private Function BindCurrentStream() As Boolean
            Dim Result = OmniPcm_BindCurrentStream(Handle)
            If Result >= 0 Then Return True
            Return False
        End Function

        Private Sub LogThrottled(Message As String)
            Dim NowTick = Environment.TickCount64
            If NowTick - LastLogTick < 10000 Then Return
            LastLogTick = NowTick
            Logger.Warn(Message)
        End Sub

        Private Shared Sub TryLoadNativeLibrary()
            Dim Candidates = {
                Path.Combine(PathExe, "OmniPcmShared.dll"),
                Path.Combine(PathExe, "native", "x64", "OmniPcmShared.dll"),
                Path.Combine(Environment.CurrentDirectory, "OmniPcmShared.dll"),
                Path.Combine(Environment.CurrentDirectory, "native", "x64", "OmniPcmShared.dll")
            }

            For Each Candidate In Candidates
                If Not File.Exists(Candidate) Then Continue For
                Try
                    NativeLibrary.Load(Candidate)
                    Return
                Catch
                End Try
            Next
        End Sub

        <DllImport("OmniPcmShared", EntryPoint:="OmniPcm_OpenUtf8", ExactSpelling:=True, CallingConvention:=CallingConvention.Cdecl)>
        Private Shared Function OmniPcm_OpenUtf8(<MarshalAs(UnmanagedType.LPStr)> MapNameUtf8 As String) As IntPtr
        End Function

        <DllImport("OmniPcmShared", CallingConvention:=CallingConvention.Cdecl)>
        Private Shared Sub OmniPcm_Close(Handle As IntPtr)
        End Sub

        <DllImport("OmniPcmShared", CallingConvention:=CallingConvention.Cdecl)>
        Private Shared Function OmniPcm_IsOpen(Handle As IntPtr) As Integer
        End Function

        <DllImport("OmniPcmShared", CallingConvention:=CallingConvention.Cdecl)>
        Private Shared Function OmniPcm_BindCurrentStream(Handle As IntPtr) As Integer
        End Function

        <DllImport("OmniPcmShared", CallingConvention:=CallingConvention.Cdecl)>
        Private Shared Function OmniPcm_IsFormatReady(Handle As IntPtr) As Integer
        End Function

        <DllImport("OmniPcmShared", CallingConvention:=CallingConvention.Cdecl)>
        Private Shared Function OmniPcm_ReadFrames(Handle As IntPtr, <Out> Buffer As Single(), FramesToRead As Integer) As Long
        End Function

        <DllImport("OmniPcmShared", CallingConvention:=CallingConvention.Cdecl)>
        Private Shared Function OmniPcm_SetAudibleCursor(Handle As IntPtr, Frame As Long, AllowBackward As Integer) As Integer
        End Function
    End Class

End Class
