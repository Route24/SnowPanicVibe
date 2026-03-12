# VideoPipeline パス一覧

## 主要な場所（macOS の場合）

### 1. mp4 出力先（プロジェクト内）

```
/Users/kenichinishi/unity/SnowPanicVibe/Recordings/
```

**中身:**
- `snow_test_tmp_<sessionId>.mp4` … 録画中の一時ファイル
- `snow_test_latest.mp4` … 確定後の最終ファイル
- `video_pipeline_assi_log.txt` … ログ
- `video_pipeline_session.txt` … セッション情報

**Cursor で開く:** 右の Explorer で `Recordings` フォルダを探す  
（プロジェクト直下・SNOWPANICVIBE の直下。`Assets` の外側）

---

### 2. ホームディレクトリ内

```
/Users/kenichinishi/SnowPanicVideos/
```

**中身:**
- `.vp_session.txt` … セッション情報
- `.vpselftest_active` … SelfTest 実行中フラグ
- `video_pipeline_run_<sessionId>.txt` … 各 run のログ

**Finder で開く:** 
1. Finder を開く
2. メニュー「移動」→「ホームフォルダへ」
3. `SnowPanicVideos` フォルダを開く

**ターミナルで開く:**
```bash
open ~/SnowPanicVideos
```

---

## プロジェクト内のフォルダ構造

```
SnowPanicVibe/
├── Assets/
├── Recordings/          ← ここ（mp4 と assi_log）
│   ├── snow_test_latest.mp4
│   ├── snow_test_tmp_vp_*.mp4
│   ├── video_pipeline_assi_log.txt
│   └── video_pipeline_session.txt
├── docs/
└── ...
```

**注意:** `Recordings` はプロジェクトルート直下。`Assets` の**中ではない**。

---

## パスが分からないとき

1. **Unity メニュー**  
   `SnowPanic → VideoPipeline → Ping` を実行  
   → Console にパスが表示される

2. **Finder で Recordings を開く**  
   Unity メニュー `Tools → ASSI Report` を開き  
   → 「フォルダを開く」ボタンがあれば押す
