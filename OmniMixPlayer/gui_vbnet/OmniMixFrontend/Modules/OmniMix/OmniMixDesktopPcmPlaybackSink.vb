Imports System.Runtime.InteropServices
Imports NAudio.Wave

Public Class OmniMixDesktopPcmPlaybackSink
    Implements IDisposable

    Private ReadOnly Provider As SharedPcmWaveProvider
    Private Output As WasapiOut
    Private IsDisposed As Boolean = False

    Public Sub New(MapName As String)
        Provider = New SharedPcmWaveProvider(MapName)
    End Sub

    Public Sub Start()
        If IsDisposed Then Return
        ' 桌面播放器升级为现代 WASAPI 共享模式输出，提供 100ms 低延迟
        Output = New WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 100)
        Output.Init(Provider)
        Output.Play()
        Logger.Info("桌面播放器已成功启动现代 WASAPI 共享模式输出")
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
        Private Shared ReadOnly NativeLibraryLock As New Object()
        Private Shared NativeLibraryHandle As IntPtr = IntPtr.Zero
        Private FloatBuffer As Single() = Array.Empty(Of Single)()
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
                OmniPcm_ReportAudioSourcePosition(Handle, CInt(FramesRead))
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

            Try
                If Not TryLoadNativeLibrary() Then
                    LastOpenFailureTick = NowTick
                    LogThrottled("OmniMix 桌面播放器未找到 OmniPcmShared.dll。")
                    Return False
                End If

                Handle = OmniPcm_OpenUtf8(If(String.IsNullOrWhiteSpace(MapName), Nothing, MapName))
                If Handle <> IntPtr.Zero AndAlso OmniPcm_IsOpen(Handle) <> 0 Then Return True
            Catch Ex As Exception When TypeOf Ex Is DllNotFoundException OrElse
                                           TypeOf Ex Is BadImageFormatException OrElse
                                           TypeOf Ex Is EntryPointNotFoundException
                LastOpenFailureTick = NowTick
                LogThrottled("OmniMix 桌面播放器加载 OmniPcmShared.dll 失败：" & Ex.Message)
                Return False
            End Try

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

        Private Shared Function TryLoadNativeLibrary() As Boolean
            SyncLock NativeLibraryLock
                If NativeLibraryHandle <> IntPtr.Zero Then Return True

                Dim BaseDirectory = PathExeFolder.TrimEnd("\"c, "/"c)
                Dim AppBaseDirectory = AppContext.BaseDirectory.TrimEnd("\"c, "/"c)
                Dim WorkingDirectory = Environment.CurrentDirectory.TrimEnd("\"c, "/"c)
                Dim Candidates = {
                    Path.Combine(BaseDirectory, "OmniPcmShared.dll"),
                    Path.Combine(BaseDirectory, "native", "x64", "OmniPcmShared.dll"),
                    Path.Combine(AppBaseDirectory, "OmniPcmShared.dll"),
                    Path.Combine(AppBaseDirectory, "native", "x64", "OmniPcmShared.dll"),
                    Path.Combine(WorkingDirectory, "OmniPcmShared.dll"),
                    Path.Combine(WorkingDirectory, "native", "x64", "OmniPcmShared.dll")
                }

                For Each Candidate In Candidates.Distinct(StringComparer.OrdinalIgnoreCase)
                    If Not File.Exists(Candidate) Then Continue For
                    Try
                        NativeLibraryHandle = NativeLibrary.Load(Candidate)
                        If NativeLibraryHandle <> IntPtr.Zero Then Return True
                    Catch
                    End Try
                Next

                Return False
            End SyncLock
        End Function

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
        Private Shared Function OmniPcm_ReportAudioSourcePosition(Handle As IntPtr, TimeSamples As Integer) As Integer
        End Function
    End Class

End Class
