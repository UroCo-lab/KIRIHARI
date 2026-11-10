using System.ComponentModel;
using System.IO;

namespace Kirihari
{
    internal static class Manuals
    {
        internal static string Resolve(string home, string kind)
        {
            string name;
            switch (kind)
            {
                case "quickstart": name = "KIRIHARI-クイックスタート.pdf"; break;
                case "detail": name = "KIRIHARI-詳しい使い方.pdf"; break;
                default: throw new IOException("このマニュアルは開けません。");
            }
            string path = Path.Combine(home, "docs", name);
            if (!File.Exists(path)) throw new IOException("PDFが見つかりません。本体ZIPをすべて展開し直してください。");
            return path;
        }
        internal static void Open(string path)
        {
            try { Setup.Open(path); }
            catch (Win32Exception e)
            {
                throw new IOException("PDFを開けませんでした。PDFを表示するアプリの設定を確認してください。\n" + e.Message, e);
            }
        }
    }
}
