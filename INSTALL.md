# AIに導入を依頼する方へ

GitHubのURLまたは本体ZIPと一緒に、次のように依頼できます。

> このKIRIHARIをWindows PCに導入してください。まずINSTALL.mdを読み、書き込み可能な新しいフォルダへ導入し、初回準備から編集画面が開くところまで確認してください。既存のプロジェクトは残してください。改編するときはAI_EDITING.mdを読んでください。

1.8.18の確認にはローカルの本体ZIPを指定します。公開済みの版は [KIRIHARIのReleases](https://github.com/UroCo-lab/KIRIHARI/releases) から取得できます。

## 導入を行うAIへの手順

1. Windows x64であること、指定URL・ファイルが意図した配布元であることを確認する。動作確認はWindows 11 x64で実施し、Windows 10とWindows on ARMは未検証。GitHubの説明と本書を読む。配布物は「windows-x64.zip」が本体、「source.zip」が改編用、「samples.zip」が任意の動画素材。GitHubの自動生成「Source code」ZIPには実行ファイルがない。
2. 本体ZIPと、同じReleaseのSHA256SUMS.txtを取得する。認証情報をURLやログへ書かない。URLやファイルを読み取れないAIは、その制約を伝える。PCを操作できないAIは、導入済みと報告せず利用者へ短い操作手順を渡す。
3. 下記スクリプトを読んでから実行するか、チェックサムを検証してZIPをすべて新しいフォルダに展開する。EXE単体では動かない。既存のインストール先を上書きしない。通常の導入にGit・Python・開発SDKは不要。
4. KIRIHARI.exeを起動する。初回の「はじめの準備」で「必要な部品を準備」を押す。不足するFFmpegをアプリ内へ配置し、.NET Framework 4.8・WebView2が必要ならMicrosoftインストーラーへ進む。Microsoftの同意・管理者確認やWindowsの警告では利用者の判断が必要。組織の制限を回避しない。
5. 編集画面に進んだことを確認して一度終了し、同じEXEを再起動する。部品が揃い、準備画面が省略されることを確認する。「設定 → クイックスタート（PDF）」を案内し、実際に確認した範囲と未確認事項を報告する。

## インストールスクリプト

同梱の `install.ps1` はWindows PowerShell 5.1以降で使えます。ダウンロードと本体の展開までを行います。部品の導入はその後のアプリ初回画面が担当します。初期状態ではアプリを起動しません。起動も依頼されている場合は `-Launch` を付けます。

```powershell
# 本体ZIPを指定。同じフォルダのSHA256SUMS.txtを自動参照します。
.\install.ps1 -ArchivePath 'C:\Downloads\KIRIHARI-1.8.18-windows-x64.zip'

# 展開先を指定する場合。存在しないフォルダを選びます。
.\install.ps1 -ArchivePath 'C:\Downloads\KIRIHARI-1.8.18-windows-x64.zip' -Destination "$env:LOCALAPPDATA\Programs\KIRIHARI-1.8.18" -Launch
```

GitHubリポジトリURL、`releases/latest`、特定の`releases/tag/バージョン`を `-SourceUrl` に渡すと、Releaseの本体とチェックサムを取得します。直接のHTTPS ZIP URLにも対応します。直接URLの場合は別に確認した64桁の `-ExpectedSha256` を渡してください。Releaseチェックサムは転送・ファイルの一致確認で、配布元自体の信頼性を保証するものではありません。

既定の導入先は利用者のLocalAppData内 `Programs/KIRIHARI-日時` です。既存フォルダへの導入は拒否します。端末の実行ポリシーでスクリプトが止まる場合は、管理者の方針を確認するかZIPを手動で展開します。

## 改編したくなったら

本体と同じ版のソースZIPを別フォルダへ展開し、[AI_HANDOFF.md](AI_HANDOFF.md) と修正したい内容をAIに渡してください。ソース側の手順は [AI_EDITING.md](AI_EDITING.md) にあります。公開後はGitHubの同じ版のタグも使用できます。UIの色・配置は主に `src/ui/index.html`、動画処理はC#ソースで変更します。

更新時も新しいフォルダへ導入します。旧フォルダの `data`（編集情報等）と `output`（完成動画）は自動で移動しません。必要な編集は旧版で素材込みのプロジェクトZIPに保存してから、新版で開くと安全です。
