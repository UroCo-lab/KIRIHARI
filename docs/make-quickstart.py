"""Create the Japanese two-page quick start. Requires ReportLab and Pillow."""
from pathlib import Path
from shutil import copyfile
from PIL import Image
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.lib.colors import HexColor, white
from reportlab.lib.pagesizes import A4
from manual_pdf import manual_canvas

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'output/pdf'
ASSETS = ROOT / 'docs/images'
OUT.mkdir(parents=True, exist_ok=True)
pdfmetrics.registerFont(TTFont('JP', 'C:/Windows/Fonts/BIZ-UDGothicR.ttc', subfontIndex=0))
pdfmetrics.registerFont(TTFont('JPB', 'C:/Windows/Fonts/BIZ-UDGothicB.ttc', subfontIndex=0))
pdfmetrics.registerFont(TTFont('Latin', 'C:/Windows/Fonts/arial.ttf'))
pdfmetrics.registerFont(TTFont('LatinB', 'C:/Windows/Fonts/arialbd.ttf'))
INK, TEAL, PALE, GRAY, LINE, ORANGE = map(HexColor,
    ['#20313b', '#087f87', '#eff8f7', '#5c6e77', '#d6e5e6', '#ed9e28'])
W, H = A4
FILE = OUT / 'KIRIHARI-クイックスタート.pdf'
c = manual_canvas(FILE, A4)
c.setTitle('KIRIHARI クイックスタート - はじめ方からMP4書き出しまで')
c.setAuthor('KIRIHARI')
c.setCreator('KIRIHARI manual')

def text(x, y, label, size=11, bold=False, color=INK, latin=False):
    c.setFillColor(color)
    c.setFont(('LatinB' if bold else 'Latin') if latin else ('JPB' if bold else 'JP'), size)
    c.drawString(x, y, label)

def box(x, y, w, h, fill=PALE, radius=8):
    c.setFillColor(fill); c.setStrokeColor(LINE)
    c.roundRect(x, y, w, h, radius, fill=1, stroke=1)

def button(x, y, label, w=None, size=11):
    if w is None: w = pdfmetrics.stringWidth(label, 'JPB', size)+20
    c.setFillColor(TEAL)
    c.roundRect(x, y, w, 28, 5, fill=1, stroke=0)
    text(x+10, y+9, label, size, True, white)
    return w

def arrow(x, y, end, color=TEAL):
    c.setStrokeColor(color); c.setLineWidth(1.8)
    c.line(x, y, end, y)
    c.line(end-6, y+4, end, y); c.line(end-6, y-4, end, y)

def step(n, title, y, detail):
    c.setFillColor(TEAL); c.circle(47, y+4, 13, fill=1, stroke=0)
    text(43, y, str(n), 13, True, white, True)
    text(69, y, title, 16, True)
    text(69, y-24, detail, 10.5, color=GRAY)

def picture(name, x, y, w):
    p = ASSETS / name
    image = Image.open(p)
    h = w * image.height / image.width
    c.drawImage(str(p), x, y, width=w, height=h, mask='auto')
    return h

def header(page, subtitle):
    text(35, 791, 'KIRIHARI', 22, True, TEAL, True)
    text(176, 792, 'クイックスタート', 20, True)
    text(35, 765, subtitle, 10.5, color=GRAY)
    c.setStrokeColor(LINE); c.line(35, 749, 560, 749)
    text(35, 24, 'Windows 10 / 11（64ビット） | 1.8.17', 8.5, color=GRAY)
    text(522, 24, f'{page} / 2', 9, color=GRAY, latin=True)

header(1, 'まずは、短い動画を切って文字を重ね、MP4を1本作ってみましょう。')
step(1, '展開して、初回の準備をする', 711,
     '本体は KIRIHARI-1.8.17-windows-x64.zip。Source codeは選びません。')
picture('setup-window.png', 38, 465, 206)
text(269, 658, 'ZIPを右クリック → すべて展開 → 展開', 11, True)
text(269, 637, '「ドキュメント」など、自分のフォルダに置きます。', 10)
text(269, 608, 'KIRIHARI.exe をダブルクリック', 11, True)
text(269, 587, 'EXEだけを移動せず、フォルダごと残します。', 10)
button(269, 548, '必要な部品を準備', 153)
text(269, 527, 'Microsoftの画面が出たら案内に従い、完了を待ちます。', 9.5)
button(269, 489, 'KIRIHARIを始める', 153)
text(269, 469, '次回から準備画面は省略。再起動の案内があれば従います。', 9)

step(2, '動画を選んで、作業を始める', 431,
     '起動画面の「動画を選んで編集」を押します。名前の入力は任意です。')
picture('new-project-dialog.png', 38, 216, 210)
button(276, 361, '動画を選ぶ', 112)
text(276, 339, '使いたい動画を選び、「開く」を押します。', 10.5)
text(276, 311, '読み込んだ動画の名前をクリック', 11, True)
text(276, 290, 'メインとなる動画を1本選びます。', 10.5)
button(276, 251, '編集を始める', 112)
text(276, 225, '起動画面から練習動画（猫・12秒）も開けます。', 10)

step(3, '完成動画に残すところをドラッグ', 186,
     '「カット」行の空き部分で、始点から終点までマウスを押したまま動かします。')
picture('quick-cut.png', 35, 49, 525)
text(35, 37, '白い帯だけが残ります。長さは両端をドラッグして調整。不要な帯は右クリック → 削除。', 9, color=GRAY)
c.showPage()

header(2, '文字を入れて確認したら、完成動画と編集内容をそれぞれ保存します。')
step(4, '説明の文字（テロップ）を入れる', 711,
     '白いカット帯をクリックし、文字を出したい位置に赤い線を合わせます。')
text(69, 668, '「文字を入れる」を押すと、プレビュー上ですぐに入力できます。', 10.5, color=GRAY)
picture('quick-cat-frame.png', 38, 470, 302)
# Illustration of a caption on the real sample frame, rather than a fabricated UI screenshot.
box(68, 559, 183, 31, HexColor('#20313b'), 4)
text(81, 568, 'ここを確認', 16, True, white)
text(38, 453, '文字を重ねたイメージ（サンプル動画）', 9, color=GRAY)
button(366, 611, '文字を入れる', 121)
text(366, 588, '文字を入力します。', 11)
text(366, 563, '位置は文字の枠をドラッグ。', 10)
text(366, 544, '大きさや色は上の設定で変更。', 10)
button(366, 503, '完了', 95)
text(366, 480, 'テロップの長さも、帯の両端で調整。', 9)

step(5, '再生で確認して、MP4に書き出す', 408,
     '「編修後」に切り替え、再生します。「編修後の長さ」で完成尺を確認できます。')
picture('quick-play.png', 35, 344, 525)
text(35, 327, '確認用の再生速度は、書き出す動画の速度に影響しません。', 9, color=GRAY)
box(35, 222, 525, 89)
button(49, 263, '完成動画を作る（MP4）', 152, size=10)
arrow(210, 277, 232)
text(241, 272, '保存先と音声', 10.5, True)
arrow(322, 277, 341)
button(350, 263, '書き出す', 78)
arrow(437, 277, 457)
text(467, 272, 'MP4完成', 11, True, TEAL)
text(49, 244, '初期の保存先は、本体フォルダ内の「output」です。', 10)
text(49, 231, '音を入れる場合は、書き出し画面の「元の音声を入れる」を選びます。', 9, color=GRAY)

step(6, '編集内容も保存しておく', 181,
     '「編集を保存」を押すと、あとから続きの編集を開けます。')
box(35, 64, 525, 77)
text(49, 121, '見る・共有する', 10.5, True)
text(196, 121, 'MP4の完成動画を使います。', 10.5)
text(49, 100, '続きから編集する', 10.5, True)
text(196, 100, '次回は起動画面の一覧または「前の編集を開く」から。', 10.5)
text(49, 79, '素材ごと保管・移動する', 10.5, True)
text(196, 79, '「設定 → 素材を含むZIPで保存…」を使います。', 10)
text(35, 44, 'ZIP保存は持ち運び用です。普段の編集ファイルは「編集を保存」で更新します。', 9, color=GRAY)
c.showPage()
c.save()
copyfile(FILE, ROOT / 'docs/KIRIHARI-クイックスタート.pdf')
print('Created two-page quick start.')
