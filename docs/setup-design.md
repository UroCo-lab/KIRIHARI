# 初回準備の実装

## 起動順

`Program.Main` はWebView2を含む編集画面を作る前に、WPFの `SetupWindow` を開く。.NET Framework 4.8以降、WebView2 Runtime 131.0.2903.40以降、FFmpegの所在を確認する。初回、または不足があれば案内し、すべて準備できて利用者が開始を押したときだけ `data/setup-v1.txt` を保存する。

Windows 10/11標準の.NET 4.xを使って準備画面を動かし、4.8未満ならMicrosoftの導入画面へ進む。編集画面を呼ぶメソッドはJITのインライン展開を禁止し、先にSDK側の型を作らない。基盤の.NET自体が欠落・破損している場合は、この経路でも起動できず手動導入が必要。4.8未満の実機での起動は未検証。

## 取得と配置

- ボタン操作までは外部通信しない。FFmpegはGyan提供のGitHub Releaseを優先し、通信失敗時はGyanの同一版へ切り替える。両方とも同じ固定SHA-256を使う。
- 自動取得する版は9.0.2 essentials、SHA-256は `60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba`。配布ファイルの変更時は新しいソース・ハッシュを確認してコードを更新する。
- ZIPのパスを展開先に使わず、2つのEXE・LICENSE・READMEだけを固定名で取り出す。新しい専用フォルダに配置し、EXEの起動を確認してから `data/ffmpeg.txt` に場所を保存する。既存の動画やツールを上書きしない。
- FFmpegの新旧で削除された `-filter_complex_script` の有無をヘルプから調べ、非対応の版では `-/filter_complex` に切り替える。長いフィルターもコマンド文字数の上限を避けてファイルで渡す。
- Microsoftの.NET・WebView2インストーラーは公式リンクから取得する。WinVerifyTrustで署名を検証し、発行者がMicrosoft Corporationであることを確認してから画面付きで起動する。使用許諾の自動同意や自動再起動は行わない。
- .NET導入後はアプリを開き直す。終了コード3010/1641ならPCの再起動を案内する。インストーラー実行中の中止は、そのインストーラー側で行う。

## 検証

`build.ps1 -Test` に検出境界・初回と修復時の表示条件・完了記録・ハッシュ不一致・ZIPのパス逸脱防止・欠けたZIP・中止・未署名インストーラーの拒否・FFmpegの指定保存・画面描画を含める。テストに使う架空のEXEは実行しない。

必要時のみ `KIRIHARI.exe --setup-download-qa <検証用フォルダ> <samplesフォルダ>` で実配布先からの取得と書き出しを検証する。このコマンドはFFmpegを検証用フォルダに置き、Microsoftのインストーラーは署名確認だけで実行しない。通常のCIではネットワーク導入試験を行わない。

## 参照した配布元・仕様

- [.NET FrameworkのRelease値](https://learn.microsoft.com/en-us/dotnet/framework/migration-guide/how-to-determine-which-versions-are-installed)
- [.NET Framework 4.8](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48)
- [WebView2の検出・導入](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)
- [SDK 1.0.2903.40の必要Runtime](https://learn.microsoft.com/en-us/microsoft-edge/webview2/release-notes/sdk/1-0-2903-40)
- [Gyanの配布ページ](https://www.gyan.dev/ffmpeg/builds/)、[同じ提供者のGitHub Release](https://github.com/GyanD/codexffmpeg/releases/tag/9.0.2)
- [FFmpegのフィルターファイル指定変更](https://ffmpeg.org/pipermail/ffmpeg-cvslog/2024-January/140522.html)
