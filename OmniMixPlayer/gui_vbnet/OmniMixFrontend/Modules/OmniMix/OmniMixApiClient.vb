Imports System.Net.Http
Imports System.Net
Imports System.Collections.Concurrent
Imports System.Text
Imports System.Text.Json
Imports System.Text.Json.Serialization
Imports Grpc.Core
Imports Grpc.Net.Client
Imports Grpc.Net.Client.Web
Imports OmniMixPlayer.SDK.Protos.Models
Imports OmniMixPlayer.SDK.Protos.Services

Public Class OmniMixBackendStatus
    Public Property IsOnline As Boolean
    Public Property Port As Integer?
    Public Property BaseUrl As String
    Public Property Message As String
    Public Property BackendPath As String
    Public Property StartedBackend As Boolean
End Class

Public Class OmniMixPlaylistData
    Public Property Tags As List(Of OmniMixTagInfo) = New List(Of OmniMixTagInfo)
    Public Property Albums As List(Of OmniMixAlbumInfo) = New List(Of OmniMixAlbumInfo)
    Public Property Songs As List(Of OmniMixSongInfo) = New List(Of OmniMixSongInfo)
    Public Property Playlists As List(Of OmniMixLibraryPlaylistInfo) = New List(Of OmniMixLibraryPlaylistInfo)
End Class

Public Class OmniMixLibraryPlaylistInfo
    Public Property Id As String = ""
    Public Property Name As String = ""
    Public Property ModuleId As String = ""
    Public Property CoverPath As String = ""
    Public Property SortOrder As Integer
    Public Property Songs As List(Of OmniMixSongInfo) = New List(Of OmniMixSongInfo)
End Class

Public Class OmniMixTagInfo
    Public Property Id As String = ""
    Public Property Name As String = ""
    Public Property ModuleId As String = ""
    Public Property BitValue As Integer
    Public Property IsGrowable As Boolean
End Class

Public Class OmniMixAlbumInfo
    Public Property Id As String = ""
    Public Property Name As String = ""
    Public Property TagId As String = ""
    Public Property ModuleId As String = ""
    Public Property CoverPath As String = ""
    Public Property SongCount As Integer
    Public Property IsGrowable As Boolean
End Class

Public Class OmniMixSongInfo
    Public Property Uuid As String = ""
    Public Property Title As String = ""
    Public Property Artist As String = ""
    Public Property AlbumId As String = ""
    Public Property Duration As Double
    Public Property ModuleId As String = ""
    Public Property CoverPath As String = ""
    Public Property CoverUrl As String = ""
    Public Property ImageUrl As String = ""
    Public Property IsFavorite As Boolean
    Public Property IsExcluded As Boolean
End Class

Public Class OmniMixTrackInfo
    Public Property Uuid As String = ""
    Public Property Title As String = ""
    Public Property Artist As String = ""
    Public Property AlbumId As String = ""
    Public Property Duration As Double
    Public Property ModuleId As String = ""
    Public Property CoverPath As String = ""
    Public Property CoverUrl As String = ""
    Public Property ImageUrl As String = ""
End Class

Public Class OmniMixQueueItemInfo
    Inherits OmniMixTrackInfo

    Public Property Index As Integer
End Class

Public Class OmniMixPlaylistSourceInfo
    Public Property Id As String = ""
    Public Property Name As String = ""
    Public Property SongCount As Integer
End Class

Public Class OmniMixPlaybackInstanceInfo
    Public Property Id As String = ""
    Public Property ClientId As String = ""
    Public Property Role As String = ""
    Public Property Mode As String = ""
    Public Property Attached As Boolean
    Public Property IsPlaying As Boolean
    Public Property Position As Double
    Public Property Volume As Double = 1
    Public Property TargetLatency As Double = 0.1
    Public Property QueueCount As Integer
    Public Property QueueIndex As Integer = -1
    Public Property HistoryCount As Integer
    Public Property SampleRate As Integer
    Public Property Channels As Integer
    Public Property Shuffle As Boolean
    Public Property RepeatMode As String = "none"
    Public Property CurrentTrack As OmniMixTrackInfo
    Public Property ModId As String = ""
    Public Property GameName As String = ""
    Public Property SharedMemoryReady As Boolean = True
    Public Property CanControlVolume As Boolean = True

    Public ReadOnly Property IsServerManaged As Boolean
        Get
            Return String.Equals(Mode, "ServerManaged", StringComparison.OrdinalIgnoreCase)
        End Get
    End Property
End Class

Public Class OmniMixInstanceStatsInfo
    Public Property InstanceCount As Integer
    Public Property AttachedAudioClients As Integer
    Public Property ControllerClients As Integer
    Public Property ObserverClients As Integer
    Public Property SharedMemoryBytes As Long
    Public Property ActiveDecoders As Integer
    Public Property TotalQueueItems As Integer
    Public Property HeartbeatTimeoutSeconds As Integer
    Public Property DetachedTtlSeconds As Integer
    Public Property CleanupIntervalSeconds As Integer
End Class

Public Class OmniMixArchiveInfo
    Public Property InstanceId As String = ""
    Public Property ModId As String = ""
    Public Property Mode As String = ""
    Public Property Label As String = ""
    Public Property ArchivedAt As String = ""

    Public ReadOnly Property DisplayName As String
        Get
            Return If(String.IsNullOrWhiteSpace(Label), InstanceId, Label)
        End Get
    End Property
End Class

Public Class OmniMixEqualizerPointInfo
    Public Property Id As String = ""
    Public Property Frequency As Double = 1000
    Public Property GainDb As Double = 0
    Public Property Q As Double = 1
    Public Property Type As String = "Peaking"
End Class

Public Class OmniMixEqualizerStateInfo
    Public Property Enabled As Boolean
    Public Property GlobalGainDb As Double
    Public Property SoftClipEnabled As Boolean = True
    Public Property Points As List(Of OmniMixEqualizerPointInfo) = New List(Of OmniMixEqualizerPointInfo)
End Class

Public Class OmniMixModuleInfo
    Public Property Id As String = ""
    Public Property Name As String = ""
    Public Property Version As String = ""
    Public Property Priority As Integer
    Public Property LoadedAt As String = ""
    Public Property Enabled As Boolean
    <JsonPropertyName("hasSettingsUI")>
    Public Property HasSettingsUI As Boolean
    Public Property HasQuickLinks As Boolean
    Public Property LinkEntries As List(Of OmniMixModuleLinkEntryInfo) = New List(Of OmniMixModuleLinkEntryInfo)
End Class

Public Class OmniMixModuleLinkEntryInfo
    Public Property Id As String = ""
    Public Property Title As String = ""
    Public Property Icon As String = ""
    Public Property Svg As String = ""
    Public Property BackgroundColor As String = ""
    Public Property IconColor As String = ""
End Class

Public Class OmniMixRawNodeData
    Public Property Id As String = ""
    Private _NodeType As String = ""
    <JsonPropertyName("node-type")>
    Public Property NodeType As String
        Get
            Return _NodeType
        End Get
        Set(value As String)
            _NodeType = If(value, "")
        End Set
    End Property
    <JsonPropertyName("nodeType")>
    Public Property NodeTypeCamel As String
        Get
            Return _NodeType
        End Get
        Set(value As String)
            If Not String.IsNullOrWhiteSpace(value) Then _NodeType = value
        End Set
    End Property
    Public Property Text As String = ""
    <JsonPropertyName("font-size")>
    Public Property FontSize As Double = 14
    Public Property Color As String = ""
    Public Property Direction As String = ""
    <JsonPropertyName("cross-axis-align")>
    Public Property CrossAxisAlignment As String = ""
    Public Property Spacing As Double = 8
    Public Property Padding As Double
    Public Property Children As List(Of OmniMixRawNodeData) = New List(Of OmniMixRawNodeData)
    Public Property Value As String = ""
    <JsonPropertyName("input-type")>
    Public Property InputType As String = "text"
    <JsonPropertyName("button-variant")>
    Public Property ButtonVariant As String = "primary"
    Public Property Checked As Boolean
    Public Property Source As String = ""
    <JsonPropertyName("image-width")>
    Public Property ImageWidth As Double = 200
    <JsonPropertyName("image-height")>
    Public Property ImageHeight As Double = 200
    <JsonPropertyName("image-fit")>
    Public Property ImageFit As String = "contain"
    <JsonPropertyName("selected-value")>
    Public Property SelectedValue As String = ""
    Public Property Options As List(Of OmniMixRawOptionData) = New List(Of OmniMixRawOptionData)
    Public Property Items As List(Of OmniMixRawNodeData) = New List(Of OmniMixRawNodeData)
End Class

Public Class OmniMixRawOptionData
    Public Property Value As String = ""
    Public Property Label As String = ""
End Class

Public Module OmniMixApiClient

    Private ReadOnly ProfileOnlyVolumeInstances As New ConcurrentDictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly VolumeSaveLocks As New ConcurrentDictionary(Of String, SemaphoreSlim)(StringComparer.OrdinalIgnoreCase)

    Private ReadOnly Client As New HttpClient With {
        .Timeout = TimeSpan.FromSeconds(3)
    }
    Private ReadOnly JsonOptions As New JsonSerializerOptions With {
        .PropertyNameCaseInsensitive = True
    }

    Public Async Function DiscoverAsync() As Task(Of OmniMixBackendStatus)
        Dim Candidates As New List(Of Integer)
        Dim FilePort = ReadPortFile()
        If FilePort.HasValue Then Candidates.Add(FilePort.Value)
        If Not Candidates.Contains(17890) Then Candidates.Add(17890)

        For Each CandidatePort In Candidates
            If Await CheckHealthAsync(CandidatePort) Then
                Return New OmniMixBackendStatus With {
                    .IsOnline = True,
                    .Port = CandidatePort,
                    .BaseUrl = $"http://127.0.0.1:{CandidatePort}",
                    .Message = $"已连接 OmniMix 后端（127.0.0.1:{CandidatePort}）"
                }
            End If
        Next

        Return New OmniMixBackendStatus With {
            .IsOnline = False,
            .Port = FilePort,
            .BaseUrl = If(FilePort.HasValue, $"http://127.0.0.1:{FilePort.Value}", ""),
            .Message = If(FilePort.HasValue,
                $"发现端口文件（{FilePort.Value}），但 /api/health 暂不可用。",
                "未发现可用的 OmniMix 后端。")
        }
    End Function

    Public Async Function GetPlaylistAsync(BaseUrl As String) As Task(Of OmniMixPlaylistData)
        Dim UseRestFallback = False
        Try
            Using Channel = CreateGrpcChannel(BaseUrl)
                Dim Library = New LibraryService.LibraryServiceClient(Channel)
                Dim TagsResponse = Await Library.QueryTagsAsync(New TagQuery())
                Dim AlbumsResponse = Await Library.QueryAlbumsAsync(New AlbumQuery())
                Dim TracksResponse = Await Library.QueryTracksAsync(New TrackQuery())
                Dim PlaylistsResponse = Await Library.QueryPlaylistsAsync(New PlaylistQuery())
                Dim LibraryPlaylists As New List(Of OmniMixLibraryPlaylistInfo)
                For Each PlaylistInfo In PlaylistsResponse.Playlists
                    Dim PlaylistWithEntries = Await Library.GetPlaylistWithEntriesAsync(New GetPlaylistWithEntriesRequest With {.PlaylistId = PlaylistInfo.Id})
                    LibraryPlaylists.Add(New OmniMixLibraryPlaylistInfo With {
                        .Id = PlaylistInfo.Id,
                        .Name = PlaylistInfo.Name,
                        .ModuleId = PlaylistInfo.ModuleId,
                        .CoverPath = PlaylistInfo.CoverUri,
                        .SortOrder = PlaylistInfo.SortOrder,
                        .Songs = PlaylistWithEntries.Entries.
                            OrderBy(Function(Entry) Entry.Position).
                            Select(Function(Entry) New OmniMixSongInfo With {
                                .Uuid = Entry.TrackUuid,
                                .Title = Entry.Title,
                                .Artist = Entry.Artist,
                                .AlbumId = Entry.AlbumId,
                                .Duration = Entry.Duration,
                                .ModuleId = PlaylistInfo.ModuleId,
                                .CoverPath = Entry.CoverUri,
                                .CoverUrl = Entry.CoverUri,
                                .ImageUrl = Entry.CoverUri
                            }).
                            ToList()
                    })
                Next
                Return New OmniMixPlaylistData With {
                    .Tags = TagsResponse.Tags.Select(AddressOf MapTag).ToList(),
                    .Albums = AlbumsResponse.Albums.Select(AddressOf MapAlbum).ToList(),
                    .Songs = TracksResponse.Tracks.Select(AddressOf MapSong).ToList(),
                    .Playlists = LibraryPlaylists
                }
            End Using
        Catch Ex As Exception When IsGrpcEndpointUnavailable(Ex)
            UseRestFallback = True
        End Try
        If UseRestFallback Then
            Dim Playlist = Await GetJsonAsync(Of OmniMixPlaylistData)(BaseUrl, "/api/playlist")
            Return If(Playlist, New OmniMixPlaylistData)
        End If
        Return New OmniMixPlaylistData
    End Function

    Public Async Function GetSongsAsync(BaseUrl As String, Optional AlbumId As String = "", Optional TagId As String = "") As Task(Of List(Of OmniMixSongInfo))
        Dim UseRestFallback = False
        Try
            Using Channel = CreateGrpcChannel(BaseUrl)
                Dim Library = New LibraryService.LibraryServiceClient(Channel)
                Dim Query As New TrackQuery With {
                    .AlbumId = If(AlbumId, "")
                }
                If Not String.IsNullOrWhiteSpace(TagId) Then Query.TagIds.Add(TagId)
                Dim Response = Await Library.QueryTracksAsync(Query)
                Return Response.Tracks.Select(AddressOf MapSong).ToList()
            End Using
        Catch Ex As Exception When IsGrpcEndpointUnavailable(Ex)
            UseRestFallback = True
        End Try
        If UseRestFallback Then
            Dim Query As New List(Of String)
            If Not String.IsNullOrWhiteSpace(AlbumId) Then Query.Add("albumId=" & Uri.EscapeDataString(AlbumId))
            If Not String.IsNullOrWhiteSpace(TagId) Then Query.Add("tagId=" & Uri.EscapeDataString(TagId))
            Dim ApiPath = "/api/songs" & If(Query.Count = 0, "", "?" & String.Join("&", Query))
            Dim Songs = Await GetJsonAsync(Of List(Of OmniMixSongInfo))(BaseUrl, ApiPath)
            Return If(Songs, New List(Of OmniMixSongInfo))
        End If
        Return New List(Of OmniMixSongInfo)
    End Function

    Public Async Function GetInstancesAsync(BaseUrl As String) As Task(Of List(Of OmniMixPlaybackInstanceInfo))
        Dim UseRestFallback = False
        Try
            Using Channel = CreateGrpcChannel(BaseUrl)
                Dim Instances = New InstanceService.InstanceServiceClient(Channel)
                Dim Playback = New PlaybackService.PlaybackServiceClient(Channel)
                Dim Response = Await Instances.ListInstancesAsync(New ListInstancesRequest())
                Dim Result As New List(Of OmniMixPlaybackInstanceInfo)
                For Each Summary In Response.Instances
                    Dim Profile As InstanceProfile = Nothing
                    Dim Status As PlaybackStatus = Nothing
                    Try
                        Profile = Await Instances.GetProfileAsync(New GetProfileRequest With {.InstanceId = Summary.Id})
                    Catch
                    End Try
                    Try
                        Status = Await Playback.GetStatusAsync(New GetStatusRequest With {.InstanceId = Summary.Id})
                    Catch
                    End Try
                    Result.Add(MapInstance(Summary, Profile, Status))
                Next
                Return Result
            End Using
        Catch Ex As Exception When IsGrpcEndpointUnavailable(Ex)
            UseRestFallback = True
        End Try
        If UseRestFallback Then
            Dim Instances = Await GetJsonAsync(Of List(Of OmniMixPlaybackInstanceInfo))(BaseUrl, "/api/instances")
            Return If(Instances, New List(Of OmniMixPlaybackInstanceInfo))
        End If
        Return New List(Of OmniMixPlaybackInstanceInfo)
    End Function

    Public Async Function GetInstanceStatsAsync(BaseUrl As String) As Task(Of OmniMixInstanceStatsInfo)
        Try
            Using Channel = CreateGrpcChannel(BaseUrl)
                Dim Instances = New InstanceService.InstanceServiceClient(Channel)
                Dim Response = Await Instances.ListInstancesAsync(New ListInstancesRequest())
                Dim Stats As New OmniMixInstanceStatsInfo With {
                    .InstanceCount = Response.Instances.Count,
                    .AttachedAudioClients = Response.Instances.Where(Function(Item) Item.IsOnline).Count(),
                    .ControllerClients = Response.Instances.Where(Function(Item) Item.Kind = CType(3, InstanceKind)).Count(),
                    .ObserverClients = Response.Instances.Where(Function(Item) Item.Kind = CType(4, InstanceKind)).Count(),
                    .TotalQueueItems = Response.Instances.Sum(Function(Item) Item.QueueCount)
                }
                Return Stats
            End Using
        Catch Ex As Exception When IsGrpcEndpointUnavailable(Ex)
        End Try
        Try
            Dim Stats = Await GetJsonAsync(Of OmniMixInstanceStatsInfo)(BaseUrl, "/api/instances/stats")
            Return If(Stats, New OmniMixInstanceStatsInfo)
        Catch RestEx As Exception When IsOptionalEndpointMissing(RestEx)
            Return New OmniMixInstanceStatsInfo
        End Try
    End Function

    Public Async Function GetInstanceQueueAsync(BaseUrl As String, InstanceId As String) As Task(Of List(Of OmniMixQueueItemInfo))
        Dim UseRestFallback = False
        Try
            Using Channel = CreateGrpcChannel(BaseUrl)
                Dim Playback = New PlaybackService.PlaybackServiceClient(Channel)
                Dim Response = Await Playback.GetQueueAsync(New GetQueueRequest With {.InstanceId = If(InstanceId, "")})
                Return Response.Queue.Select(AddressOf MapQueueTrack).ToList()
            End Using
        Catch Ex As Exception When IsGrpcEndpointUnavailable(Ex)
            UseRestFallback = True
        End Try
        If UseRestFallback Then
            Dim Queue = Await GetJsonAsync(Of List(Of OmniMixQueueItemInfo))(BaseUrl, "/api/instances/" & Uri.EscapeDataString(InstanceId) & "/queue")
            Return If(Queue, New List(Of OmniMixQueueItemInfo))
        End If
        Return New List(Of OmniMixQueueItemInfo)
    End Function

    Public Async Function GetPlaylistSourcesAsync(BaseUrl As String, InstanceId As String) As Task(Of List(Of OmniMixPlaylistSourceInfo))
        Dim UseRestFallback = False
        Try
            Using Channel = CreateGrpcChannel(BaseUrl)
                Dim Playback = New PlaybackService.PlaybackServiceClient(Channel)
                Dim Response = Await Playback.GetPlaylistSourcesAsync(New GetPlaylistSourcesRequest With {.InstanceId = If(InstanceId, "")})
                Return Response.Sources.Select(Function(Source) New OmniMixPlaylistSourceInfo With {
                    .Id = Source.Id,
                    .Name = Source.Name,
                    .SongCount = Source.SongCount
                }).ToList()
            End Using
        Catch Ex As Exception When IsGrpcEndpointUnavailable(Ex)
            UseRestFallback = True
        End Try
        If UseRestFallback Then
            Try
                Dim Sources = Await GetJsonAsync(Of List(Of OmniMixPlaylistSourceInfo))(BaseUrl, "/api/instances/" & Uri.EscapeDataString(InstanceId) & "/playlist/sources")
                Return If(Sources, New List(Of OmniMixPlaylistSourceInfo))
            Catch RestEx As Exception When IsOptionalEndpointMissing(RestEx)
                Return New List(Of OmniMixPlaylistSourceInfo)
            End Try
        End If
        Return New List(Of OmniMixPlaylistSourceInfo)
    End Function

    Public Async Function AddPlaylistSourceAsync(BaseUrl As String, InstanceId As String, SourceId As String, SourceName As String, Uuids As IEnumerable(Of String)) As Task
        Dim UseRestFallback = False
        Try
            Using Channel = CreateGrpcChannel(BaseUrl)
                Dim Playback = New PlaybackService.PlaybackServiceClient(Channel)
                Dim Existing = Await Playback.GetPlaylistSourcesAsync(New GetPlaylistSourcesRequest With {.InstanceId = If(InstanceId, "")})
                Dim Request As New SetPlaylistSourcesRequest With {.InstanceId = If(InstanceId, "")}
                For Each Source In Existing.Sources
                    Request.Sources.Add(New PlaylistSourceSpec With {
                        .Id = Source.Id,
                        .Name = Source.Name,
                        .Kind = Source.Kind,
                        .RefId = Source.RefId
                    })
                Next
                Dim NewSource As New PlaylistSourceSpec With {
                    .Id = If(SourceId, ""),
                    .Name = If(SourceName, ""),
                    .Kind = CType(4, PlaylistSourceKind)
                }
                NewSource.Uuids.AddRange(If(Uuids, Enumerable.Empty(Of String)()).Where(Function(Uuid) Not String.IsNullOrWhiteSpace(Uuid)))
                Request.Sources.Add(NewSource)
                Await Playback.SetPlaylistSourcesAsync(Request)
            End Using
        Catch Ex As Exception When IsGrpcEndpointUnavailable(Ex)
            UseRestFallback = True
        End Try
        If UseRestFallback Then
            Await PostJsonAsync(BaseUrl, "/api/instances/" & Uri.EscapeDataString(InstanceId) & "/playlist/sources", New With {
                .index = -1,
                .source = New With {
                    .id = If(SourceId, ""),
                    .name = If(SourceName, ""),
                    .uuids = If(Uuids, Enumerable.Empty(Of String)()).Where(Function(Uuid) Not String.IsNullOrWhiteSpace(Uuid)).ToArray()
                }
            })
        End If
    End Function

    Public Async Function RemovePlaylistSourceAsync(BaseUrl As String, InstanceId As String, SourceId As String) As Task
        Dim UseRestFallback = False
        Try
            Using Channel = CreateGrpcChannel(BaseUrl)
                Dim Playback = New PlaybackService.PlaybackServiceClient(Channel)
                Dim Existing = Await Playback.GetPlaylistSourcesAsync(New GetPlaylistSourcesRequest With {.InstanceId = If(InstanceId, "")})
                Dim Request As New SetPlaylistSourcesRequest With {.InstanceId = If(InstanceId, "")}
                For Each Source In Existing.Sources
                    If String.Equals(Source.Id, SourceId, StringComparison.OrdinalIgnoreCase) Then Continue For
                    Request.Sources.Add(New PlaylistSourceSpec With {
                        .Id = Source.Id,
                        .Name = Source.Name,
                        .Kind = Source.Kind,
                        .RefId = Source.RefId
                    })
                Next
                Await Playback.SetPlaylistSourcesAsync(Request)
            End Using
        Catch Ex As Exception When IsGrpcEndpointUnavailable(Ex)
            UseRestFallback = True
        End Try
        If UseRestFallback Then
            Await DeleteAsync(BaseUrl, "/api/instances/" & Uri.EscapeDataString(InstanceId) & "/playlist/sources/" & Uri.EscapeDataString(SourceId))
        End If
    End Function

    Public Async Function GetInstanceHistoryAsync(BaseUrl As String, InstanceId As String) As Task(Of List(Of OmniMixQueueItemInfo))
        Dim UseRestFallback = False
        Try
            Using Channel = CreateGrpcChannel(BaseUrl)
                Dim Playback = New PlaybackService.PlaybackServiceClient(Channel)
                Dim Response = Await Playback.GetHistoryAsync(New GetHistoryRequest With {.InstanceId = If(InstanceId, "")})
                Return Response.History.Select(AddressOf MapQueueTrack).ToList()
            End Using
        Catch Ex As Exception When IsGrpcEndpointUnavailable(Ex)
            UseRestFallback = True
        End Try
        If UseRestFallback Then
            Try
                Dim History = Await GetJsonAsync(Of List(Of OmniMixQueueItemInfo))(BaseUrl, "/api/instances/" & Uri.EscapeDataString(InstanceId) & "/history")
                Return If(History, New List(Of OmniMixQueueItemInfo))
            Catch RestEx As Exception When IsOptionalEndpointMissing(RestEx)
                Return New List(Of OmniMixQueueItemInfo)
            End Try
        End If
        Return New List(Of OmniMixQueueItemInfo)
    End Function

    Public Async Function ConnectControllerAsync(BaseUrl As String, Optional ClientId As String = "vbnet-gui") As Task
        Dim UseRestFallback = False
        Try
            Using Channel = CreateGrpcChannel(BaseUrl)
                Dim Instances = New InstanceService.InstanceServiceClient(Channel)
                Await Instances.ConnectAsync(New InstanceConnectRequest With {
                    .ClientId = If(ClientId, "vbnet-gui"),
                    .Kind = CType(2, InstanceKind),
                    .DisplayName = "VB.NET GUI",
                    .Capabilities = FullGuiCapabilities()
                })
            End Using
        Catch Ex As Exception When IsGrpcEndpointUnavailable(Ex)
            UseRestFallback = True
        End Try
        If UseRestFallback Then
            Await PostJsonAsync(BaseUrl, "/api/instances/connect", New With {
                .clientId = ClientId,
                .role = "controller",
                .mode = "server"
            })
        End If
    End Function

    Public Async Function ConnectDesktopPlayerInstanceAsync(BaseUrl As String) As Task(Of String)
        Using Channel = CreateGrpcChannel(BaseUrl)
            Dim Instances = New InstanceService.InstanceServiceClient(Channel)
            Dim Response = Await Instances.ConnectAsync(New InstanceConnectRequest With {
                .ClientId = "vbnet-desktop-player",
                .Kind = CType(2, InstanceKind),
                .ModId = "omnimix.vbnet.desktop",
                .GameName = "OmniMix 桌面播放器",
                .DisplayName = "OmniMix 桌面播放器",
                .Capabilities = DesktopPlayerCapabilities()
            })
            Return If(Response.InstanceId, "")
        End Using
    End Function

    Public Async Function HeartbeatInstanceAsync(BaseUrl As String, InstanceId As String) As Task(Of Boolean)
        Using Channel = CreateGrpcChannel(BaseUrl)
            Dim Instances = New InstanceService.InstanceServiceClient(Channel)
            Dim Response = Await Instances.HeartbeatAsync(New InstanceHeartbeatRequest With {.InstanceId = If(InstanceId, "")})
            Return Response.Alive
        End Using
    End Function

    Public Async Function DisconnectInstanceAsync(BaseUrl As String, InstanceId As String) As Task
        Using Channel = CreateGrpcChannel(BaseUrl)
            Dim Instances = New InstanceService.InstanceServiceClient(Channel)
            Await Instances.DisconnectAsync(New InstanceDisconnectRequest With {.InstanceId = If(InstanceId, "")})
        End Using
    End Function

    Public Async Function PlayAsync(BaseUrl As String, InstanceId As String, Optional Uuid As String = "") As Task
        Using Channel = CreateGrpcChannel(BaseUrl)
            Dim Playback = New PlaybackService.PlaybackServiceClient(Channel)
            Await Playback.PlayAsync(New PlayRequest With {
                .InstanceId = If(InstanceId, ""),
                .Uuid = If(Uuid, "")
            })
        End Using
    End Function

    Public Async Function AddToQueueAsync(BaseUrl As String, InstanceId As String, Uuid As String) As Task
        If String.IsNullOrWhiteSpace(Uuid) Then Return
        Dim UseInsertFallback = False
        Try
            Await PostQueueSingleAsync(BaseUrl, InstanceId, Uuid)
        Catch Ex As Exception When IsEndpointUnsupported(Ex)
            UseInsertFallback = True
        End Try
        If UseInsertFallback Then Await PostQueueInsertAsync(BaseUrl, InstanceId, New String() {Uuid})
    End Function

    Public Async Function AddToQueueRangeAsync(BaseUrl As String, InstanceId As String, Uuids As IEnumerable(Of String)) As Task
        Dim SongUuids = If(Uuids, Enumerable.Empty(Of String)()).
            Where(Function(Uuid) Not String.IsNullOrWhiteSpace(Uuid)).
            Distinct().
            ToArray()
        If SongUuids.Length = 0 Then Return

        Dim UseSingleFallback = False
        Try
            Await PostQueueInsertAsync(BaseUrl, InstanceId, SongUuids)
        Catch Ex As Exception When IsEndpointUnsupported(Ex)
            UseSingleFallback = True
        End Try
        If UseSingleFallback Then
            For Each SongUuid In SongUuids
                Await PostQueueSingleAsync(BaseUrl, InstanceId, SongUuid)
            Next
        End If
    End Function

    Private Async Function PostQueueSingleAsync(BaseUrl As String, InstanceId As String, Uuid As String) As Task
        Using Channel = CreateGrpcChannel(BaseUrl)
            Dim Playback = New PlaybackService.PlaybackServiceClient(Channel)
            Await Playback.AddToQueueAsync(New AddToQueueRequest With {
                .InstanceId = If(InstanceId, ""),
                .Uuid = If(Uuid, "")
            })
        End Using
    End Function

    Private Async Function PostQueueInsertAsync(BaseUrl As String, InstanceId As String, Uuids As IEnumerable(Of String)) As Task
        Dim SongUuids = If(Uuids, Enumerable.Empty(Of String)()).ToArray()
        Using Channel = CreateGrpcChannel(BaseUrl)
            Dim Playback = New PlaybackService.PlaybackServiceClient(Channel)
            Dim Request As New InsertIntoQueueRequest With {
                .InstanceId = If(InstanceId, ""),
                .Index = Integer.MaxValue
            }
            Request.Uuids.AddRange(SongUuids)
            Await Playback.InsertIntoQueueAsync(Request)
        End Using
    End Function

    Public Async Function RemoveQueueItemAsync(BaseUrl As String, InstanceId As String, Index As Integer) As Task
        Using Channel = CreateGrpcChannel(BaseUrl)
            Dim Playback = New PlaybackService.PlaybackServiceClient(Channel)
            Await Playback.RemoveFromQueueAsync(New RemoveFromQueueRequest With {
                .InstanceId = If(InstanceId, ""),
                .Index = Index
            })
        End Using
    End Function

    Public Async Function MoveQueueItemAsync(BaseUrl As String, InstanceId As String, FromIndex As Integer, ToIndex As Integer) As Task
        Using Channel = CreateGrpcChannel(BaseUrl)
            Dim Playback = New PlaybackService.PlaybackServiceClient(Channel)
            Await Playback.MoveInQueueAsync(New MoveInQueueRequest With {
                .InstanceId = If(InstanceId, ""),
                .FromIndex = FromIndex,
                .ToIndex = ToIndex
            })
        End Using
    End Function

    Public Async Function ClearQueueAsync(BaseUrl As String, InstanceId As String) As Task
        Using Channel = CreateGrpcChannel(BaseUrl)
            Dim Playback = New PlaybackService.PlaybackServiceClient(Channel)
            Await Playback.ClearQueueAsync(New ClearQueueRequest With {.InstanceId = If(InstanceId, "")})
        End Using
    End Function

    Public Async Function RemoveHistoryItemAsync(BaseUrl As String, InstanceId As String, Index As Integer) As Task
        Using Channel = CreateGrpcChannel(BaseUrl)
            Dim Playback = New PlaybackService.PlaybackServiceClient(Channel)
            Await Playback.RemoveFromHistoryAsync(New RemoveFromHistoryRequest With {
                .InstanceId = If(InstanceId, ""),
                .Index = Index
            })
        End Using
    End Function

    Public Async Function MoveHistoryItemAsync(BaseUrl As String, InstanceId As String, FromIndex As Integer, ToIndex As Integer) As Task
        Using Channel = CreateGrpcChannel(BaseUrl)
            Dim Playback = New PlaybackService.PlaybackServiceClient(Channel)
            Await Playback.MoveInHistoryAsync(New MoveInHistoryRequest With {
                .InstanceId = If(InstanceId, ""),
                .FromIndex = FromIndex,
                .ToIndex = ToIndex
            })
        End Using
    End Function

    Public Async Function ClearHistoryAsync(BaseUrl As String, InstanceId As String) As Task
        Using Channel = CreateGrpcChannel(BaseUrl)
            Dim Playback = New PlaybackService.PlaybackServiceClient(Channel)
            Await Playback.ClearHistoryAsync(New ClearHistoryRequest With {.InstanceId = If(InstanceId, "")})
        End Using
    End Function

    Public Async Function SendInstanceCommandAsync(BaseUrl As String, InstanceId As String, Command As String) As Task
        Using Channel = CreateGrpcChannel(BaseUrl)
            Dim Playback = New PlaybackService.PlaybackServiceClient(Channel)
            Select Case If(Command, "").Trim().ToLowerInvariant()
                Case "play"
                    Await Playback.PlayAsync(New PlayRequest With {.InstanceId = If(InstanceId, "")})
                Case "pause"
                    Await Playback.PauseAsync(New PauseRequest With {.InstanceId = If(InstanceId, "")})
                Case "resume"
                    Await Playback.ResumeAsync(New ResumeRequest With {.InstanceId = If(InstanceId, "")})
                Case "toggle"
                    Await Playback.ToggleAsync(New ToggleRequest With {.InstanceId = If(InstanceId, "")})
                Case "next"
                    Await Playback.NextAsync(New NextRequest With {.InstanceId = If(InstanceId, "")})
                Case "prev", "previous"
                    Await Playback.PrevAsync(New PrevRequest With {.InstanceId = If(InstanceId, "")})
                Case "stop"
                    Await Playback.StopAsync(New StopRequest With {.InstanceId = If(InstanceId, "")})
                Case Else
                    Await PostEmptyAsync(BaseUrl, "/api/instances/" & Uri.EscapeDataString(InstanceId) & "/" & Uri.EscapeDataString(Command))
            End Select
        End Using
    End Function

    Public Async Function SeekInstanceAsync(BaseUrl As String, InstanceId As String, Position As Double) As Task
        Using Channel = CreateGrpcChannel(BaseUrl)
            Dim Playback = New PlaybackService.PlaybackServiceClient(Channel)
            Await Playback.SeekAsync(New SeekRequest With {
                .InstanceId = If(InstanceId, ""),
                .Position = CSng(Position)
            })
        End Using
    End Function

    Public Async Function SetInstanceVolumeAsync(BaseUrl As String, InstanceId As String, Volume As Double) As Task
        Volume = Math.Max(0, Math.Min(1, Volume))
        Dim Key = If(InstanceId, "")
        Dim SaveLock = VolumeSaveLocks.GetOrAdd(Key, Function(Ignored) New SemaphoreSlim(1, 1))
        Await SaveLock.WaitAsync()
        Try
            Dim UseProfileFallback = ProfileOnlyVolumeInstances.ContainsKey(Key)
            If Not UseProfileFallback Then
                Try
                    Using Channel = CreateGrpcChannel(BaseUrl)
                        Dim Playback = New PlaybackService.PlaybackServiceClient(Channel)
                        Await Playback.SetVolumeAsync(New SetVolumeRequest With {
                            .InstanceId = Key,
                            .Volume = CSng(Volume)
                        })
                    End Using
                Catch Ex As RpcException When Ex.StatusCode = StatusCode.FailedPrecondition
                    ProfileOnlyVolumeInstances(Key) = True
                    UseProfileFallback = True
                End Try
            End If
            If UseProfileFallback Then
                Using Channel = CreateGrpcChannel(BaseUrl)
                    Dim Instances = New InstanceService.InstanceServiceClient(Channel)
                    Dim Profile = Await Instances.GetProfileAsync(New GetProfileRequest With {.InstanceId = Key})
                    Profile.Volume = CSng(Volume)
                    Await Instances.UpdateProfileAsync(New UpdateProfileRequest With {.Profile = Profile})
                End Using
            End If
        Finally
            SaveLock.Release()
        End Try
    End Function

    Public Async Function SetInstanceLatencyAsync(BaseUrl As String, InstanceId As String, Latency As Double) As Task
        Using Channel = CreateGrpcChannel(BaseUrl)
            Dim Playback = New PlaybackService.PlaybackServiceClient(Channel)
            Await Playback.SetTargetLatencyAsync(New SetTargetLatencyRequest With {
                .InstanceId = If(InstanceId, ""),
                .Latency = CSng(Latency)
            })
        End Using
    End Function

    Public Async Function SetInstanceShuffleAsync(BaseUrl As String, InstanceId As String, Enabled As Boolean) As Task
        Using Channel = CreateGrpcChannel(BaseUrl)
            Dim Playback = New PlaybackService.PlaybackServiceClient(Channel)
            Await Playback.SetShuffleAsync(New SetShuffleRequest With {
                .InstanceId = If(InstanceId, ""),
                .Enabled = Enabled
            })
        End Using
    End Function

    Public Async Function SetInstanceRepeatModeAsync(BaseUrl As String, InstanceId As String, Mode As String) As Task
        Using Channel = CreateGrpcChannel(BaseUrl)
            Dim Playback = New PlaybackService.PlaybackServiceClient(Channel)
            Await Playback.SetRepeatModeAsync(New SetRepeatModeRequest With {
                .InstanceId = If(InstanceId, ""),
                .Mode = RepeatModeFromString(Mode)
            })
        End Using
    End Function

    Public Async Function GetInstanceEqualizerAsync(BaseUrl As String, InstanceId As String) As Task(Of OmniMixEqualizerStateInfo)
        Dim UseRestFallback = False
        Try
            Using Channel = CreateGrpcChannel(BaseUrl)
                Dim Playback = New PlaybackService.PlaybackServiceClient(Channel)
                Dim State = Await Playback.GetEqualizerAsync(New GetEqualizerRequest With {.InstanceId = If(InstanceId, "")})
                Return MapEqualizer(State)
            End Using
        Catch Ex As Exception When IsGrpcEndpointUnavailable(Ex)
            UseRestFallback = True
        End Try
        If UseRestFallback Then
            Try
                Dim State = Await GetJsonAsync(Of OmniMixEqualizerStateInfo)(BaseUrl, "/api/instances/" & Uri.EscapeDataString(InstanceId) & "/equalizer")
                Return If(State, New OmniMixEqualizerStateInfo)
            Catch RestEx As Exception When IsOptionalEndpointMissing(RestEx)
                Return New OmniMixEqualizerStateInfo
            End Try
        End If
        Return New OmniMixEqualizerStateInfo
    End Function

    Public Async Function PutInstanceEqualizerAsync(BaseUrl As String, InstanceId As String, State As OmniMixEqualizerStateInfo) As Task
        Using Channel = CreateGrpcChannel(BaseUrl)
            Dim Playback = New PlaybackService.PlaybackServiceClient(Channel)
            Await Playback.SetEqualizerAsync(New SetEqualizerRequest With {
                .InstanceId = If(InstanceId, ""),
                .State = ToEqualizerState(State)
            })
        End Using
    End Function

    Public Async Function GetInstanceEqualizerPresetsAsync(BaseUrl As String, InstanceId As String) As Task(Of Dictionary(Of String, OmniMixEqualizerStateInfo))
        Try
            Dim Presets = Await GetJsonAsync(Of Dictionary(Of String, OmniMixEqualizerStateInfo))(BaseUrl, "/api/instances/" & Uri.EscapeDataString(InstanceId) & "/equalizer/presets")
            Return If(Presets, New Dictionary(Of String, OmniMixEqualizerStateInfo))
        Catch Ex As Exception When IsOptionalEndpointMissing(Ex)
            Return New Dictionary(Of String, OmniMixEqualizerStateInfo)
        End Try
    End Function

    Public Async Function GetConfigAsync(BaseUrl As String) As Task(Of Dictionary(Of String, JsonElement))
        Dim Config = Await GetJsonAsync(Of Dictionary(Of String, JsonElement))(BaseUrl, "/api/config")
        Return If(Config, New Dictionary(Of String, JsonElement))
    End Function

    Public Async Function PutConfigRawAsync(BaseUrl As String, Updates As Dictionary(Of String, Object)) As Task
        Await PutJsonAsync(BaseUrl, "/api/config", Updates)
    End Function

    Public Async Function SaveConfigAsync(BaseUrl As String) As Task
        Await PostEmptyAsync(BaseUrl, "/api/config/save")
    End Function

    Public Async Function AddPortFileDirAsync(BaseUrl As String, DirectoryPath As String) As Task
        Try
            If String.IsNullOrWhiteSpace(BaseUrl) OrElse String.IsNullOrWhiteSpace(DirectoryPath) Then Return

            Dim FullPath As String
            Try
                FullPath = Path.GetFullPath(DirectoryPath)
            Catch
                FullPath = DirectoryPath.Trim()
            End Try

            Dim Config = Await GetConfigAsync(BaseUrl)
            Dim Dirs As New List(Of String)
            Dim Existing As JsonElement
            If Config.TryGetValue("port_file_dirs", Existing) Then
                AddPortFileDirs(Dirs, Existing)
            End If

            If Not Dirs.Any(Function(Item) ArePathsEqual(Item, FullPath)) Then Dirs.Add(FullPath)

            Await PutConfigRawAsync(BaseUrl, New Dictionary(Of String, Object) From {
                {"port_file_dirs", Dirs.ToArray()}
            })
            Await SaveConfigAsync(BaseUrl)
        Catch Ex As Exception
            Logger.Warn(Ex, "同步 OmniMix 端口文件目录失败，将继续使用游戏目录内的端口文件")
        End Try
    End Function

    Private Sub AddPortFileDirs(Dirs As List(Of String), Value As JsonElement, Optional Depth As Integer = 0)
        If Depth > 32 Then Return
        If Value.ValueKind = JsonValueKind.Array Then
            For Each Item In Value.EnumerateArray()
                AddPortFileDirs(Dirs, Item, Depth + 1)
            Next
            Return
        End If
        If Value.ValueKind <> JsonValueKind.String Then Return

        Dim Text = Value.GetString()
        If String.IsNullOrWhiteSpace(Text) Then Return
        Text = Text.Trim()
        If Text.StartsWith("[", StringComparison.Ordinal) OrElse Text.StartsWith("""", StringComparison.Ordinal) Then
            Try
                Using Document = JsonDocument.Parse(Text)
                    AddPortFileDirs(Dirs, Document.RootElement, Depth + 1)
                    Return
                End Using
            Catch
            End Try
        End If

        If Not Path.IsPathRooted(Text) Then Return
        If Not Dirs.Any(Function(Item) ArePathsEqual(Item, Text)) Then Dirs.Add(Text)
    End Sub

    Public Async Function StopBackendAsync(BaseUrl As String) As Task
        Await PostEmptyAsync(BaseUrl, "/api/backend/stop")
    End Function

    Public Async Function SetActiveInstanceAsync(BaseUrl As String, InstanceId As String) As Task
        Await PutConfigRawAsync(BaseUrl, New Dictionary(Of String, Object) From {
            {"active_instance", If(InstanceId, "")}
        })
        Await SaveConfigAsync(BaseUrl)
    End Function

    Public Async Function GetArchivesAsync(BaseUrl As String) As Task(Of List(Of OmniMixArchiveInfo))
        Dim UseRestFallback = False
        Try
            Using Channel = CreateGrpcChannel(BaseUrl)
                Dim Instances = New InstanceService.InstanceServiceClient(Channel)
                Dim Response = Await Instances.ListArchivesAsync(New ListArchivesRequest())
                Return Response.Archives.Select(AddressOf MapArchive).ToList()
            End Using
        Catch Ex As Exception When IsGrpcEndpointUnavailable(Ex)
            UseRestFallback = True
        End Try
        If UseRestFallback Then
            Try
                Dim Archives = Await GetJsonAsync(Of List(Of OmniMixArchiveInfo))(BaseUrl, "/api/instances/archives")
                Return If(Archives, New List(Of OmniMixArchiveInfo))
            Catch RestEx As Exception When IsOptionalEndpointMissing(RestEx)
                Return New List(Of OmniMixArchiveInfo)
            End Try
        End If
        Return New List(Of OmniMixArchiveInfo)
    End Function

    Public Async Function DeleteInstanceAsync(BaseUrl As String, InstanceId As String) As Task
        Using Channel = CreateGrpcChannel(BaseUrl)
            Dim Instances = New InstanceService.InstanceServiceClient(Channel)
            Await Instances.DeleteInstanceAsync(New DeleteInstanceRequest With {.InstanceId = If(InstanceId, "")})
        End Using
    End Function

    Public Async Function SetInstanceMetaAsync(BaseUrl As String, InstanceId As String, ModId As String, GameName As String, Mode As String) As Task
        Using Channel = CreateGrpcChannel(BaseUrl)
            Dim Instances = New InstanceService.InstanceServiceClient(Channel)
            Dim Profile As InstanceProfile
            Try
                Profile = Await Instances.GetProfileAsync(New GetProfileRequest With {.InstanceId = If(InstanceId, "")})
            Catch
                Profile = New InstanceProfile With {.Id = If(InstanceId, "")}
            End Try
            Profile.ModId = If(ModId, "")
            Profile.GameName = If(GameName, "")
            If String.Equals(Mode, "ServerManaged", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(Mode, "server", StringComparison.OrdinalIgnoreCase) Then
                If Profile.Capabilities Is Nothing Then Profile.Capabilities = New InstanceCapabilities
                Profile.Capabilities.ServerControlledPlayback = True
            End If
            Await Instances.UpdateProfileAsync(New UpdateProfileRequest With {
                .InstanceId = If(InstanceId, ""),
                .Profile = Profile
            })
        End Using
    End Function

    Public Async Function UpdateInstanceProfileAsync(BaseUrl As String, InstanceId As String, Profile As Dictionary(Of String, Object)) As Task
        Using Channel = CreateGrpcChannel(BaseUrl)
            Dim Instances = New InstanceService.InstanceServiceClient(Channel)
            Dim Existing As InstanceProfile
            Try
                Existing = Await Instances.GetProfileAsync(New GetProfileRequest With {.InstanceId = If(InstanceId, "")})
            Catch
                Existing = New InstanceProfile With {.Id = If(InstanceId, "")}
            End Try
            ApplyLegacyProfile(Existing, If(Profile, New Dictionary(Of String, Object)))
            Await Instances.UpdateProfileAsync(New UpdateProfileRequest With {
                .InstanceId = If(InstanceId, ""),
                .Profile = Existing
            })
        End Using
    End Function

    Public Async Function GetInstanceProfileAsync(BaseUrl As String, InstanceId As String) As Task(Of Dictionary(Of String, JsonElement))
        Using Channel = CreateGrpcChannel(BaseUrl)
            Dim Instances = New InstanceService.InstanceServiceClient(Channel)
            Dim Profile = Await Instances.GetProfileAsync(New GetProfileRequest With {.InstanceId = If(InstanceId, "")})
            Return LegacyProfileToJson(Profile)
        End Using
    End Function

    Public Async Function ArchiveInstanceAsync(BaseUrl As String, InstanceId As String, Label As String) As Task
        Using Channel = CreateGrpcChannel(BaseUrl)
            Dim Instances = New InstanceService.InstanceServiceClient(Channel)
            Await Instances.ArchiveInstanceAsync(New ArchiveInstanceRequest With {
                .InstanceId = If(InstanceId, ""),
                .Label = If(Label, "")
            })
        End Using
    End Function

    Public Async Function RenameArchiveAsync(BaseUrl As String, InstanceId As String, Label As String) As Task
        Await PutJsonAsync(BaseUrl, "/api/instances/archives/" & Uri.EscapeDataString(InstanceId) & "/rename", New With {
            .label = If(Label, "")
        })
    End Function

    Public Async Function DeleteArchiveAsync(BaseUrl As String, InstanceId As String) As Task
        Using Channel = CreateGrpcChannel(BaseUrl)
            Dim Instances = New InstanceService.InstanceServiceClient(Channel)
            Await Instances.DeleteArchiveAsync(New DeleteArchiveRequest With {.ArchiveId = If(InstanceId, "")})
        End Using
    End Function

    Public Async Function InheritFromArchiveAsync(BaseUrl As String, InstanceId As String, ArchiveId As String) As Task(Of Dictionary(Of String, JsonElement))
        Using Channel = CreateGrpcChannel(BaseUrl)
            Dim Instances = New InstanceService.InstanceServiceClient(Channel)
            Dim Result = Await Instances.InheritFromArchiveAsync(New InheritFromArchiveRequest With {
                .NewInstanceId = If(InstanceId, ""),
                .ArchiveId = If(ArchiveId, "")
            })
            Return LegacyProfileToJson(Result.Profile)
        End Using
    End Function

    Public Async Function GetModulesAsync(BaseUrl As String) As Task(Of List(Of OmniMixModuleInfo))
        Dim Modules = Await GetJsonAsync(Of List(Of OmniMixModuleInfo))(BaseUrl, "/api/modules")
        Return If(Modules, New List(Of OmniMixModuleInfo))
    End Function

    Public Async Function SetModuleEnabledAsync(BaseUrl As String, ModuleId As String, Enabled As Boolean) As Task
        Dim RequestUrl = BaseUrl.TrimEnd("/"c) & "/api/modules/" & Uri.EscapeDataString(ModuleId)
        Dim Body = JsonSerializer.Serialize(New With {.enabled = Enabled}, JsonOptions)
        Using Content As New StringContent(Body, Encoding.UTF8, "application/json")
            Using Response = Await Client.PostAsync(RequestUrl, Content)
                Response.EnsureSuccessStatusCode()
            End Using
        End Using
    End Function

    Public Async Function GetModuleUiAsync(BaseUrl As String, ModuleId As String) As Task(Of OmniMixRawNodeData)
        Return Await GetJsonAsync(Of OmniMixRawNodeData)(BaseUrl, "/api/modules/" & Uri.EscapeDataString(ModuleId) & "/ui")
    End Function

    Public Async Function GetModuleSettingsUiAsync(BaseUrl As String, ModuleId As String) As Task(Of OmniMixRawNodeData)
        Dim FallbackToMainUi = False
        Try
            Return Await GetJsonAsync(Of OmniMixRawNodeData)(BaseUrl, "/api/modules/" & Uri.EscapeDataString(ModuleId) & "/settings")
        Catch Ex As Exception When IsOptionalEndpointMissing(Ex)
            Logger.Warn(Ex, "模块设置接口不存在，回退到模块主 UI：" & ModuleId)
            FallbackToMainUi = True
        End Try
        If FallbackToMainUi Then Return Await GetModuleUiAsync(BaseUrl, ModuleId)
        Return New OmniMixRawNodeData With {.NodeType = "Text", .Text = "No module settings UI available"}
    End Function

    Public Async Function GetModuleLinkUiAsync(BaseUrl As String, ModuleId As String, LinkId As String) As Task(Of OmniMixRawNodeData)
        Return Await GetJsonAsync(Of OmniMixRawNodeData)(BaseUrl, "/api/modules/" & Uri.EscapeDataString(ModuleId) & "/link/" & Uri.EscapeDataString(LinkId))
    End Function

    Private Function CreateGrpcChannel(BaseUrl As String) As GrpcChannel
        Dim Handler = New GrpcWebHandler(GrpcWebMode.GrpcWeb, New HttpClientHandler())
        Return GrpcChannel.ForAddress(BaseUrl.TrimEnd("/"c), New GrpcChannelOptions With {
            .HttpHandler = Handler,
            .HttpVersion = HttpVersion.Version11,
            .HttpVersionPolicy = HttpVersionPolicy.RequestVersionExact
        })
    End Function

    Private Function MapTag(Tag As Tag) As OmniMixTagInfo
        If Tag Is Nothing Then Return New OmniMixTagInfo
        Return New OmniMixTagInfo With {
            .Id = If(Tag.Id, ""),
            .Name = If(Tag.Name, ""),
            .ModuleId = If(Tag.ModuleId, ""),
            .IsGrowable = Tag.Kind = CType(2, TagKind)
        }
    End Function

    Private Function MapAlbum(Album As Album) As OmniMixAlbumInfo
        If Album Is Nothing Then Return New OmniMixAlbumInfo
        Return New OmniMixAlbumInfo With {
            .Id = If(Album.Id, ""),
            .Name = If(Album.Title, ""),
            .ModuleId = If(Album.ModuleId, ""),
            .CoverPath = If(Album.CoverUri, "")
        }
    End Function

    Private Function MapSong(Track As OmniMixPlayer.SDK.Protos.Models.Track) As OmniMixSongInfo
        If Track Is Nothing Then Return New OmniMixSongInfo
        Return New OmniMixSongInfo With {
            .Uuid = If(Track.Uuid, ""),
            .Title = If(Track.Title, ""),
            .Artist = If(Track.Artist, ""),
            .AlbumId = If(Track.AlbumId, ""),
            .Duration = Track.Duration,
            .ModuleId = If(Track.ModuleId, ""),
            .CoverPath = If(Track.CoverUri, ""),
            .CoverUrl = If(Track.CoverUri, ""),
            .ImageUrl = If(Track.CoverUri, ""),
            .IsFavorite = Track.IsFavorite,
            .IsExcluded = Track.IsExcluded
        }
    End Function

    Private Function MapQueueTrack(Track As QueueTrack) As OmniMixQueueItemInfo
        If Track Is Nothing Then Return New OmniMixQueueItemInfo
        Return New OmniMixQueueItemInfo With {
            .Index = Track.Index,
            .Uuid = If(Track.Uuid, ""),
            .Title = If(Track.Title, ""),
            .Artist = If(Track.Artist, ""),
            .AlbumId = If(Track.AlbumId, ""),
            .Duration = Track.Duration,
            .ModuleId = If(Track.ModuleId, ""),
            .CoverPath = If(Track.CoverUri, ""),
            .CoverUrl = If(Track.CoverUri, ""),
            .ImageUrl = If(Track.CoverUri, "")
        }
    End Function

    Private Function MapInstance(Summary As InstanceSummary, Profile As InstanceProfile, Status As PlaybackStatus) As OmniMixPlaybackInstanceInfo
        Dim Result As New OmniMixPlaybackInstanceInfo With {
            .Id = If(Summary?.Id, ""),
            .ClientId = If(Summary?.Id, ""),
            .Role = KindToString(If(Summary Is Nothing, CType(0, InstanceKind), Summary.Kind)),
            .Mode = "ServerManaged",
            .Attached = If(Summary Is Nothing, False, Summary.IsOnline),
            .QueueCount = If(Summary Is Nothing, 0, Summary.QueueCount),
            .ModId = If(Summary?.ModId, ""),
            .GameName = If(Summary?.GameName, ""),
            .SharedMemoryReady = If(Summary Is Nothing, False, Summary.IsOnline)
        }

        If Profile IsNot Nothing Then
            Result.Volume = Profile.Volume
            Result.CanControlVolume = Profile.Capabilities Is Nothing OrElse Profile.Capabilities.VolumeControl
            Result.TargetLatency = Profile.TargetLatency
            Result.ModId = NonEmpty(Profile.ModId, Result.ModId)
            Result.GameName = NonEmpty(Profile.GameName, Result.GameName)
            Result.Mode = ModeFromCapabilities(Profile.Capabilities)
            If Profile.PlaybackTimeline IsNot Nothing Then
                Result.QueueCount = Math.Max(Result.QueueCount, Profile.PlaybackTimeline.ManualQueueUuids.Count)
                Result.HistoryCount = Profile.PlaybackTimeline.HistoryUuids.Count
                Result.Shuffle = Profile.PlaybackTimeline.Shuffle
                Result.RepeatMode = RepeatModeToString(Profile.PlaybackTimeline.RepeatMode)
            End If
        End If

        If Status IsNot Nothing Then
            Result.IsPlaying = Status.IsPlaying
            Result.Position = Status.Position
            If Profile Is Nothing OrElse Result.CanControlVolume Then Result.Volume = Status.Volume
            Result.Shuffle = Status.Shuffle
            Result.RepeatMode = RepeatModeToString(Status.RepeatMode)
            If Not String.IsNullOrWhiteSpace(Status.TrackUuid) Then
                Result.CurrentTrack = New OmniMixTrackInfo With {
                    .Uuid = Status.TrackUuid,
                    .Title = If(Status.Title, ""),
                    .Artist = If(Status.Artist, ""),
                    .AlbumId = If(Status.AlbumId, ""),
                    .Duration = Status.Duration
                }
            End If
        End If

        Return Result
    End Function

    Private Function MapArchive(Profile As InstanceProfile) As OmniMixArchiveInfo
        If Profile Is Nothing Then Return New OmniMixArchiveInfo
        Return New OmniMixArchiveInfo With {
            .InstanceId = If(Profile.Id, ""),
            .ModId = If(Profile.ModId, ""),
            .Mode = ModeFromCapabilities(Profile.Capabilities),
            .Label = If(Profile.DisplayName, ""),
            .ArchivedAt = TimestampToIso(Profile.UpdatedAt)
        }
    End Function

    Private Function MapEqualizer(State As EqualizerState) As OmniMixEqualizerStateInfo
        Dim Result As New OmniMixEqualizerStateInfo
        If State Is Nothing Then Return Result
        Result.Enabled = State.Enabled
        Result.GlobalGainDb = State.GlobalGainDb
        Result.SoftClipEnabled = State.SoftClipEnabled
        Result.Points = State.Points.Select(Function(Point) New OmniMixEqualizerPointInfo With {
            .Id = If(Point.Id, ""),
            .Frequency = Point.Frequency,
            .GainDb = Point.GainDb,
            .Q = Point.Q,
            .Type = EqualizerTypeToString(Point.Type)
        }).ToList()
        Return Result
    End Function

    Private Function ToEqualizerState(State As OmniMixEqualizerStateInfo) As EqualizerState
        State = If(State, New OmniMixEqualizerStateInfo)
        Dim Result As New EqualizerState With {
            .Enabled = State.Enabled,
            .GlobalGainDb = CSng(State.GlobalGainDb),
            .SoftClipEnabled = State.SoftClipEnabled
        }
        For Each Point In If(State.Points, New List(Of OmniMixEqualizerPointInfo))
            Result.Points.Add(New EqualizerPoint With {
                .Id = If(Point.Id, ""),
                .Frequency = CSng(Point.Frequency),
                .GainDb = CSng(Point.GainDb),
                .Q = CSng(Point.Q),
                .Type = EqualizerTypeFromString(Point.Type)
            })
        Next
        Return Result
    End Function

    Private Function LegacyProfileToJson(Profile As InstanceProfile) As Dictionary(Of String, JsonElement)
        Dim Json = JsonSerializer.Serialize(LegacyProfileToDictionary(Profile), JsonOptions)
        Return JsonSerializer.Deserialize(Of Dictionary(Of String, JsonElement))(Json, JsonOptions)
    End Function

    Private Function LegacyProfileToDictionary(Profile As InstanceProfile) As Dictionary(Of String, Object)
        If Profile Is Nothing Then Profile = New InstanceProfile

        Dim Timeline = If(Profile.PlaybackTimeline, New PlaybackTimelineState)
        Dim ActiveQueueId = "default"

        Dim Sources As New List(Of Object)
        For Each Source In Timeline.PlaylistSources
            Sources.Add(New Dictionary(Of String, Object) From {
                {"Id", If(Source.Id, "")},
                {"Name", If(Source.Name, "")},
                {"Uuids", Source.Uuids.ToList()},
                {"SongCount", Source.Uuids.Count},
                {"Kind", PlaylistSourceKindToString(Source.Kind)},
                {"RefId", If(Source.RefId, "")}
            })
        Next

        Dim Queue As New Dictionary(Of String, Object) From {
            {"Id", ActiveQueueId},
            {"Name", "Default"},
            {"PlaylistSources", Sources},
            {"SongUuids", Timeline.ManualQueueUuids.ToList()},
            {"HistoryUuids", Timeline.HistoryUuids.ToList()},
            {"Index", -1},
            {"HistoryPosition", -1},
            {"PlaylistPosition", Timeline.SourceCursor},
            {"Shuffle", Timeline.Shuffle},
            {"RepeatMode", RepeatModeToString(Timeline.RepeatMode)}
        }

        Return New Dictionary(Of String, Object) From {
            {"Id", If(Profile.Id, "")},
            {"DisplayName", If(Profile.DisplayName, "")},
            {"ModId", If(Profile.ModId, "")},
            {"GameName", If(Profile.GameName, "")},
            {"Mode", ModeFromCapabilities(Profile.Capabilities)},
            {"ActiveQueueId", ActiveQueueId},
            {"Volume", CDbl(Profile.Volume)},
            {"TargetLatency", CDbl(Profile.TargetLatency)},
            {"Queues", New List(Of Object) From {Queue}}
        }
    End Function

    Private Sub ApplyLegacyProfile(Profile As InstanceProfile, Values As Dictionary(Of String, Object))
        If Profile Is Nothing Then Return
        Values = If(Values, New Dictionary(Of String, Object))

        Profile.Volume = CSng(ObjectDouble(Values, "Volume", If(Profile.Volume = 0, 1.0, Profile.Volume)))

        Dim Queues = ObjectList(Values, "Queues")
        Dim ActiveQueue As Dictionary(Of String, Object) = Nothing
        Dim ActiveQueueId = ObjectString(Values, "ActiveQueueId", "default")

        For Each QueueObject In Queues
            Dim QueueValues = ToObjectDictionary(QueueObject)
            If QueueValues Is Nothing Then Continue For
            Dim QueueId = ObjectString(QueueValues, "Id", "default")
            If ActiveQueue Is Nothing OrElse String.Equals(QueueId, ActiveQueueId, StringComparison.OrdinalIgnoreCase) Then
                ActiveQueue = QueueValues
            End If
        Next

        If ActiveQueue Is Nothing Then
            ActiveQueue = New Dictionary(Of String, Object) From {
                {"Id", ActiveQueueId},
                {"Name", "Default"},
                {"SongUuids", New List(Of String)},
                {"HistoryUuids", New List(Of String)}
            }
        End If

        If Profile.PlaybackTimeline Is Nothing Then Profile.PlaybackTimeline = New PlaybackTimelineState
        Profile.PlaybackTimeline.ManualQueueUuids.Clear()
        Profile.PlaybackTimeline.HistoryUuids.Clear()
        Profile.PlaybackTimeline.PlaylistSources.Clear()
        Profile.PlaybackTimeline.ManualQueueUuids.AddRange(ObjectStringList(ActiveQueue, "SongUuids"))
        Profile.PlaybackTimeline.HistoryUuids.AddRange(ObjectStringList(ActiveQueue, "HistoryUuids"))
        Profile.PlaybackTimeline.Shuffle = ObjectBool(ActiveQueue, "Shuffle", Profile.PlaybackTimeline.Shuffle)
        Profile.PlaybackTimeline.RepeatMode = RepeatModeFromString(ObjectString(ActiveQueue, "RepeatMode", RepeatModeToString(Profile.PlaybackTimeline.RepeatMode)))
        Profile.PlaybackTimeline.Version = Math.Max(Profile.PlaybackTimeline.Version, 1)

        For Each SourceObject In ObjectList(ActiveQueue, "PlaylistSources")
            Dim SourceValues = ToObjectDictionary(SourceObject)
            If SourceValues Is Nothing Then Continue For
            Dim Source As New PlaylistSourceState With {
                .Id = ObjectString(SourceValues, "Id", ""),
                .Name = ObjectString(SourceValues, "Name", ""),
                .Kind = PlaylistSourceKindFromString(ObjectString(SourceValues, "Kind", "track")),
                .RefId = ObjectString(SourceValues, "RefId", "")
            }
            Source.Uuids.AddRange(ObjectStringList(SourceValues, "Uuids"))
            Profile.PlaybackTimeline.PlaylistSources.Add(Source)
        Next
    End Sub

    Private Function ObjectString(Values As Dictionary(Of String, Object), Key As String, Fallback As String) As String
        Dim Value As Object = Nothing
        If Not TryGetObjectValue(Values, Key, Value) OrElse Value Is Nothing Then Return Fallback
        If TypeOf Value Is JsonElement Then
            Dim Element = DirectCast(Value, JsonElement)
            If Element.ValueKind = JsonValueKind.String Then Return NonEmpty(Element.GetString(), Fallback)
            Return NonEmpty(Element.ToString(), Fallback)
        End If
        Return NonEmpty(Convert.ToString(Value), Fallback)
    End Function

    Private Function ObjectDouble(Values As Dictionary(Of String, Object), Key As String, Fallback As Double) As Double
        Dim Value As Object = Nothing
        If Not TryGetObjectValue(Values, Key, Value) OrElse Value Is Nothing Then Return Fallback
        Try
            If TypeOf Value Is JsonElement Then
                Dim Element = DirectCast(Value, JsonElement)
                If Element.ValueKind = JsonValueKind.Number Then Return Element.GetDouble()
                If Element.ValueKind = JsonValueKind.String Then Return Double.Parse(Element.GetString())
            End If
            Return Convert.ToDouble(Value)
        Catch
            Return Fallback
        End Try
    End Function

    Private Function ObjectBool(Values As Dictionary(Of String, Object), Key As String, Fallback As Boolean) As Boolean
        Dim Value As Object = Nothing
        If Not TryGetObjectValue(Values, Key, Value) OrElse Value Is Nothing Then Return Fallback
        Try
            If TypeOf Value Is JsonElement Then
                Dim Element = DirectCast(Value, JsonElement)
                If Element.ValueKind = JsonValueKind.True OrElse Element.ValueKind = JsonValueKind.False Then Return Element.GetBoolean()
                If Element.ValueKind = JsonValueKind.String Then Return Boolean.Parse(Element.GetString())
            End If
            Return Convert.ToBoolean(Value)
        Catch
            Return Fallback
        End Try
    End Function

    Private Function ObjectList(Values As Dictionary(Of String, Object), Key As String) As List(Of Object)
        Dim Value As Object = Nothing
        If Not TryGetObjectValue(Values, Key, Value) OrElse Value Is Nothing Then Return New List(Of Object)
        If TypeOf Value Is JsonElement Then
            Dim Element = DirectCast(Value, JsonElement)
            If Element.ValueKind <> JsonValueKind.Array Then Return New List(Of Object)
            Return Element.EnumerateArray().Cast(Of Object)().ToList()
        End If
        Dim ObjectEnumerable = TryCast(Value, IEnumerable(Of Object))
        If ObjectEnumerable IsNot Nothing Then Return ObjectEnumerable.ToList()
        Return New List(Of Object)
    End Function

    Private Function ObjectStringList(Values As Dictionary(Of String, Object), Key As String) As List(Of String)
        Dim Value As Object = Nothing
        If Not TryGetObjectValue(Values, Key, Value) OrElse Value Is Nothing Then Return New List(Of String)
        If TypeOf Value Is JsonElement Then
            Dim Element = DirectCast(Value, JsonElement)
            If Element.ValueKind <> JsonValueKind.Array Then Return New List(Of String)
            Return Element.EnumerateArray().
                Where(Function(Item) Item.ValueKind = JsonValueKind.String).
                Select(Function(Item) Item.GetString()).
                Where(Function(Item) Not String.IsNullOrWhiteSpace(Item)).
                ToList()
        End If
        Dim StringEnumerable = TryCast(Value, IEnumerable(Of String))
        If StringEnumerable IsNot Nothing Then Return StringEnumerable.Where(Function(Item) Not String.IsNullOrWhiteSpace(Item)).ToList()
        Dim ObjectEnumerable = TryCast(Value, IEnumerable(Of Object))
        If ObjectEnumerable IsNot Nothing Then
            Return ObjectEnumerable.Select(Function(Item) Convert.ToString(Item)).
                Where(Function(Item) Not String.IsNullOrWhiteSpace(Item)).
                ToList()
        End If
        Return New List(Of String)
    End Function

    Private Function ToObjectDictionary(Value As Object) As Dictionary(Of String, Object)
        If Value Is Nothing Then Return Nothing
        Dim Existing = TryCast(Value, Dictionary(Of String, Object))
        If Existing IsNot Nothing Then Return Existing
        If TypeOf Value Is JsonElement Then
            Dim Element = DirectCast(Value, JsonElement)
            If Element.ValueKind <> JsonValueKind.Object Then Return Nothing
            Return JsonSerializer.Deserialize(Of Dictionary(Of String, Object))(Element.GetRawText(), JsonOptions)
        End If
        Return Nothing
    End Function

    Private Function TryGetObjectValue(Values As Dictionary(Of String, Object), Key As String, ByRef Value As Object) As Boolean
        If Values Is Nothing Then Return False
        If Values.TryGetValue(Key, Value) Then Return True
        Dim Match = Values.FirstOrDefault(Function(Item) String.Equals(Item.Key, Key, StringComparison.OrdinalIgnoreCase))
        If String.IsNullOrEmpty(Match.Key) Then Return False
        Value = Match.Value
        Return True
    End Function

    Private Function NonEmpty(Value As String, Fallback As String) As String
        Return If(String.IsNullOrWhiteSpace(Value), Fallback, Value)
    End Function

    Private Function ModeFromCapabilities(Capabilities As InstanceCapabilities) As String
        If Capabilities IsNot Nothing AndAlso Capabilities.ServerControlledPlayback Then Return "ServerManaged"
        Return "ClientManaged"
    End Function

    Private Function FullGuiCapabilities() As InstanceCapabilities
        Return New InstanceCapabilities With {
            .ServerControlledPlayback = True,
            .QueueManagement = True,
            .PlaylistManagement = True,
            .MultiplePlaylists = True,
            .TagFiltering = True,
            .UnlimitedTags = True,
            .AlbumFiltering = True,
            .Shuffle = True,
            .Repeat = True,
            .Seek = True,
            .VolumeControl = True,
            .Equalizer = True,
            .AudioPlayback = False
        }
    End Function

    Private Function DesktopPlayerCapabilities() As InstanceCapabilities
        Return New InstanceCapabilities With {
            .ServerControlledPlayback = True,
            .QueueManagement = True,
            .PlaylistManagement = True,
            .MultiplePlaylists = True,
            .Shuffle = True,
            .Repeat = True,
            .Seek = True,
            .VolumeControl = True,
            .Equalizer = True,
            .AudioPlayback = True,
            .CustomSystemMediaService = True
        }
    End Function

    Private Function KindToString(Kind As InstanceKind) As String
        Select Case CInt(Kind)
            Case 1
                Return "GameMod"
            Case 2
                Return "Gui"
            Case 3
                Return "ExternalClient"
            Case 4
                Return "Observer"
            Case Else
                Return ""
        End Select
    End Function

    Private Function RepeatModeFromString(Mode As String) As RepeatMode
        Select Case If(Mode, "").Trim().ToLowerInvariant()
            Case "one"
                Return CType(2, RepeatMode)
            Case "all"
                Return CType(3, RepeatMode)
            Case Else
                Return CType(1, RepeatMode)
        End Select
    End Function

    Private Function RepeatModeToString(Mode As RepeatMode) As String
        Select Case CInt(Mode)
            Case 2
                Return "one"
            Case 3
                Return "all"
            Case Else
                Return "none"
        End Select
    End Function

    Private Function EqualizerTypeFromString(FilterType As String) As EqualizerFilterType
        Select Case If(FilterType, "").Trim().ToLowerInvariant()
            Case "lowshelf", "low_shelf", "low-shelf"
                Return CType(2, EqualizerFilterType)
            Case "highshelf", "high_shelf", "high-shelf"
                Return CType(3, EqualizerFilterType)
            Case "lowpass", "low_pass", "low-pass"
                Return CType(4, EqualizerFilterType)
            Case "highpass", "high_pass", "high-pass"
                Return CType(5, EqualizerFilterType)
            Case Else
                Return CType(1, EqualizerFilterType)
        End Select
    End Function

    Private Function EqualizerTypeToString(FilterType As EqualizerFilterType) As String
        Select Case CInt(FilterType)
            Case 2
                Return "LowShelf"
            Case 3
                Return "HighShelf"
            Case 4
                Return "LowPass"
            Case 5
                Return "HighPass"
            Case Else
                Return "Peaking"
        End Select
    End Function

    Private Function PlaylistSourceKindFromString(Kind As String) As PlaylistSourceKind
        Select Case If(Kind, "").Trim().ToLowerInvariant()
            Case "tag"
                Return CType(1, PlaylistSourceKind)
            Case "album"
                Return CType(2, PlaylistSourceKind)
            Case "playlist"
                Return CType(3, PlaylistSourceKind)
            Case Else
                Return CType(4, PlaylistSourceKind)
        End Select
    End Function

    Private Function PlaylistSourceKindToString(Kind As PlaylistSourceKind) As String
        Select Case CInt(Kind)
            Case 1
                Return "tag"
            Case 2
                Return "album"
            Case 3
                Return "playlist"
            Case Else
                Return "track"
        End Select
    End Function

    Private Function TimestampToIso(Timestamp As OmniTimestamp) As String
        If Timestamp Is Nothing OrElse Timestamp.Seconds <= 0 Then Return ""
        Try
            Return DateTimeOffset.FromUnixTimeSeconds(Timestamp.Seconds).UtcDateTime.ToString("O")
        Catch
            Return ""
        End Try
    End Function

    Private Async Function PostJsonAsync(BaseUrl As String, ApiPath As String, BodyObject As Object) As Task
        Dim RequestUrl = BaseUrl.TrimEnd("/"c) & ApiPath
        Dim Body = JsonSerializer.Serialize(BodyObject, JsonOptions)
        Using Content As New StringContent(Body, Encoding.UTF8, "application/json")
            Using Response = Await Client.PostAsync(RequestUrl, Content)
                Response.EnsureSuccessStatusCode()
            End Using
        End Using
    End Function

    Private Async Function PostJsonForResultAsync(Of T)(BaseUrl As String, ApiPath As String, BodyObject As Object) As Task(Of T)
        Dim RequestUrl = BaseUrl.TrimEnd("/"c) & ApiPath
        Dim Body = JsonSerializer.Serialize(BodyObject, JsonOptions)
        Using Content As New StringContent(Body, Encoding.UTF8, "application/json")
            Using Response = Await Client.PostAsync(RequestUrl, Content)
                Response.EnsureSuccessStatusCode()
                Dim Json = Await Response.Content.ReadAsStringAsync()
                Return JsonSerializer.Deserialize(Of T)(Json, JsonOptions)
            End Using
        End Using
    End Function

    Private Async Function PostEmptyAsync(BaseUrl As String, ApiPath As String) As Task
        Dim RequestUrl = BaseUrl.TrimEnd("/"c) & ApiPath
        Using Content As New StringContent("", Encoding.UTF8, "application/json")
            Using Response = Await Client.PostAsync(RequestUrl, Content)
                Response.EnsureSuccessStatusCode()
            End Using
        End Using
    End Function

    Private Async Function PostEmptyForResultAsync(Of T)(BaseUrl As String, ApiPath As String) As Task(Of T)
        Dim RequestUrl = BaseUrl.TrimEnd("/"c) & ApiPath
        Using Content As New StringContent("", Encoding.UTF8, "application/json")
            Using Response = Await Client.PostAsync(RequestUrl, Content)
                Response.EnsureSuccessStatusCode()
                Dim Json = Await Response.Content.ReadAsStringAsync()
                Return JsonSerializer.Deserialize(Of T)(Json, JsonOptions)
            End Using
        End Using
    End Function

    Private Async Function DeleteAsync(BaseUrl As String, ApiPath As String) As Task
        Dim RequestUrl = BaseUrl.TrimEnd("/"c) & ApiPath
        Using Response = Await Client.DeleteAsync(RequestUrl)
            Response.EnsureSuccessStatusCode()
        End Using
    End Function

    Private Async Function PutJsonAsync(BaseUrl As String, ApiPath As String, BodyObject As Object) As Task
        Dim RequestUrl = BaseUrl.TrimEnd("/"c) & ApiPath
        Dim Body = JsonSerializer.Serialize(BodyObject, JsonOptions)
        Using Content As New StringContent(Body, Encoding.UTF8, "application/json")
            Using Response = Await Client.PutAsync(RequestUrl, Content)
                Response.EnsureSuccessStatusCode()
            End Using
        End Using
    End Function

    Private Async Function GetJsonAsync(Of T)(BaseUrl As String, ApiPath As String) As Task(Of T)
        Dim RequestUrl = BaseUrl.TrimEnd("/"c) & ApiPath
        Using Response = Await Client.GetAsync(RequestUrl)
            Response.EnsureSuccessStatusCode()
            Dim Json = Await Response.Content.ReadAsStringAsync()
            Return JsonSerializer.Deserialize(Of T)(Json, JsonOptions)
        End Using
    End Function

    Private Async Function CheckHealthAsync(CandidatePort As Integer) As Task(Of Boolean)
        Try
            Using Response = Await Client.GetAsync($"http://127.0.0.1:{CandidatePort}/api/health")
                Return Response.IsSuccessStatusCode
            End Using
        Catch
            Return False
        End Try
    End Function

    Private Function ReadPortFile() As Integer?
        For Each DirectoryPath In GetPortFileDirs()
            If String.IsNullOrWhiteSpace(DirectoryPath) Then Continue For
            Try
                Dim FilePath = Path.Combine(DirectoryPath, "omnimix_port.txt")
                If Not FileUtils.Exists(FilePath) Then Continue For
                Dim Raw = File.ReadAllText(FilePath).Trim()
                Dim ParsedPort As Integer
                If Integer.TryParse(Raw, ParsedPort) AndAlso ParsedPort > 0 AndAlso ParsedPort < 65536 Then Return ParsedPort
            Catch
            End Try
        Next
        Return Nothing
    End Function

    Private Function GetPortFileDirs() As IEnumerable(Of String)
        Dim Dirs As New List(Of String) From {
            PathExeFolder,
            Path.GetTempPath()
        }

        Dim PublicDir = Environment.GetEnvironmentVariable("PUBLIC")
        If Not String.IsNullOrWhiteSpace(PublicDir) Then Dirs.Insert(1, Path.Combine(PublicDir, "OmniMixPlayer"))

        Return Dirs.Distinct(StringComparer.OrdinalIgnoreCase)
    End Function

    Private Function ArePathsEqual(PathA As String, PathB As String) As Boolean
        Try
            Return String.Equals(Path.GetFullPath(PathA).TrimEnd("\"c, "/"c), Path.GetFullPath(PathB).TrimEnd("\"c, "/"c), StringComparison.OrdinalIgnoreCase)
        Catch
            Return String.Equals(If(PathA, "").TrimEnd("\"c, "/"c), If(PathB, "").TrimEnd("\"c, "/"c), StringComparison.OrdinalIgnoreCase)
        End Try
    End Function

    Private Function IsOptionalEndpointMissing(Ex As Exception) As Boolean
        Dim HttpEx = TryCast(Ex, HttpRequestException)
        If HttpEx IsNot Nothing AndAlso HttpEx.StatusCode = HttpStatusCode.NotFound Then Return True
        Dim GrpcEx = TryCast(Ex, RpcException)
        If GrpcEx IsNot Nothing AndAlso GrpcEx.StatusCode = StatusCode.NotFound Then Return True
        Return False
    End Function

    Private Function IsEndpointUnsupported(Ex As Exception) As Boolean
        If IsGrpcEndpointUnavailable(Ex) Then Return True
        Dim HttpEx = TryCast(Ex, HttpRequestException)
        If HttpEx Is Nothing Then Return False
        Return HttpEx.StatusCode = HttpStatusCode.NotFound OrElse
               HttpEx.StatusCode = HttpStatusCode.MethodNotAllowed OrElse
               HttpEx.StatusCode = HttpStatusCode.NotImplemented
    End Function

    Private Function IsGrpcEndpointUnavailable(Ex As Exception) As Boolean
        Dim GrpcEx = TryCast(Ex, RpcException)
        If GrpcEx IsNot Nothing Then
            Return GrpcEx.StatusCode = StatusCode.Unimplemented OrElse
                   GrpcEx.StatusCode = StatusCode.Unavailable OrElse
                   GrpcEx.StatusCode = StatusCode.NotFound
        End If

        Dim HttpEx = TryCast(Ex, HttpRequestException)
        If HttpEx IsNot Nothing Then
            Return Not HttpEx.StatusCode.HasValue OrElse
                   HttpEx.StatusCode = HttpStatusCode.NotFound OrElse
                   HttpEx.StatusCode = HttpStatusCode.MethodNotAllowed OrElse
                   HttpEx.StatusCode = HttpStatusCode.NotImplemented OrElse
                   HttpEx.StatusCode = HttpStatusCode.UnsupportedMediaType
        End If

        Return False
    End Function

End Module
