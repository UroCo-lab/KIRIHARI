# はじめに（Windows版 1.8.18）

展開から最初のMP4書き出しまでを画像で確認できます。 [クイックスタート（PDF・2ページ）](KIRIHARI-クイックスタート.pdf)

## 必要なもの

- Windowsのx64 PC。動作確認はWindows 11 x64で実施しました。Windows 10とWindows on ARMは未検証です。
- .NET Framework 4.8以降。
- Microsoft Edge WebView2 Evergreen Runtime。
- `ffmpeg.exe` と `ffprobe.exe`。H.264の書き出しにはlibx264、音声にはAACエンコーダーを利用します。

## 最初の1回

1. GitHub Releasesの **KIRIHARI-1.8.18-windows-x64.zip** をダウンロードします。`Source code` や `source.zip` は開発用です。
2. ZIPを右クリックし、**すべて展開**します。展開した `KIRIHARI` フォルダ全体を、自分が書き込める階層の浅いローカルフォルダに置きます。深い階層では編集用コピーの保存先がWindowsのパス長制限に達する場合があります。
3. **KIRIHARI.exe** を起動します。拡張子が非表示のPCでは **KIRIHARI** と表示されます。初回は **はじめの準備** が開き、必要な部品を確認します。
4. 不足する部品がある場合は **必要な部品を準備** を押します。FFmpegの展開・コピー・指定は自動です。.NET FrameworkとWebView2はMicrosoftのインストール画面に従って進めます。PCやアプリの再起動が必要になった場合は、準備画面に表示します。ダウンロードにはインターネット接続が必要です。
5. **KIRIHARIを始める** を押します。初回は **練習動画で試す** で同梱の猫の動画を開けます。次回以降は起動画面の **メニュー** にあります。自分の動画は **動画を選んで編集 → 動画を選ぶ → 編集を始める** で編集します。動画を複数読み込んだときはメイン動画を選びます。次回から準備画面は省略します。不足した部品が見つかったときは再び表示し、**設定 → はじめの準備** からも開けます。

準備中に閉じたり導入を中止した場合、完了扱いにはしません。次回の起動で再開できます。ダウンロード中に右上の閉じるボタンを押すと取得を中止します。Microsoftのインストール画面が開いている間は、その画面で中止してから準備画面を閉じてください。

## 手動で準備する場合

すでにFFmpegがある場合は、準備画面の **すでにあるFFmpegを選ぶ** で `ffmpeg.exe` を指定します。同じフォルダに `ffprobe.exe` が必要です。アプリの隣やPATHにある候補も自動で探し、両方が起動できることを確認します。

自動取得が使えない場合は、[Gyanの配布ページ](https://www.gyan.dev/ffmpeg/builds/)から `ffmpeg-release-essentials.zip` を取得・展開し、`bin` 内の `ffmpeg.exe` と `ffprobe.exe` を **両方とも** EXEの隣にコピーするか、準備画面から指定します。FFmpeg自体は本体ZIPに含みません。

WebView2のエラーが出る場合は、[Microsoft公式のダウンロードページ](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)から **Evergreen Standalone Installer / x64** をインストールして、アプリを起動し直します。Edgeブラウザー本体とは別のランタイムです。.NET Frameworkのエラーが出る場合は、[Microsoftの.NET Framework 4.8配布ページ](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48)のRuntimeを確認してください。依存物の取得にはインターネット接続が必要です。

準備画面は、Windowsの.NET 4.x上で、WebView2を読み込む前に表示します。.NETそのものが破損・欠落して準備画面も開かない場合は、上記Microsoftの配布ページから手動で修復・導入してください。職場のPCで導入を禁止されている場合は管理者に相談してください。

本版のEXEは未署名です。WindowsのSmartScreenなどが警告を表示することがあります。実行前に、配布元が公式のReleasesであることと、ZIPのSHA-256が公開値に一致することを確認してください。組織のPCで実行が禁止される場合は管理者に相談してください。

SHA-256はPowerShellで確認できます。公開ページの `SHA256SUMS.txt` にある同じファイル名の値と比較します。

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath .\KIRIHARI-1.8.18-windows-x64.zip
```

## サンプル動画を使う場合（任意）

画像の練習には、テロップの **画像** から `samples/lightweight/images` にある透過PNGとJPEGを選びます。

**動画を選んで編集 → 動画を選ぶ** から、同梱の `samples/lightweight` にあるMP4を選べます。猫は12秒、差し込みは7秒、光学ステージは2秒です。起動画面の **前の編集を開く** では同じフォルダの編集見本を選べます。3分の猫は任意の別添え **KIRIHARI-1.8.18-samples.zip** に入っています。

## 保存と更新

- アプリの `data/projects` に管理プロジェクト、`data/work` に自動保存や中間ファイル、`data/proxies` に軽い編集用動画、`data/webview` に画面設定などが保存されます。初期の完成動画の保存先は `output` です。
- 自動取得したFFmpegはアプリ内の `tools/ffmpeg` に保存します。`data/ffmpeg.txt` が場所の指定、`data/setup-v1.txt` が初回準備の完了記録です。Microsoftの部品はOS側へ導入されるため、アプリフォルダを削除しても残ります。
- 編集ファイルだけでは元動画を含みません。素材を移動・削除すると再開できなくなる場合があります。別PCへ移すときは、書き出し後に **元動画を含むプロジェクトZIP** を保存してください。
- 更新前にはプロジェクトZIPと完成動画を別の場所へ退避し、新しい版は別フォルダに展開します。元のアプリフォルダは、新しい版で再開できると確認するまで保管してください。
- 削除する場合は必要な素材・プロジェクト・完成動画を退避してからアプリフォルダを削除します。

本体のセットアップや通常の利用に、Git、Python、C#コンパイラは必要ありません。
