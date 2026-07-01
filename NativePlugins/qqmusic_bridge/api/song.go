package api

import (
	"encoding/base64"
	"encoding/json"
	"fmt"
	"qqmusic_bridge/crypto"
	"qqmusic_bridge/models"
	"regexp"
	"strconv"
	"strings"
	"time"
)

// GetSongURLFCG tries to get song URL using FCG API (2025.9 format)
func (c *Client) GetSongURLFCG(songMid string, quality models.AudioQuality) (*models.SongURL, error) {
	c.mu.RLock()
	cookies := c.cookies
	uin := c.uin
	c.mu.RUnlock()

	debugLog("[GetSongURLFCG] Getting URL for %s, quality=%s, uin=%d", songMid, quality, uin)

	// Build filename with quality prefix to request specific quality
	prefix := quality.GetFilePrefix()
	ext := quality.GetFileExt()
	filename := fmt.Sprintf("%s%s.%s", prefix, songMid, ext)
	debugLog("[GetSongURLFCG] Requesting filename: %s", filename)

	// Request with filename to specify quality
	reqData := map[string]interface{}{
		"req_1": map[string]interface{}{
			"module": "vkey.GetVkeyServer",
			"method": "CgiGetVkey",
			"param": map[string]interface{}{
				"filename":  []string{filename},
				"guid":      c.guid,
				"songmid":   []string{songMid},
				"songtype":  []int{0},
				"uin":       fmt.Sprintf("%d", uin),
				"loginflag": 1,
				"platform":  "20",
			},
		},
		"comm": map[string]interface{}{
			"format": "json",
			"uin":    uin,
			"ct":     24,
			"cv":     0,
		},
	}

	jsonData, err := json.Marshal(reqData)
	if err != nil {
		return nil, err
	}

	debugLog("[GetSongURLFCG] Request body: %s", string(jsonData))

	resp, err := c.httpClient.R().
		SetHeader("Cookie", cookies).
		SetHeader("Referer", "https://y.qq.com/").
		SetHeader("Origin", "https://y.qq.com").
		SetHeader("Content-Type", "application/json;charset=UTF-8").
		SetHeader("Accept", "application/json, text/plain, */*").
		SetHeader("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8").
		SetBody(jsonData).
		Post("https://u.y.qq.com/cgi-bin/musicu.fcg")

	if err != nil {
		debugLog("[GetSongURLFCG] Request error: %v", err)
		return nil, err
	}

	debugLog("[GetSongURLFCG] Response: %s", string(resp.Body()[:min(500, len(resp.Body()))]))

	var result struct {
		Code int `json:"code"`
		Req1 struct {
			Code int `json:"code"`
			Data struct {
				Msg        string   `json:"msg"`
				Sip        []string `json:"sip"`
				Midurlinfo []struct {
					Purl     string `json:"purl"`
					Songmid  string `json:"songmid"`
					Filename string `json:"filename"`
				} `json:"midurlinfo"`
			} `json:"data"`
		} `json:"req_1"`
	}

	if err := json.Unmarshal(resp.Body(), &result); err != nil {
		debugLog("[GetSongURLFCG] Parse error: %v", err)
		return nil, fmt.Errorf("failed to parse response: %w", err)
	}

	if result.Req1.Code != 0 {
		debugLog("[GetSongURLFCG] Req1 error code: %d", result.Req1.Code)
		return nil, fmt.Errorf("FCG API error: %d", result.Req1.Code)
	}

	// Check if server indicates file not found (404) in message
	if strings.Contains(result.Req1.Data.Msg, "404") {
		debugLog("[GetSongURLFCG] Server indicates 404 in msg: %s", result.Req1.Data.Msg)
		return nil, fmt.Errorf("file not available (404)")
	}

	if len(result.Req1.Data.Midurlinfo) == 0 || result.Req1.Data.Midurlinfo[0].Purl == "" {
		debugLog("[GetSongURLFCG] No purl in response")
		return nil, fmt.Errorf("no URL available")
	}

	// Use sip server from response, fallback to default
	serverURL := "https://ws.stream.qqmusic.qq.com"
	if len(result.Req1.Data.Sip) > 0 && result.Req1.Data.Sip[0] != "" {
		serverURL = strings.TrimSuffix(result.Req1.Data.Sip[0], "/")
	}

	purl := result.Req1.Data.Midurlinfo[0].Purl
	fullURL := serverURL + "/" + purl
	debugLog("[GetSongURLFCG] Got URL: %s", fullURL)

	// Detect actual format from the returned URL
	actualExt := "m4a" // default
	if strings.Contains(purl, ".m4a") {
		actualExt = "m4a"
	} else if strings.Contains(purl, ".mp3") {
		actualExt = "mp3"
	} else if strings.Contains(purl, ".flac") {
		actualExt = "flac"
	}
	debugLog("[GetSongURLFCG] Detected format: %s", actualExt)

	return &models.SongURL{
		Mid:     songMid,
		URL:     fullURL,
		Quality: string(quality),
		Format:  actualExt,
	}, nil
}

// GetSongURL gets the streaming URL for a song
func (c *Client) GetSongURL(songMid string, quality models.AudioQuality) (*models.SongURL, error) {
	// Try FCG API first
	url, err := c.GetSongURLFCG(songMid, quality)
	if err == nil && url != nil && url.URL != "" {
		return url, nil
	}
	debugLog("[GetSongURL] FCG API failed: %v, trying CGI API", err)

	uin := c.GetUIN()
	guid := c.GetGUID()

	// Build filename based on quality
	prefix := quality.GetFilePrefix()
	ext := quality.GetFileExt()
	filename := fmt.Sprintf("%s%s.%s", prefix, songMid, ext)

	params := map[string]interface{}{
		"filename":  []string{filename},
		"guid":      guid,
		"songmid":   []string{songMid},
		"songtype":  []int{0},
		"uin":       fmt.Sprintf("%d", uin),
		"loginflag": 1,
		"platform":  "20",
	}

	data, err := c.RequestCGI("music.vkey.GetVkey", "GetVkey", params)
	if err != nil {
		return nil, fmt.Errorf("failed to get song URL: %w", err)
	}

	var result struct {
		Sip        []string `json:"sip"`
		Midurlinfo []struct {
			Purl     string `json:"purl"`
			Songmid  string `json:"songmid"`
			Filename string `json:"filename"`
		} `json:"midurlinfo"`
	}

	if err := json.Unmarshal(data, &result); err != nil {
		return nil, fmt.Errorf("failed to parse song URL: %w", err)
	}

	if len(result.Midurlinfo) == 0 || result.Midurlinfo[0].Purl == "" {
		return nil, fmt.Errorf("no URL available for this quality, may need VIP")
	}

	// Get a server URL
	serverURL := StreamURL
	if len(result.Sip) > 0 && result.Sip[0] != "" {
		serverURL = strings.TrimSuffix(result.Sip[0], "/")
	}

	purl := result.Midurlinfo[0].Purl
	fullURL := serverURL + "/" + purl

	// Detect actual format from the returned URL (server may return different format)
	actualExt := ext
	if strings.Contains(purl, ".m4a") {
		actualExt = "m4a"
	} else if strings.Contains(purl, ".mp3") {
		actualExt = "mp3"
	} else if strings.Contains(purl, ".flac") {
		actualExt = "flac"
	}
	if actualExt != ext {
		debugLog("[GetSongURL] Format mismatch: requested %s, got %s", ext, actualExt)
	}

	return &models.SongURL{
		Mid:     songMid,
		URL:     fullURL,
		Quality: string(quality),
		Format:  actualExt,
	}, nil
}

// GetSongURLWithFallback tries to get song URL with quality fallback
// Strategy: VIP mode first (for VIP-only songs), then guest mode (for stability)
func (c *Client) GetSongURLWithFallback(songMid string, preferredQuality models.AudioQuality) (*models.SongURL, error) {
	debugLog("[GetSongURLWithFallback] Getting URL for %s (preferred: %s)", songMid, preferredQuality)

	// Step 1: Try VIP mode first (some songs require VIP even for 128k)
	url, err := c.GetSongURLAutoVIP(songMid)
	if err == nil && url.URL != "" {
		debugLog("[GetSongURLWithFallback] VIP mode succeeded")
		return url, nil
	}
	debugLog("[GetSongURLWithFallback] VIP mode failed: %v, trying guest mode...", err)

	// Step 2: Fallback to guest mode (more stable for free songs)
	url, err = c.GetSongURLAuto(songMid)
	if err == nil && url.URL != "" {
		debugLog("[GetSongURLWithFallback] Guest mode succeeded")
		return url, nil
	}

	return nil, fmt.Errorf("failed to get song URL (both VIP and guest mode failed): %v", err)
}

// GetSongURLAutoVIP gets song URL using VIP mode (real uin)
// Some songs require VIP even for 128k quality
// Does NOT specify filename - let server auto-select to avoid CDN 404
func (c *Client) GetSongURLAutoVIP(songMid string) (*models.SongURL, error) {
	c.mu.RLock()
	cookies := c.cookies
	uin := c.uin
	c.mu.RUnlock()

	debugLog("[GetSongURLAutoVIP] Getting URL for %s (VIP mode, uin=%d)", songMid, uin)

	reqData := map[string]interface{}{
		"req_1": map[string]interface{}{
			"module": "vkey.GetVkeyServer",
			"method": "CgiGetVkey",
			"param": map[string]interface{}{
				"guid":      c.guid,
				"songmid":   []string{songMid},
				"songtype":  []int{0},
				"uin":       fmt.Sprintf("%d", uin),
				"loginflag": 1,
				"platform":  "20",
			},
		},
		"comm": map[string]interface{}{
			"format": "json",
			"uin":    uin,
			"ct":     24,
			"cv":     0,
		},
	}

	jsonData, err := json.Marshal(reqData)
	if err != nil {
		return nil, err
	}

	debugLog("[GetSongURLAutoVIP] Request body: %s", string(jsonData))

	resp, err := c.httpClient.R().
		SetHeader("Cookie", cookies).
		SetHeader("Referer", "https://y.qq.com/").
		SetHeader("Origin", "https://y.qq.com").
		SetHeader("Content-Type", "application/json;charset=UTF-8").
		SetBody(jsonData).
		Post("https://u.y.qq.com/cgi-bin/musicu.fcg")

	if err != nil {
		return nil, err
	}

	debugLog("[GetSongURLAutoVIP] Response: %s", string(resp.Body()[:min(500, len(resp.Body()))]))

	var result struct {
		Code int `json:"code"`
		Req1 struct {
			Code int `json:"code"`
			Data struct {
				Msg        string   `json:"msg"`
				Sip        []string `json:"sip"`
				MidURLInfo []struct {
					Purl     string `json:"purl"`
					FileName string `json:"filename"`
				} `json:"midurlinfo"`
			} `json:"data"`
		} `json:"req_1"`
	}

	if err := json.Unmarshal(resp.Body(), &result); err != nil {
		return nil, err
	}

	if result.Code != 0 || result.Req1.Code != 0 {
		return nil, fmt.Errorf("API error: code=%d, req1.code=%d", result.Code, result.Req1.Code)
	}

	if len(result.Req1.Data.MidURLInfo) == 0 || result.Req1.Data.MidURLInfo[0].Purl == "" {
		debugLog("[GetSongURLAutoVIP] No purl in response")
		return nil, fmt.Errorf("no URL available (VIP mode)")
	}

	purl := result.Req1.Data.MidURLInfo[0].Purl

	// Detect format from purl
	ext := "m4a"
	if strings.Contains(purl, ".mp3") {
		ext = "mp3"
	} else if strings.Contains(purl, ".flac") {
		ext = "flac"
	}

	baseURL := "http://aqqmusic.tc.qq.com/"
	if len(result.Req1.Data.Sip) > 0 && result.Req1.Data.Sip[0] != "" {
		baseURL = result.Req1.Data.Sip[0]
	}

	fullURL := baseURL + purl
	debugLog("[GetSongURLAutoVIP] Got URL: %s", fullURL)
	debugLog("[GetSongURLAutoVIP] Detected format: %s", ext)

	return &models.SongURL{
		URL:     fullURL,
		Quality: "128",
		Format:  ext,
	}, nil
}

// GetSongURLAuto gets song URL without specifying quality (guest mode for stability)
func (c *Client) GetSongURLAuto(songMid string) (*models.SongURL, error) {
	c.mu.RLock()
	cookies := c.cookies
	c.mu.RUnlock()

	// Use guest mode (uin=0) for maximum stability
	// High quality downloads require APP-specific auth that we can't replicate
	uin := int64(0)
	debugLog("[GetSongURLAuto] Getting URL for %s (guest mode, uin=0)", songMid)

	// Request without filename - let server auto-select best available quality for this user
	reqData := map[string]interface{}{
		"req_1": map[string]interface{}{
			"module": "vkey.GetVkeyServer",
			"method": "CgiGetVkey",
			"param": map[string]interface{}{
				"guid":      c.guid,
				"songmid":   []string{songMid},
				"songtype":  []int{0},
				"uin":       fmt.Sprintf("%d", uin),
				"loginflag": 1,
				"platform":  "20",
			},
		},
		"comm": map[string]interface{}{
			"format": "json",
			"uin":    uin,
			"ct":     24,
			"cv":     0,
		},
	}

	jsonData, err := json.Marshal(reqData)
	if err != nil {
		return nil, err
	}

	debugLog("[GetSongURLAuto] Request body: %s", string(jsonData))

	resp, err := c.httpClient.R().
		SetHeader("Cookie", cookies).
		SetHeader("Referer", "https://y.qq.com/").
		SetHeader("Origin", "https://y.qq.com").
		SetHeader("Content-Type", "application/json;charset=UTF-8").
		SetBody(jsonData).
		Post("https://u.y.qq.com/cgi-bin/musicu.fcg")

	if err != nil {
		return nil, err
	}

	debugLog("[GetSongURLAuto] Response: %s", string(resp.Body()[:min(500, len(resp.Body()))]))

	var result struct {
		Code int `json:"code"`
		Req1 struct {
			Code int `json:"code"`
			Data struct {
				Msg        string   `json:"msg"`
				Sip        []string `json:"sip"`
				Midurlinfo []struct {
					Purl     string `json:"purl"`
					Songmid  string `json:"songmid"`
					Filename string `json:"filename"`
				} `json:"midurlinfo"`
			} `json:"data"`
		} `json:"req_1"`
	}

	if err := json.Unmarshal(resp.Body(), &result); err != nil {
		return nil, fmt.Errorf("failed to parse response: %w", err)
	}

	if result.Req1.Code != 0 {
		return nil, fmt.Errorf("FCG API error: %d", result.Req1.Code)
	}

	if len(result.Req1.Data.Midurlinfo) == 0 || result.Req1.Data.Midurlinfo[0].Purl == "" {
		debugLog("[GetSongURLAuto] No purl in response")
		return nil, fmt.Errorf("no URL available")
	}

	serverURL := "https://ws.stream.qqmusic.qq.com"
	if len(result.Req1.Data.Sip) > 0 && result.Req1.Data.Sip[0] != "" {
		serverURL = strings.TrimSuffix(result.Req1.Data.Sip[0], "/")
	}

	purl := result.Req1.Data.Midurlinfo[0].Purl
	fullURL := serverURL + "/" + purl
	debugLog("[GetSongURLAuto] Got URL: %s", fullURL)

	// Detect format from URL
	actualExt := "m4a"
	if strings.Contains(purl, ".m4a") {
		actualExt = "m4a"
	} else if strings.Contains(purl, ".mp3") {
		actualExt = "mp3"
	} else if strings.Contains(purl, ".flac") {
		actualExt = "flac"
	}
	debugLog("[GetSongURLAuto] Detected format: %s", actualExt)

	return &models.SongURL{
		Mid:     songMid,
		URL:     fullURL,
		Quality: "auto",
		Format:  actualExt,
	}, nil
}

// GetSongInfo gets detailed information about a song
func (c *Client) GetSongInfo(songMid string) (*models.SongInfo, error) {
	params := map[string]interface{}{
		"songMid": []string{songMid},
	}

	data, err := c.RequestCGI("music.trackInfo.UniformRuleCtrl", "GetTrackInfo", params)
	if err != nil {
		return nil, fmt.Errorf("failed to get song info: %w", err)
	}

	var result struct {
		Tracks []struct {
			Mid      string `json:"mid"`
			Id       int64  `json:"id"`
			Name     string `json:"name"`
			Title    string `json:"title"`
			Interval int    `json:"interval"`
			Singer   []struct {
				Name string `json:"name"`
				Mid  string `json:"mid"`
			} `json:"singer"`
			Album struct {
				Name string `json:"name"`
				Mid  string `json:"mid"`
			} `json:"album"`
			File struct {
				MediaMid  string `json:"media_mid"`
				Size128   int64  `json:"size_128"`
				Size320   int64  `json:"size_320"`
				SizeFlac  int64  `json:"size_flac"`
				SizeHires int64  `json:"size_hires"`
			} `json:"file"`
		} `json:"tracks"`
	}

	if err := json.Unmarshal(data, &result); err != nil {
		return nil, fmt.Errorf("failed to parse song info: %w", err)
	}

	if len(result.Tracks) == 0 {
		return nil, fmt.Errorf("song not found")
	}

	track := result.Tracks[0]
	artists := make([]string, len(track.Singer))
	for i, singer := range track.Singer {
		artists[i] = singer.Name
	}

	name := track.Name
	if name == "" {
		name = track.Title
	}

	return &models.SongInfo{
		Mid:      track.Mid,
		ID:       track.Id,
		Name:     name,
		Duration: float64(track.Interval),
		Artists:  artists,
		Album:    track.Album.Name,
		AlbumMid: track.Album.Mid,
		CoverUrl: buildCoverUrl(track.Album.Mid),
		File: models.SongFile{
			MediaMid: track.File.MediaMid,
			Size128:  track.File.Size128,
			Size320:  track.File.Size320,
			SizeFlac: track.File.SizeFlac,
			SizeHRes: track.File.SizeHires,
		},
	}, nil
}

// GetSongInfoBatch gets info for multiple songs
func (c *Client) GetSongInfoBatch(songMids []string) ([]models.SongInfo, error) {
	if len(songMids) == 0 {
		return nil, nil
	}

	params := map[string]interface{}{
		"songMid": songMids,
	}

	data, err := c.RequestCGI("music.trackInfo.UniformRuleCtrl", "GetTrackInfo", params)
	if err != nil {
		return nil, fmt.Errorf("failed to get song info batch: %w", err)
	}

	var result struct {
		Tracks []struct {
			Mid      string `json:"mid"`
			Id       int64  `json:"id"`
			Name     string `json:"name"`
			Title    string `json:"title"`
			Interval int    `json:"interval"`
			Singer   []struct {
				Name string `json:"name"`
				Mid  string `json:"mid"`
			} `json:"singer"`
			Album struct {
				Name string `json:"name"`
				Mid  string `json:"mid"`
			} `json:"album"`
			File struct {
				MediaMid  string `json:"media_mid"`
				Size128   int64  `json:"size_128"`
				Size320   int64  `json:"size_320"`
				SizeFlac  int64  `json:"size_flac"`
				SizeHires int64  `json:"size_hires"`
			} `json:"file"`
		} `json:"tracks"`
	}

	if err := json.Unmarshal(data, &result); err != nil {
		return nil, fmt.Errorf("failed to parse song info batch: %w", err)
	}

	var songs []models.SongInfo
	for _, track := range result.Tracks {
		artists := make([]string, len(track.Singer))
		for i, singer := range track.Singer {
			artists[i] = singer.Name
		}

		name := track.Name
		if name == "" {
			name = track.Title
		}

		songs = append(songs, models.SongInfo{
			Mid:      track.Mid,
			ID:       track.Id,
			Name:     name,
			Duration: float64(track.Interval),
			Artists:  artists,
			Album:    track.Album.Name,
			AlbumMid: track.Album.Mid,
			CoverUrl: buildCoverUrl(track.Album.Mid),
			File: models.SongFile{
				MediaMid: track.File.MediaMid,
				Size128:  track.File.Size128,
				Size320:  track.File.Size320,
				SizeFlac: track.File.SizeFlac,
				SizeHRes: track.File.SizeHires,
			},
		})
	}

	return songs, nil
}

// SearchSongs searches for songs by keyword
func (c *Client) SearchSongs(keyword string, page, pageSize int) ([]models.SongInfo, int, error) {
	if pageSize <= 0 {
		pageSize = 30
	}
	if page < 1 {
		page = 1
	}

	params := map[string]interface{}{
		"searchid":     crypto.GenerateSearchID(),
		"query":        keyword,
		"page_num":     page,
		"num_per_page": pageSize,
		"search_type":  0, // 0: songs
	}

	data, err := c.RequestCGI("music.search.SearchCgiService", "DoSearchForQQMusicDesktop", params)
	if err != nil {
		return nil, 0, fmt.Errorf("failed to search songs: %w", err)
	}

	var result struct {
		Body struct {
			Song struct {
				TotalNum int `json:"totalnum"`
				List     []struct {
					Mid      string `json:"mid"`
					Id       int64  `json:"id"`
					Name     string `json:"name"`
					Interval int    `json:"interval"`
					Singer   []struct {
						Name string `json:"name"`
						Mid  string `json:"mid"`
					} `json:"singer"`
					Album struct {
						Name string `json:"name"`
						Mid  string `json:"mid"`
					} `json:"album"`
					File struct {
						MediaMid  string `json:"media_mid"`
						Size128   int64  `json:"size_128"`
						Size320   int64  `json:"size_320"`
						SizeFlac  int64  `json:"size_flac"`
						SizeHires int64  `json:"size_hires"`
					} `json:"file"`
				} `json:"list"`
			} `json:"song"`
		} `json:"body"`
	}

	if err := json.Unmarshal(data, &result); err != nil {
		return nil, 0, fmt.Errorf("failed to parse search results: %w", err)
	}

	var songs []models.SongInfo
	for _, track := range result.Body.Song.List {
		artists := make([]string, len(track.Singer))
		for i, singer := range track.Singer {
			artists[i] = singer.Name
		}

		songs = append(songs, models.SongInfo{
			Mid:      track.Mid,
			ID:       track.Id,
			Name:     track.Name,
			Duration: float64(track.Interval),
			Artists:  artists,
			Album:    track.Album.Name,
			AlbumMid: track.Album.Mid,
			CoverUrl: buildCoverUrl(track.Album.Mid),
			File: models.SongFile{
				MediaMid: track.File.MediaMid,
				Size128:  track.File.Size128,
				Size320:  track.File.Size320,
				SizeFlac: track.File.SizeFlac,
				SizeHRes: track.File.SizeHires,
			},
		})
	}

	return songs, result.Body.Song.TotalNum, nil
}

// GetSongLyric gets the lyrics for a song
func (c *Client) GetSongLyric(songMid string) (string, error) {
	c.mu.RLock()
	cookies := c.cookies
	gtk := c.gtk
	uin := c.uin
	c.mu.RUnlock()

	debugLog("[GetSongLyric] songMid=%s", songMid)

	// Rain120/qq-music-api uses the traditional lyric endpoint. In practice
	// it can include translations when GetPlayLyricInfo only returns main LRC.
	traditional, traditionalErr := c.getSongLyricTraditional(songMid, cookies, gtk, uin)
	if traditionalErr == nil && strings.TrimSpace(traditional.lrc) != "" && strings.TrimSpace(traditional.tlyric) != "" {
		return marshalLyricData(traditional.lrc, traditional.tlyric, traditional.rlyric), nil
	}
	if traditionalErr != nil {
		debugLog("[GetSongLyric] Traditional method failed: %v", traditionalErr)
	}

	cgi, cgiErr := c.getSongLyricCGI(songMid)
	if cgiErr != nil {
		debugLog("[GetSongLyric] CGI method failed: %v", cgiErr)
	}

	combined := mergeLyricPayloads(traditional, cgi)
	download, downloadErr := c.getSongLyricDownload(songMid, combined.songID)
	if downloadErr != nil {
		debugLog("[GetSongLyric] Lyric download method failed: %v", downloadErr)
	}
	combined = mergeLyricPayloads(combined, download)
	if strings.TrimSpace(combined.lrc) != "" && strings.TrimSpace(combined.tlyric) != "" {
		return marshalLyricData(combined.lrc, combined.tlyric, combined.rlyric), nil
	}

	if strings.TrimSpace(combined.lrc) != "" {
		return marshalLyricData(combined.lrc, combined.tlyric, combined.rlyric), nil
	}

	if traditionalErr != nil {
		return "", traditionalErr
	}
	if cgiErr != nil {
		return "", cgiErr
	}

	return marshalLyricData("", "", ""), nil
}

type lyricPayload struct {
	lrc    string
	tlyric string
	rlyric string
	songID int64
}

func (c *Client) getSongLyricCGI(songMid string) (lyricPayload, error) {
	params := map[string]interface{}{
		"songMID": songMid,
		"songID":  0,
	}
	data, err := c.RequestCGI("music.musichallSong.PlayLyricInfo", "GetPlayLyricInfo", params)
	if err != nil {
		return lyricPayload{}, err
	}

	payload := lyricPayload{
		lrc:    findLyricString(data, lyricFieldKeys),
		tlyric: findLyricString(data, translationFieldKeys),
		rlyric: findLyricString(data, romanFieldKeys),
	}
	var result struct {
		SongID int64 `json:"songID"`
	}
	if err := json.Unmarshal(data, &result); err == nil {
		payload.songID = result.SongID
	}
	debugLog("[GetSongLyric] CGI lyric fields (songID=%d, lrc=%d, trans=%d, roma=%d)", payload.songID, len(payload.lrc), len(payload.tlyric), len(payload.rlyric))
	if strings.TrimSpace(payload.lrc) == "" {
		debugLog("[GetSongLyric] CGI method returned empty lyric. Data: %s", string(data[:min(300, len(data))]))
	}
	return payload, nil
}

func (c *Client) getSongLyricTraditional(songMid, cookies string, gtk int, uin int64) (lyricPayload, error) {
	reqURL := "https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg"
	resp, err := c.httpClient.R().
		SetHeader("Referer", "https://c.y.qq.com/").
		SetHeader("Host", "c.y.qq.com").
		SetHeader("Cookie", cookies).
		SetQueryParam("songmid", songMid).
		SetQueryParam("format", "json").
		SetQueryParam("outCharset", "utf-8").
		SetQueryParam("pcachetime", fmt.Sprintf("%d", timeNowMillis())).
		SetQueryParam("g_tk", fmt.Sprintf("%d", gtk)).
		SetQueryParam("loginUin", fmt.Sprintf("%d", uin)).
		SetQueryParam("hostUin", "0").
		SetQueryParam("inCharset", "utf8").
		SetQueryParam("notice", "0").
		SetQueryParam("platform", "yqq.json").
		SetQueryParam("needNewCode", "0").
		Get(reqURL)

	if err != nil {
		return lyricPayload{}, fmt.Errorf("failed to get lyrics via traditional method: %w", err)
	}

	debugLog("[GetSongLyric] Traditional Response: %s", string(resp.Body()[:min(500, len(resp.Body()))]))

	var result map[string]interface{}
	if err := json.Unmarshal(resp.Body(), &result); err != nil {
		return lyricPayload{}, fmt.Errorf("failed to parse traditional lyrics response: %w", err)
	}

	payload := lyricPayload{
		lrc:    findLyricValue(result, lyricFieldKeys),
		tlyric: findLyricValue(result, translationFieldKeys),
		rlyric: findLyricValue(result, romanFieldKeys),
	}
	debugLog("[GetSongLyric] Traditional lyric fields (lrc=%d, trans=%d, roma=%d)", len(payload.lrc), len(payload.tlyric), len(payload.rlyric))

	return payload, nil
}

func (c *Client) getSongLyricDownload(songMid string, songID int64) (lyricPayload, error) {
	if songID <= 0 {
		info, err := c.GetSongInfo(songMid)
		if err != nil {
			return lyricPayload{}, fmt.Errorf("failed to get song id for lyric download: %w", err)
		}
		if info != nil {
			songID = info.ID
		}
	}
	if songID <= 0 {
		return lyricPayload{}, fmt.Errorf("missing song id for lyric download")
	}

	resp, err := c.httpClient.R().
		SetHeader("Referer", "https://c.y.qq.com/").
		SetHeader("Host", "c.y.qq.com").
		SetFormData(map[string]string{
			"version":     "15",
			"miniversion": "82",
			"lrctype":     "4",
			"musicid":     fmt.Sprintf("%d", songID),
		}).
		Post("https://c.y.qq.com/qqmusic/fcgi-bin/lyric_download.fcg")
	if err != nil {
		return lyricPayload{}, fmt.Errorf("failed to download QQ lyric package: %w", err)
	}

	body := strings.ReplaceAll(string(resp.Body()), "<!--", "")
	body = strings.ReplaceAll(body, "-->", "")
	payload := lyricPayload{
		lrc:    normalizeQQDownloadedLyric(extractQQDownloadedLyric(body, "content")),
		tlyric: normalizeQQDownloadedLyric(extractQQDownloadedLyric(body, "contentts")),
		rlyric: normalizeQQDownloadedLyric(extractQQDownloadedLyric(body, "contentroma")),
		songID: songID,
	}
	debugLog("[GetSongLyric] Lyric download fields (songID=%d, lrc=%d, trans=%d, roma=%d)", songID, len(payload.lrc), len(payload.tlyric), len(payload.rlyric))
	if strings.TrimSpace(payload.tlyric) != "" {
		debugLog("[GetSongLyric] Lyric download translation preview: %s", previewLyric(payload.tlyric, 120))
	}

	return payload, nil
}

func mergeLyricPayloads(primary, fallback lyricPayload) lyricPayload {
	if strings.TrimSpace(primary.lrc) == "" {
		primary.lrc = fallback.lrc
	}
	if strings.TrimSpace(primary.tlyric) == "" {
		primary.tlyric = fallback.tlyric
	}
	if strings.TrimSpace(primary.rlyric) == "" {
		primary.rlyric = fallback.rlyric
	}
	if primary.songID <= 0 {
		primary.songID = fallback.songID
	}
	return primary
}

func marshalLyricData(lrc, tlyric, rlyric string) string {
	data := struct {
		Lrc    string `json:"lrc"`
		Tlyric string `json:"tlyric"`
		Rlyric string `json:"rlyric"`
	}{
		Lrc:    decodeBase64Lyric(lrc),
		Tlyric: decodeBase64Lyric(tlyric),
		Rlyric: decodeBase64Lyric(rlyric),
	}
	debugLog("[GetSongLyric] Decoded lyric fields (lrc=%d, trans=%d, roma=%d)", len(data.Lrc), len(data.Tlyric), len(data.Rlyric))

	jsonBytes, err := json.Marshal(data)
	if err != nil {
		return data.Lrc
	}
	return string(jsonBytes)
}

func decodeBase64Lyric(value string) string {
	value = strings.TrimSpace(value)
	if value == "" {
		return ""
	}

	encodings := []*base64.Encoding{
		base64.StdEncoding,
		base64.RawStdEncoding,
		base64.URLEncoding,
		base64.RawURLEncoding,
	}
	for _, encoding := range encodings {
		if decoded, err := encoding.DecodeString(value); err == nil {
			return string(decoded)
		}
	}

	if decoded, err := base64.StdEncoding.DecodeString(padBase64(value)); err == nil {
		return string(decoded)
	}

	return value
}

var lyricFieldKeys = []string{"lyric", "lrc", "lyricContent", "content"}
var translationFieldKeys = []string{"trans", "transLyric", "translyric", "trans_lyric", "trans_lrc", "tlyric", "translate", "translation"}
var romanFieldKeys = []string{"roma", "roman", "romaLyric", "romalyric", "roma_lyric", "roma_lrc", "rlyric"}

func findLyricString(raw []byte, keys []string) string {
	var value interface{}
	if err := json.Unmarshal(raw, &value); err != nil {
		return ""
	}
	return findLyricValue(value, keys)
}

func findLyricValue(value interface{}, keys []string) string {
	keySet := make(map[string]struct{}, len(keys))
	for _, key := range keys {
		keySet[strings.ToLower(key)] = struct{}{}
	}
	return findLyricValueRecursive(value, keys, keySet)
}

func findLyricValueRecursive(value interface{}, orderedKeys []string, keySet map[string]struct{}) string {
	switch typed := value.(type) {
	case map[string]interface{}:
		for _, key := range orderedKeys {
			if child, ok := getCaseInsensitive(typed, key); ok {
				if text, ok := child.(string); ok && strings.TrimSpace(text) != "" {
					return text
				}
				if text := findLyricValueRecursive(child, orderedKeys, keySet); text != "" {
					return text
				}
			}
		}
		for key, child := range typed {
			if _, isLyricKey := keySet[strings.ToLower(key)]; isLyricKey {
				continue
			}
			if text := findLyricValueRecursive(child, orderedKeys, keySet); text != "" {
				return text
			}
		}
	case []interface{}:
		for _, child := range typed {
			if text := findLyricValueRecursive(child, orderedKeys, keySet); text != "" {
				return text
			}
		}
	}

	return ""
}

func getCaseInsensitive(values map[string]interface{}, key string) (interface{}, bool) {
	if value, ok := values[key]; ok {
		return value, true
	}
	lowerKey := strings.ToLower(key)
	for currentKey, value := range values {
		if strings.ToLower(currentKey) == lowerKey {
			return value, true
		}
	}
	return nil, false
}

func padBase64(value string) string {
	remainder := len(value) % 4
	if remainder == 0 {
		return value
	}
	return value + strings.Repeat("=", 4-remainder)
}

var qqVerbatimLineRegex = regexp.MustCompile(`^\[(\d+),(\d+)\](.*)$`)
var qqVerbatimWordTimeRegex = regexp.MustCompile(`\(\d+,\d+\)`)
var qqEmptyTranslationLineRegex = regexp.MustCompile(`^(\[\d{1,2}:\d{1,2}(?:\.\d{1,3})?\])//$`)

func extractQQDownloadedLyric(body, nodeName string) string {
	re := regexp.MustCompile(`(?s)<` + regexp.QuoteMeta(nodeName) + `\b.*?<!\[CDATA\[(.*?)\]\]>`)
	matches := re.FindStringSubmatch(body)
	if len(matches) < 2 {
		return ""
	}
	return strings.TrimSpace(matches[1])
}

func normalizeQQDownloadedLyric(value string) string {
	value = strings.TrimSpace(value)
	if value == "" || isHexString(value) {
		return ""
	}

	lines := strings.Split(strings.ReplaceAll(value, "\r\n", "\n"), "\n")
	converted := make([]string, 0, len(lines))
	for _, line := range lines {
		line = strings.TrimRight(line, "\r")
		if strings.TrimSpace(line) == "//" {
			converted = append(converted, "")
			continue
		}
		if match := qqEmptyTranslationLineRegex.FindStringSubmatch(line); len(match) == 2 {
			converted = append(converted, match[1])
			continue
		}

		if match := qqVerbatimLineRegex.FindStringSubmatch(line); len(match) == 4 {
			start, err := strconv.ParseInt(match[1], 10, 64)
			if err == nil {
				content := qqVerbatimWordTimeRegex.ReplaceAllString(match[3], "")
				converted = append(converted, formatLrcTimestamp(start)+content)
				continue
			}
		}

		converted = append(converted, line)
	}

	return strings.TrimSpace(strings.Join(converted, "\n"))
}

func isHexString(value string) bool {
	value = strings.TrimSpace(value)
	if value == "" || len(value)%2 != 0 {
		return false
	}
	for _, ch := range value {
		if (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F') {
			continue
		}
		return false
	}
	return true
}

func formatLrcTimestamp(milliseconds int64) string {
	if milliseconds < 0 {
		milliseconds = 0
	}
	totalSeconds := milliseconds / 1000
	minutes := totalSeconds / 60
	seconds := totalSeconds % 60
	centiseconds := (milliseconds % 1000) / 10
	return fmt.Sprintf("[%02d:%02d.%02d]", minutes, seconds, centiseconds)
}

func previewLyric(value string, limit int) string {
	value = strings.ReplaceAll(value, "\r", "")
	value = strings.ReplaceAll(value, "\n", " | ")
	if len(value) <= limit {
		return value
	}
	return value[:limit]
}

func timeNowMillis() int64 {
	return time.Now().UnixNano() / int64(time.Millisecond)
}

// GetRecommendSongs gets daily recommended songs (similar to personal FM)
func (c *Client) GetRecommendSongs() ([]models.SongInfo, error) {
	params := map[string]interface{}{
		"id":                99,
		"num":               30,
		"from":              0,
		"scene":             0,
		"song_ids":          []int{},
		"ext":               map[string]string{"bluetooth": ""},
		"should_count_down": 1,
	}

	data, err := c.RequestCGI("music.radioProxy.MbTrackRadioSvr", "get_radio_track", params)
	if err != nil {
		return nil, fmt.Errorf("failed to get recommend songs: %w", err)
	}

	var result struct {
		Tracks []struct {
			Mid      string `json:"mid"`
			Id       int64  `json:"id"`
			Name     string `json:"name"`
			Interval int    `json:"interval"`
			Singer   []struct {
				Name string `json:"name"`
				Mid  string `json:"mid"`
			} `json:"singer"`
			Album struct {
				Name string `json:"name"`
				Mid  string `json:"mid"`
			} `json:"album"`
			File struct {
				MediaMid  string `json:"media_mid"`
				Size128   int64  `json:"size_128"`
				Size320   int64  `json:"size_320"`
				SizeFlac  int64  `json:"size_flac"`
				SizeHires int64  `json:"size_hires"`
			} `json:"file"`
		} `json:"tracks"`
	}

	if err := json.Unmarshal(data, &result); err != nil {
		return nil, fmt.Errorf("failed to parse recommend songs: %w", err)
	}

	var songs []models.SongInfo
	for _, track := range result.Tracks {
		artists := make([]string, len(track.Singer))
		for i, singer := range track.Singer {
			artists[i] = singer.Name
		}

		songs = append(songs, models.SongInfo{
			Mid:      track.Mid,
			ID:       track.Id,
			Name:     track.Name,
			Duration: float64(track.Interval),
			Artists:  artists,
			Album:    track.Album.Name,
			AlbumMid: track.Album.Mid,
			CoverUrl: buildCoverUrl(track.Album.Mid),
			File: models.SongFile{
				MediaMid: track.File.MediaMid,
				Size128:  track.File.Size128,
				Size320:  track.File.Size320,
				SizeFlac: track.File.SizeFlac,
				SizeHRes: track.File.SizeHires,
			},
		})
	}

	return songs, nil
}

// GetAvailableQuality returns the best available quality for a song
func (c *Client) GetAvailableQuality(song *models.SongInfo) models.AudioQuality {
	// Check from highest to lowest quality
	if song.File.SizeHRes > 0 {
		return models.QualityHiRes
	}
	if song.File.SizeFlac > 0 {
		return models.QualitySQ
	}
	if song.File.Size320 > 0 {
		return models.QualityHQ
	}
	return models.QualityStandard
}
