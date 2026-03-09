# [Snow Panic / GIF主経路] ノア確認用プレビュー運用完成

## 変更ファイル一覧

---

### file path: Assets/Editor/SnowPanicVideoPipelineSelfTest.cs

**置換1（静的変数追加）**
```csharp
// 置換前
    static string _previewPath;
    static bool _previewCreated;
    static long _previewGifSize;
    static string _previewGifDriveLink;
    static string _previewType;
    static long _previewSizeBytes;

// 置換後
    static string _previewPath;
    static bool _previewCreated;
    static long _previewGifSize;
    static string _previewGifDriveLink;
    static string _previewType;
    static long _previewSizeBytes;
    static bool _previewFallbackUsed;
    static string _gifPath;
    static string _ffmpegPathUsed;
    static bool _ffmpegAvailable;
    static string _previewStatus;
```

**置換2（GeneratePreview 全体を gif主経路に書き換え）**
- ffmpeg があれば mp4→snow_test_latest.gif を第一優先で生成
- gif 成功時: PREVIEW_READY, preview_fallback_used=false
- gif 失敗時: contact_sheet (preview_strip.png) を fallback
- contact_sheet 成功時: PREVIEW_FALLBACK_READY
- 出力ファイル: /Recordings/snow_test_latest.gif, /Recordings/preview_strip.png
- ログ: preview_type, gif_path, gif_exists, gif_size_bytes, preview_fallback_used, ffmpeg_path, ffmpeg_available

**置換3（session 書き込みに新フィールド追加）**
```csharp
// 追加
            sb.AppendLine("preview_type=" + (_previewType ?? "none"));
            sb.AppendLine("gif_path=" + (_gifPath ?? (_previewType == "gif" ? _previewPath : "") ?? ""));
            sb.AppendLine("gif_exists=" + (_previewType == "gif" && !string.IsNullOrEmpty(_previewPath) && File.Exists(_previewPath)).ToString().ToLower());
            sb.AppendLine("gif_size_bytes=" + (_previewType == "gif" ? _previewGifSize : 0));
            sb.AppendLine("preview_fallback_used=" + _previewFallbackUsed.ToString().ToLower());
            sb.AppendLine("ffmpeg_path=" + (_ffmpegPathUsed ?? ""));
            sb.AppendLine("ffmpeg_available=" + _ffmpegAvailable.ToString().ToLower());
            sb.AppendLine("preview_status=" + (_previewStatus ?? "PREVIEW_ERROR"));
```

**置換4（セッションリセット時に新変数を初期化）**
```csharp
// 追加
        _previewFallbackUsed = false;
        _gifPath = "";
        _ffmpegPathUsed = "";
        _ffmpegAvailable = false;
        _previewStatus = "PREVIEW_ERROR";
```

---

### file path: Assets/Scripts/Editor/SnowLoopNoaReportAutoCopy.cs

**置換（BuildVideoPreviewForNoaSection: gif 中心の出力）**
- preview_type: gif / contact_sheet / none
- gif_path, gif_exists, gif_size_bytes を追加
- preview_fallback_used, ffmpeg_path, ffmpeg_available, preview_status を追加
- VIDEO PREVIEW FOR NOA で gif を最優先表示

---

## 変更理由

- **現状**: mp4 と contact_sheet は生成されるが gif が主経路になっていなかった
- **目的**: ノアが「見て気持ちいいか」を毎回すぐ確認できる状態にする
- **方針**: gif を第一優先、失敗時のみ contact_sheet を fallback

---

## 想定ログ例

**gif 成功時**
```
[VideoPipeline] preview_start ffmpeg_path=/opt/homebrew/bin/ffmpeg ffmpeg_available=true
[VideoPipeline] preview_done preview_type=gif gif_path=/path/Recordings/snow_test_latest.gif gif_exists=true gif_size_bytes=123456 preview_fallback_used=false
```

**gif 失敗・contact_sheet 成功時**
```
[VideoPipeline] preview_start ffmpeg_path=/opt/homebrew/bin/ffmpeg ffmpeg_available=true
[VideoPipeline] preview_gif_failed ex=...
[VideoPipeline] preview_done preview_type=contact_sheet gif_path= gif_exists=false preview_fallback_used=true
```

**VIDEO PREVIEW FOR NOA 出力例**
```
preview_type=gif
preview_path=/path/Recordings/snow_test_latest.gif
gif_path=/path/Recordings/snow_test_latest.gif
gif_exists=true
gif_size_bytes=123456
preview_fallback_used=false
ffmpeg_path=/opt/homebrew/bin/ffmpeg
ffmpeg_available=true
preview_status=PREVIEW_READY
preview_exists=true
preview_size_bytes=123456
preview_drive_link=...
```

---

## 確認手順

1. **Self Test を実行**（SnowPanic → Self Test）
2. 録画終了後、`Recordings/snow_test_latest.mp4` と `Recordings/snow_test_latest.gif` が生成されることを確認
3. gif がない場合、`Recordings/preview_strip.png` が fallback で生成されることを確認
4. 停止 → ASSI レポートに `preview_type=gif` `gif_exists=true` が出ることを確認
5. ノアにレポートを貼り、gif のパスでプレビュー確認

---

## 成功条件

- 実行後に local mp4 と local gif が揃う
- ログで gif 生成の成否が分かる
- gif 失敗時のみ contact_sheet が fallback として出力される
- ノアが毎回 gif で挙動確認できる
