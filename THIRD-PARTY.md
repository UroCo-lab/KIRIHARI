# 依存関係と配布条件

KIRIHARIの自作コード・文書・練習用素材には [MIT License](LICENSE) を適用します。著作権表示は `Copyright (c) 2026 UroCo` です。以下の外部部品とアイコンには、それぞれの条件が適用されます。

| 項目 | 1.8.17での扱い |
| --- | --- |
| .NET Framework / WPF / Windows Forms | Windows上の既存ランタイムを使用。 |
| Microsoft Edge WebView2 | Evergreen Runtimeでローカル編集画面とプレビューを表示。SDK 1.0.2903.40のx64ローダーと.NET用DLLを同梱します。SDKのBSD 3条項型ライセンスと告知は `licenses/WebView2-LICENSE.txt` と `licenses/WebView2-NOTICE.txt` に原文のまま収録します。RuntimeはMicrosoftから取得します。 |
| C# コンパイラ | ビルド時のみ Windows の .NET Framework 同梱のものを使用。 |
| フォント | 画面はWindowsのSegoe UI、Yu Gothic UI、Meiryoを使用します。PDFはWindows版BIZ UDゴシックと一部にArialをサブセット埋め込みしています。独立したフォントファイルは配布対象外です。 |
| アプリアイコン | `src/assets/KIRIHARI.png` と `KIRIHARI.ico` は、KIRIHARIに同梱する形で配布・再配布できます。アイコン単体の利用・改変はMITの許諾範囲外です。 |
| FFmpeg / FFprobe | 実行ファイルを別プロセスとして起動します。初回準備ではGyanの9.0.2 essentials ZIPを利用者の操作後に取得します。このビルドは `--enable-gpl --enable-version3` を使用し、配布元のGPL v3本文・READMEと取得元の記録を保存します。KIRIHARIの配布ZIPにFFmpegの実行ファイルは含まれません。 |

PDFはWindows版BIZ UDゴシックの閲覧・印刷向け埋め込み条件に合わせ、閲覧・印刷を許可し、文書の編集を制限しています。マニュアルの内容を変更する場合は、ソースZIPの `docs/make-*.py` を利用できます。 [Microsoftのフォント再配布FAQ](https://learn.microsoft.com/en-us/typography/fonts/font-faq)

初回準備で取得するMicrosoftのインストーラーはMicrosoftの配布リンクを使用し、Windowsの署名検証とMicrosoft Corporationの発行者名を確認してから起動する。Microsoftの使用許諾とインストール画面は利用者が操作する。自動取得するFFmpegのバージョン・URL・SHA-256は `src/Setup.cs` に固定し、取得先の変更時は再検証する。

FFmpegを同梱する改変版を配布する場合は、採用したビルドのライセンスと、対応するソースの提供条件を確認してください。H.264などのコーデック特許の扱いは著作権ライセンスと別に適用されます。 [Gyanの9.0.2配布ページ](https://github.com/GyanD/codexffmpeg/releases/tag/9.0.2) · [FFmpegのライセンス案内](https://www.ffmpeg.org/legal.html)

Windows での再生とフレームレートの情報源は Microsoft の API に従う。[WPF Multimedia Overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/multimedia-overview) · [System.Video.FrameRate](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-video-framerate)
